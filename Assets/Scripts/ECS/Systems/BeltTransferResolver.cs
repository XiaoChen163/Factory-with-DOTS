using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

public static class FactoryTransferResolver
{
    private enum NodeKind : byte
    {
        Belt,
        Merger,
        Splitter
    }

    private enum ResolutionState : byte
    {
        Unresolved,
        Resolving,
        Accepted,
        Rejected
    }

    private struct Node
    {
        public Entity Entity;
        public NodeKind Kind;
        public int SourceIndex;
        public GridCell Cell;
        public int2 Direction;
        public Entity CurrentItem;
        public float Progress;
        public int Cursor;
        public int TargetIndex;
        public int OutputIndex;
        public int2 OutputDirection;
        public bool IsReady;
        public TransportConnectionMode ConnectionMode;
    }

    public static void Resolve(
        Entity[] beltEntities,
        BeltTopology[] beltTopologies,
        BeltState[] beltStates,
        Entity[] mergerEntities,
        Merger[] mergers,
        Entity[] splitterEntities,
        Splitter[] splitters,
        HashSet<Entity> processedJunctions,
        out int loopCount,
        out int readyRequestCount,
        out int acceptedTransferCount)
    {
        ValidateInputs(
            beltEntities,
            beltTopologies,
            beltStates,
            mergerEntities,
            mergers,
            splitterEntities,
            splitters,
            processedJunctions);

        int beltCount = beltTopologies.Length;
        int mergerCount = mergers.Length;
        int splitterCount = splitters.Length;
        Node[] nodes = new Node[
            beltCount + mergerCount + splitterCount];
        Dictionary<GridCell, int> indexByCell =
            new Dictionary<GridCell, int>(nodes.Length);

        int nodeIndex = 0;
        for (int i = 0; i < beltCount; i++, nodeIndex++)
        {
            BeltTopology belt = beltTopologies[i];
            nodes[nodeIndex] = new Node
            {
                Entity = beltEntities[i],
                Kind = NodeKind.Belt,
                SourceIndex = i,
                Cell = belt.Cell,
                Direction = belt.Direction,
                CurrentItem = beltStates[i].CurrentItem,
                Progress = beltStates[i].Progress,
                TargetIndex = -1,
                OutputIndex = -1,
                ConnectionMode = belt.ConnectionMode
            };
            if (belt.ConnectionMode != TransportConnectionMode.ExplicitOnly)
                indexByCell[belt.Cell] = nodeIndex;
        }

        for (int i = 0; i < mergerCount; i++, nodeIndex++)
        {
            Merger merger = mergers[i];
            nodes[nodeIndex] = new Node
            {
                Entity = mergerEntities[i],
                Kind = NodeKind.Merger,
                SourceIndex = i,
                Cell = merger.Cell,
                Direction = merger.Direction,
                CurrentItem = merger.CurrentItem,
                Progress = merger.CurrentItem == Entity.Null ? 0f : 1f,
                Cursor = WrapThree(merger.NextInputIndex),
                TargetIndex = -1,
                OutputIndex = -1
            };
            indexByCell[merger.Cell] = nodeIndex;
        }

        for (int i = 0; i < splitterCount; i++, nodeIndex++)
        {
            Splitter splitter = splitters[i];
            nodes[nodeIndex] = new Node
            {
                Entity = splitterEntities[i],
                Kind = NodeKind.Splitter,
                SourceIndex = i,
                Cell = splitter.Cell,
                Direction = splitter.Direction,
                CurrentItem = splitter.CurrentItem,
                Progress = splitter.CurrentItem == Entity.Null ? 0f : 1f,
                Cursor = WrapThree(splitter.NextOutputIndex),
                TargetIndex = -1,
                OutputIndex = -1
            };
            indexByCell[splitter.Cell] = nodeIndex;
        }

        loopCount = MarkBeltLoops(nodes, indexByCell);
        readyRequestCount = BuildOutgoingRequests(
            nodes,
            indexByCell,
            processedJunctions);

        int[] candidateForTarget = new int[nodes.Length];
        Array.Fill(candidateForTarget, -1);
        SelectIncomingCandidates(nodes, candidateForTarget);

        ResolutionState[] states =
            new ResolutionState[nodes.Length];
        bool[] accepted = new bool[nodes.Length];
        for (int target = 0; target < nodes.Length; target++)
        {
            int candidate = candidateForTarget[target];
            if (candidate >= 0 &&
                ResolveCandidate(
                    candidate,
                    nodes,
                    candidateForTarget,
                    states))
            {
                accepted[candidate] = true;
            }
        }

        for (int i = 0; i < states.Length; i++)
        {
            if (states[i] == ResolutionState.Accepted)
            {
                accepted[i] = true;
            }
        }

        Commit(
            nodes,
            accepted,
            beltStates,
            mergers,
            splitters,
            processedJunctions,
            out acceptedTransferCount);
    }

