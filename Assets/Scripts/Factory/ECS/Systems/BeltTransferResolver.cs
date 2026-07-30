using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

public static class BeltTransferResolver
{
    private enum ResolutionState : byte
    {
        Unresolved,
        Resolving,
        Accepted,
        Rejected
    }

    private struct Snapshot
    {
        public Entity Entity;
        public Belt Belt;
        public int TargetIndex;
        public bool IsReady;
    }

    public static void Resolve(
        Entity[] entities,
        Belt[] belts,
        Dictionary<int2, int> nextWinnerByTarget,
        out int loopCount,
        out int readyRequestCount,
        out int acceptedTransferCount)
    {
        if (entities == null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        if (belts == null)
        {
            throw new ArgumentNullException(nameof(belts));
        }

        if (entities.Length != belts.Length)
        {
            throw new ArgumentException(
                "Entity and Belt snapshots must have identical lengths.");
        }

        int count = belts.Length;
        Snapshot[] snapshots = new Snapshot[count];
        Dictionary<int2, int> indexByCell =
            new Dictionary<int2, int>(count);

        for (int i = 0; i < count; i++)
        {
            snapshots[i] = new Snapshot
            {
                Entity = entities[i],
                Belt = belts[i],
                TargetIndex = -1
            };
            snapshots[i].Belt.IsLoop = false;
            indexByCell[snapshots[i].Belt.Cell] = i;
        }

        readyRequestCount = 0;
        for (int i = 0; i < count; i++)
        {
            Snapshot snapshot = snapshots[i];
            if (indexByCell.TryGetValue(
                    snapshot.Belt.NextCell,
                    out int targetIndex))
            {
                snapshot.TargetIndex = targetIndex;
            }

            snapshot.IsReady =
                snapshot.TargetIndex >= 0 &&
                snapshot.Belt.CurrentItem != Entity.Null &&
                snapshot.Belt.Progress >= 1f;
            if (snapshot.IsReady)
            {
                readyRequestCount++;
            }

            snapshots[i] = snapshot;
        }

        bool[] accepted = new bool[count];
        bool[] reservedTargets = new bool[count];
        loopCount = AcceptReadyLoops(
            snapshots,
            accepted,
            reservedTargets);

        int[] candidateForTarget = new int[count];
        Array.Fill(candidateForTarget, -1);
        SelectCandidates(
            snapshots,
            reservedTargets,
            nextWinnerByTarget,
            candidateForTarget);

        ResolutionState[] states = new ResolutionState[count];
        for (int i = 0; i < count; i++)
        {
            if (accepted[i])
            {
                states[i] = ResolutionState.Accepted;
            }
            else if (snapshots[i].IsReady &&
                     candidateForTarget[snapshots[i].TargetIndex] == i)
            {
                ResolveCandidate(
                    i,
                    snapshots,
                    candidateForTarget,
                    states);
            }
        }

        acceptedTransferCount = 0;
        for (int i = 0; i < count; i++)
        {
            if (states[i] == ResolutionState.Accepted)
            {
                accepted[i] = true;
            }

            if (!accepted[i])
            {
                continue;
            }

            acceptedTransferCount++;
            int targetIndex = snapshots[i].TargetIndex;
            if (!reservedTargets[targetIndex])
            {
                int2 targetCell = snapshots[targetIndex].Belt.Cell;
                int candidateCount = CountCandidatesForTarget(
                    snapshots,
                    targetIndex,
                    reservedTargets);
                if (candidateCount > 0)
                {
                    nextWinnerByTarget[targetCell] =
                        nextWinnerByTarget.TryGetValue(
                            targetCell,
                            out int previousWinner)
                            ? previousWinner + 1
                            : 1;
                }
            }
        }

        // Phase one clears every accepted source using the same snapshot.
        for (int i = 0; i < count; i++)
        {
            if (!accepted[i])
            {
                continue;
            }

            Snapshot source = snapshots[i];
            source.Belt.CurrentItem = Entity.Null;
            source.Belt.Progress = 0f;
            snapshots[i] = source;
        }

        // Phase two fills targets. This is the atomic commit boundary.
        for (int i = 0; i < count; i++)
        {
            if (!accepted[i])
            {
                continue;
            }

            int targetIndex = snapshots[i].TargetIndex;
            Snapshot target = snapshots[targetIndex];
            target.Belt.CurrentItem = belts[i].CurrentItem;
            target.Belt.Progress = 0f;
            snapshots[targetIndex] = target;
        }

        for (int i = 0; i < count; i++)
        {
            belts[i] = snapshots[i].Belt;
        }
    }

    private static int AcceptReadyLoops(
        Snapshot[] snapshots,
        bool[] accepted,
        bool[] reservedTargets)
    {
        int count = snapshots.Length;
        byte[] visitState = new byte[count];
        int[] pathIndex = new int[count];
        Array.Fill(pathIndex, -1);
        List<int> path = new List<int>(count);
        int loopCount = 0;

        for (int start = 0; start < count; start++)
        {
            if (visitState[start] != 0)
            {
                continue;
            }

            path.Clear();
            int current = start;
            while (current >= 0 && visitState[current] == 0)
            {
                visitState[current] = 1;
                pathIndex[current] = path.Count;
                path.Add(current);
                current = snapshots[current].TargetIndex;
            }

            if (current >= 0 &&
                visitState[current] == 1 &&
                pathIndex[current] >= 0)
            {
                int loopStart = pathIndex[current];
                loopCount++;
                bool everyItemReady = true;
                for (int i = loopStart; i < path.Count; i++)
                {
                    int member = path[i];
                    Snapshot loopMember = snapshots[member];
                    loopMember.Belt.IsLoop = true;
                    snapshots[member] = loopMember;
                    everyItemReady &= loopMember.IsReady;
                }

                if (everyItemReady)
                {
                    for (int i = loopStart; i < path.Count; i++)
                    {
                        int source = path[i];
                        accepted[source] = true;
                        reservedTargets[
                            snapshots[source].TargetIndex] = true;
                    }
                }
            }

            for (int i = 0; i < path.Count; i++)
            {
                visitState[path[i]] = 2;
                pathIndex[path[i]] = -1;
            }
        }

        return loopCount;
    }

    private static void SelectCandidates(
        Snapshot[] snapshots,
        bool[] reservedTargets,
        Dictionary<int2, int> nextWinnerByTarget,
        int[] candidateForTarget)
    {
        List<int> candidates = new List<int>(3);
        for (int target = 0; target < snapshots.Length; target++)
        {
            if (reservedTargets[target])
            {
                continue;
            }

            candidates.Clear();
            for (int source = 0; source < snapshots.Length; source++)
            {
                if (snapshots[source].IsReady &&
                    snapshots[source].TargetIndex == target)
                {
                    candidates.Add(source);
                }
            }

            if (candidates.Count == 0)
            {
                continue;
            }

            candidates.Sort((left, right) =>
            {
                int x = snapshots[left].Belt.Cell.x.CompareTo(
                    snapshots[right].Belt.Cell.x);
                if (x != 0)
                {
                    return x;
                }

                int y = snapshots[left].Belt.Cell.y.CompareTo(
                    snapshots[right].Belt.Cell.y);
                return y != 0
                    ? y
                    : snapshots[left].Entity.Index.CompareTo(
                        snapshots[right].Entity.Index);
            });

            int2 targetCell = snapshots[target].Belt.Cell;
            int nextWinner = nextWinnerByTarget.TryGetValue(
                targetCell,
                out int storedWinner)
                ? storedWinner
                : 0;
            candidateForTarget[target] =
                candidates[nextWinner % candidates.Count];
        }
    }

    private static bool ResolveCandidate(
        int source,
        Snapshot[] snapshots,
        int[] candidateForTarget,
        ResolutionState[] states)
    {
        switch (states[source])
        {
            case ResolutionState.Accepted:
                return true;
            case ResolutionState.Rejected:
                return false;
            case ResolutionState.Resolving:
                // Re-entering a resolving node means the selected edges form
                // an atomic cycle. The unwind marks every edge accepted.
                return true;
        }

        states[source] = ResolutionState.Resolving;
        int target = snapshots[source].TargetIndex;
        bool canMove;
        if (target < 0)
        {
            canMove = false;
        }
        else if (snapshots[target].Belt.CurrentItem == Entity.Null)
        {
            canMove = true;
        }
        else
        {
            int targetOutgoingTarget = snapshots[target].TargetIndex;
            canMove =
                snapshots[target].IsReady &&
                targetOutgoingTarget >= 0 &&
                candidateForTarget[targetOutgoingTarget] == target &&
                ResolveCandidate(
                    target,
                    snapshots,
                    candidateForTarget,
                    states);
        }

        states[source] = canMove
            ? ResolutionState.Accepted
            : ResolutionState.Rejected;
        return canMove;
    }

    private static int CountCandidatesForTarget(
        Snapshot[] snapshots,
        int target,
        bool[] reservedTargets)
    {
        if (reservedTargets[target])
        {
            return 0;
        }

        int count = 0;
        for (int source = 0; source < snapshots.Length; source++)
        {
            if (snapshots[source].IsReady &&
                snapshots[source].TargetIndex == target)
            {
                count++;
            }
        }

        return count;
    }
}