    private static int BuildOutgoingRequests(
        Node[] nodes,
        Dictionary<GridCell, int> indexByCell,
        HashSet<Entity> processedJunctions)
    {
        int readyCount = 0;
        for (int sourceIndex = 0;
             sourceIndex < nodes.Length;
             sourceIndex++)
        {
            Node source = nodes[sourceIndex];
            if (source.CurrentItem == Entity.Null ||
                (source.Kind == NodeKind.Belt &&
                 source.ConnectionMode == TransportConnectionMode.ExplicitOnly) ||
                (source.Kind == NodeKind.Belt &&
                 source.Progress < 1f) ||
                (source.Kind != NodeKind.Belt &&
                 processedJunctions.Contains(source.Entity)))
            {
                continue;
            }

            if (source.Kind == NodeKind.Splitter)
            {
                SelectSplitterOutput(
                    sourceIndex,
                    nodes,
                    indexByCell);
            }
            else
            {
                TrySetOutput(
                    sourceIndex,
                    0,
                    source.Direction,
                    nodes,
                    indexByCell);
            }

            source = nodes[sourceIndex];
            if (source.TargetIndex < 0)
            {
                continue;
            }

            source.IsReady = true;
            nodes[sourceIndex] = source;
            readyCount++;
        }

        return readyCount;
    }

    private static void SelectSplitterOutput(
        int sourceIndex,
        Node[] nodes,
        Dictionary<GridCell, int> indexByCell)
    {
        Node source = nodes[sourceIndex];
        int fallbackOutput = -1;
        int2 fallbackDirection = default;

        for (int offset = 0; offset < 3; offset++)
        {
            int outputIndex = WrapThree(source.Cursor + offset);
            int2 outputDirection = GetSplitterOutputDirection(
                source.Direction,
                outputIndex);
            GridCell targetCell = source.Cell + outputDirection;
            if (!indexByCell.TryGetValue(
                    targetCell,
                    out int targetIndex) ||
                !CanAcceptInput(
                    nodes[targetIndex],
                    source.Cell,
                    outputDirection,
                    nodes,
                    indexByCell))
            {
                continue;
            }

            if (fallbackOutput < 0)
            {
                fallbackOutput = outputIndex;
                fallbackDirection = outputDirection;
            }

            if (nodes[targetIndex].CurrentItem == Entity.Null)
            {
                TrySetOutput(
                    sourceIndex,
                    outputIndex,
                    outputDirection,
                    nodes,
                    indexByCell);
                return;
            }
        }

        if (fallbackOutput >= 0)
        {
            TrySetOutput(
                sourceIndex,
                fallbackOutput,
                fallbackDirection,
                nodes,
                indexByCell);
        }
    }

    private static bool TrySetOutput(
        int sourceIndex,
        int outputIndex,
        int2 outputDirection,
        Node[] nodes,
        Dictionary<GridCell, int> indexByCell)
    {
        Node source = nodes[sourceIndex];
        GridCell targetCell = source.Cell + outputDirection;
        if (!indexByCell.TryGetValue(
                targetCell,
                out int targetIndex) ||
            !CanAcceptInput(
                nodes[targetIndex],
                source.Cell,
                outputDirection,
                nodes,
                indexByCell))
        {
            return false;
        }

        source.TargetIndex = targetIndex;
        source.OutputIndex = outputIndex;
        source.OutputDirection = outputDirection;
        nodes[sourceIndex] = source;
        return true;
    }

    private static bool CanAcceptInput(
        Node target,
        GridCell sourceCell,
        int2 travelDirection,
        Node[] nodes,
        Dictionary<GridCell, int> indexByCell)
    {
        if (target.Kind == NodeKind.Belt &&
            !RampUtility.AllowsPlanarInput(target.ConnectionMode))
        {
            return false;
        }
        if (sourceCell + travelDirection != target.Cell)
        {
            return false;
        }

        switch (target.Kind)
        {
            case NodeKind.Belt:
                return IsSelectedBeltInput(
                    target,
                    travelDirection,
                    nodes,
                    indexByCell);
            case NodeKind.Splitter:
                return math.all(travelDirection == target.Direction);
            case NodeKind.Merger:
                return GetMergerInputIndex(
                    target.Direction,
                    travelDirection) >= 0;
            default:
                return false;
        }
    }

    private static bool IsSelectedBeltInput(
        Node target,
        int2 travelDirection,
        Node[] nodes,
        Dictionary<GridCell, int> indexByCell)
    {
        // The output face is never an input. Of the remaining three faces,
        // keep one stable connection by preferring straight, then right, then
        // left. This prevents a later side belt from replacing an existing
        // straight connection merely because its entity sorts first.
        int2 straight = target.Direction;
        if (HasOutputToward(
                target.Cell,
                straight,
                nodes,
                indexByCell))
        {
            return math.all(travelDirection == straight);
        }

        int2 right = new int2(straight.y, -straight.x);
        if (HasOutputToward(
                target.Cell,
                right,
                nodes,
                indexByCell))
        {
            return math.all(travelDirection == right);
        }

        int2 left = -right;
        return HasOutputToward(
                   target.Cell,
                   left,
                   nodes,
                   indexByCell) &&
               math.all(travelDirection == left);
    }

    private static bool HasOutputToward(
        GridCell targetCell,
        int2 travelDirection,
        Node[] nodes,
        Dictionary<GridCell, int> indexByCell)
    {
        GridCell sourceCell = targetCell - travelDirection;
        if (!indexByCell.TryGetValue(sourceCell, out int sourceIndex))
        {
            return false;
        }

        Node source = nodes[sourceIndex];
        if (source.Kind == NodeKind.Belt &&
            !RampUtility.AllowsPlanarOutput(source.ConnectionMode))
        {
            return false;
        }
        if (source.Kind == NodeKind.Splitter)
        {
            for (int i = 0; i < 3; i++)
            {
                if (math.all(
                        GetSplitterOutputDirection(
                            source.Direction,
                            i) == travelDirection))
                {
                    return true;
                }
            }

            return false;
        }

        return math.all(source.Direction == travelDirection);
    }

    private static void SelectIncomingCandidates(
        Node[] nodes,
        int[] candidateForTarget)
    {
        List<int> candidates = new List<int>(3);
        for (int targetIndex = 0;
             targetIndex < nodes.Length;
             targetIndex++)
        {
            candidates.Clear();
            for (int sourceIndex = 0;
                 sourceIndex < nodes.Length;
                 sourceIndex++)
            {
                if (nodes[sourceIndex].IsReady &&
                    nodes[sourceIndex].TargetIndex == targetIndex)
                {
                    candidates.Add(sourceIndex);
                }
            }

            if (candidates.Count == 0)
            {
                continue;
            }

            if (nodes[targetIndex].Kind == NodeKind.Merger)
            {
                candidateForTarget[targetIndex] =
                    SelectMergerCandidate(
                        nodes[targetIndex],
                        nodes,
                        candidates);
                continue;
            }

            candidates.Sort((left, right) =>
                CompareNodes(nodes[left], nodes[right]));
            candidateForTarget[targetIndex] = candidates[0];
        }
    }

    private static int SelectMergerCandidate(
        Node merger,
        Node[] nodes,
        List<int> candidates)
    {
        int winner = -1;
        int winnerDistance = int.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            int candidate = candidates[i];
            int inputIndex = GetMergerInputIndex(
                merger.Direction,
                nodes[candidate].OutputDirection);
            if (inputIndex < 0)
            {
                continue;
            }

            int distance = WrapThree(inputIndex - merger.Cursor);
            if (distance < winnerDistance ||
                (distance == winnerDistance &&
                 (winner < 0 ||
                  CompareNodes(
                      nodes[candidate],
                      nodes[winner]) < 0)))
            {
                winner = candidate;
                winnerDistance = distance;
            }
        }

        return winner;
    }

    private static bool ResolveCandidate(
        int sourceIndex,
        Node[] nodes,
        int[] candidateForTarget,
        ResolutionState[] states)
    {
        switch (states[sourceIndex])
        {
            case ResolutionState.Accepted:
                return true;
            case ResolutionState.Rejected:
                return false;
            case ResolutionState.Resolving:
                return true;
        }

        states[sourceIndex] = ResolutionState.Resolving;
        int targetIndex = nodes[sourceIndex].TargetIndex;
        bool canMove;
        if (targetIndex < 0)
        {
            canMove = false;
        }
        else if (nodes[targetIndex].CurrentItem == Entity.Null)
        {
            canMove = true;
        }
        else
        {
            int targetOutgoingTarget = nodes[targetIndex].TargetIndex;
            canMove =
                nodes[targetIndex].IsReady &&
                targetOutgoingTarget >= 0 &&
                candidateForTarget[targetOutgoingTarget] ==
                    targetIndex &&
                ResolveCandidate(
                    targetIndex,
                    nodes,
                    candidateForTarget,
                    states);
        }

        states[sourceIndex] = canMove
            ? ResolutionState.Accepted
            : ResolutionState.Rejected;
        return canMove;
    }

    private static void Commit(
        Node[] nodes,
        bool[] accepted,
        BeltState[] beltStates,
        Merger[] mergers,
        Splitter[] splitters,
        HashSet<Entity> processedJunctions,
        out int acceptedTransferCount)
    {
        Entity[] transferredItems = new Entity[nodes.Length];
        acceptedTransferCount = 0;

        for (int sourceIndex = 0;
             sourceIndex < nodes.Length;
             sourceIndex++)
        {
            if (!accepted[sourceIndex])
            {
                continue;
            }

            transferredItems[sourceIndex] =
                nodes[sourceIndex].CurrentItem;
            Node source = nodes[sourceIndex];
            source.CurrentItem = Entity.Null;
            source.Progress = 0f;
            if (source.Kind == NodeKind.Splitter)
            {
                source.Cursor = WrapThree(source.OutputIndex + 1);
            }
            if (source.Kind != NodeKind.Belt)
            {
                processedJunctions.Add(source.Entity);
            }

            nodes[sourceIndex] = source;
            acceptedTransferCount++;
        }

        for (int sourceIndex = 0;
             sourceIndex < nodes.Length;
             sourceIndex++)
        {
            if (!accepted[sourceIndex])
            {
                continue;
            }

            int targetIndex = nodes[sourceIndex].TargetIndex;
            Node target = nodes[targetIndex];
            target.CurrentItem = transferredItems[sourceIndex];
            target.Progress = 0f;
            if (target.Kind != NodeKind.Belt)
            {
                // A junction that receives an item cannot forward it until
                // the next simulation tick. This keeps junction processing
                // to one item per tick while retaining pipelined throughput.
                processedJunctions.Add(target.Entity);
            }
            if (target.Kind == NodeKind.Merger)
            {
                int inputIndex = GetMergerInputIndex(
                    target.Direction,
                    nodes[sourceIndex].OutputDirection);
                target.Cursor = WrapThree(inputIndex + 1);
            }

            nodes[targetIndex] = target;
        }

        for (int i = 0; i < nodes.Length; i++)
        {
            Node node = nodes[i];
            switch (node.Kind)
            {
                case NodeKind.Belt:
                    BeltState belt = beltStates[node.SourceIndex];
                    belt.CurrentItem = node.CurrentItem;
                    belt.Progress = node.Progress;
                    beltStates[node.SourceIndex] = belt;
                    break;
                case NodeKind.Merger:
                    Merger merger = mergers[node.SourceIndex];
                    merger.CurrentItem = node.CurrentItem;
                    merger.NextInputIndex = node.Cursor;
                    mergers[node.SourceIndex] = merger;
                    break;
                case NodeKind.Splitter:
                    Splitter splitter = splitters[node.SourceIndex];
                    splitter.CurrentItem = node.CurrentItem;
                    splitter.NextOutputIndex = node.Cursor;
                    splitters[node.SourceIndex] = splitter;
                    break;
            }
        }
    }

    private static int MarkBeltLoops(
        Node[] nodes,
        Dictionary<GridCell, int> indexByCell)
    {
        byte[] visitState = new byte[nodes.Length];
        int[] pathIndex = new int[nodes.Length];
        Array.Fill(pathIndex, -1);
        List<int> path = new List<int>(nodes.Length);
        int loopCount = 0;

        for (int start = 0; start < nodes.Length; start++)
        {
            if (nodes[start].Kind != NodeKind.Belt ||
                visitState[start] != 0)
            {
                continue;
            }

            path.Clear();
            int current = start;
            while (current >= 0 &&
                   nodes[current].Kind == NodeKind.Belt &&
                   visitState[current] == 0)
            {
                visitState[current] = 1;
                pathIndex[current] = path.Count;
                path.Add(current);

                GridCell nextCell =
                    nodes[current].Cell + nodes[current].Direction;
                current = indexByCell.TryGetValue(
                    nextCell,
                    out int nextIndex)
                    && CanAcceptInput(
                        nodes[nextIndex],
                        nodes[current].Cell,
                        nodes[current].Direction,
                        nodes,
                        indexByCell)
                        ? nextIndex
                        : -1;
            }

            if (current >= 0 &&
                nodes[current].Kind == NodeKind.Belt &&
                visitState[current] == 1 &&
                pathIndex[current] >= 0)
            {
                loopCount++;
            }

            for (int i = 0; i < path.Count; i++)
            {
                visitState[path[i]] = 2;
                pathIndex[path[i]] = -1;
            }
        }

        return loopCount;
    }

    private static int GetMergerInputIndex(
        int2 direction,
        int2 travelDirection)
    {
        if (math.all(travelDirection == direction))
        {
            return 0;
        }

        if (math.all(
                travelDirection ==
                RotateClockwise(direction)))
        {
            return 1;
        }

        if (math.all(
                travelDirection ==
                RotateCounterClockwise(direction)))
        {
            return 2;
        }

        return -1;
    }

    private static int2 GetSplitterOutputDirection(
        int2 direction,
        int outputIndex)
    {
        switch (WrapThree(outputIndex))
        {
            case 0:
                return direction;
            case 1:
                return RotateCounterClockwise(direction);
            default:
                return RotateClockwise(direction);
        }
    }

    private static int2 RotateClockwise(int2 direction)
    {
        return new int2(direction.y, -direction.x);
    }

    private static int2 RotateCounterClockwise(int2 direction)
    {
        return new int2(-direction.y, direction.x);
    }

    private static int WrapThree(int value)
    {
        int wrapped = value % 3;
        return wrapped < 0 ? wrapped + 3 : wrapped;
    }

    private static int CompareNodes(Node left, Node right)
    {
        int level = left.Cell.Level.CompareTo(right.Cell.Level);
        if (level != 0)
        {
            return level;
        }

        int x = left.Cell.X.CompareTo(right.Cell.X);
        if (x != 0)
        {
            return x;
        }

        int y = left.Cell.Z.CompareTo(right.Cell.Z);
        return y != 0
            ? y
            : left.Entity.Index.CompareTo(right.Entity.Index);
    }

    private static void ValidateInputs(
        Entity[] beltEntities,
        BeltTopology[] beltTopologies,
        BeltState[] beltStates,
        Entity[] mergerEntities,
        Merger[] mergers,
        Entity[] splitterEntities,
        Splitter[] splitters,
        HashSet<Entity> processedJunctions)
    {
        if (beltEntities == null ||
            beltTopologies == null ||
            beltStates == null ||
            mergerEntities == null ||
            mergers == null ||
            splitterEntities == null ||
            splitters == null)
        {
            throw new ArgumentNullException(
                "Transport snapshots cannot be null.");
        }

        if (processedJunctions == null)
        {
            throw new ArgumentNullException(
                nameof(processedJunctions));
        }

        if (beltEntities.Length != beltTopologies.Length ||
            beltTopologies.Length != beltStates.Length ||
            mergerEntities.Length != mergers.Length ||
            splitterEntities.Length != splitters.Length)
        {
            throw new ArgumentException(
                "Entity and component snapshots must have matching lengths.");
        }
    }
}
