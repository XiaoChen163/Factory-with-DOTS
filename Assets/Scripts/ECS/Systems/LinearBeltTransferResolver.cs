using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

public enum FactoryTransportKind : byte
{
    Belt,
    Merger,
    Splitter
}

public readonly struct FactoryTransportIndex
{
    public FactoryTransportIndex(
        FactoryTransportKind kind,
        int index)
    {
        Kind = kind;
        Index = index;
    }

    public FactoryTransportKind Kind { get; }
    public int Index { get; }
}

/// <summary>
/// Phase 2 transfer resolver. Static connections and loop data live in
/// persistent native containers and are rebuilt only when the grid revision
/// changes. Dynamic arbitration remains on the main thread until Phase 3.
/// </summary>
public sealed class FactoryLinearTransferResolver : IDisposable
{
    private const int MaxRoutingPasses = 3;

    private enum ResolutionState : byte
    {
        Unresolved,
        Resolving,
        Accepted,
        Rejected
    }

    private struct TopologyNode
    {
        public Entity Entity;
        public FactoryTransportKind Kind;
        public int SourceIndex;
        public int2 Cell;
        public int2 Direction;
        public int Input0;
        public int Input1;
        public int Input2;
        public int Output0;
        public int Output1;
        public int Output2;
        public byte InputCount;
        public byte IsLoop;
    }

    private struct DynamicNode
    {
        public Entity CurrentItem;
        public float Progress;
        public int Cursor;
        public int TargetIndex;
        public int OutputIndex;
        public byte IsReady;
    }

    private NativeParallelHashMap<int2, int> indexByCell;
    private NativeList<TopologyNode> topology;
    private NativeList<DynamicNode> dynamicNodes;
    private NativeList<int> candidateForTarget;
    private NativeList<byte> resolutionStates;
    private NativeList<byte> accepted;
    private NativeList<Entity> transferredItems;
    private NativeList<int> resolutionStack;
    private NativeList<byte> loopVisitState;
    private NativeList<int> loopPathIndex;
    private NativeList<int> loopPath;
    private uint cachedRevision;
    private int cachedBeltCount;
    private int cachedMergerCount;
    private int cachedSplitterCount;
    private bool hasCachedRevision;
    private bool disposed;

    public FactoryLinearTransferResolver()
    {
        indexByCell = new NativeParallelHashMap<int2, int>(
            16,
            Allocator.Persistent);
        topology = new NativeList<TopologyNode>(
            16,
            Allocator.Persistent);
        dynamicNodes = new NativeList<DynamicNode>(
            16,
            Allocator.Persistent);
        candidateForTarget = new NativeList<int>(
            16,
            Allocator.Persistent);
        resolutionStates = new NativeList<byte>(
            16,
            Allocator.Persistent);
        accepted = new NativeList<byte>(
            16,
            Allocator.Persistent);
        transferredItems = new NativeList<Entity>(
            16,
            Allocator.Persistent);
        resolutionStack = new NativeList<int>(
            16,
            Allocator.Persistent);
        loopVisitState = new NativeList<byte>(
            16,
            Allocator.Persistent);
        loopPathIndex = new NativeList<int>(
            16,
            Allocator.Persistent);
        loopPath = new NativeList<int>(
            16,
            Allocator.Persistent);
    }

    public int TopologyRebuildCount { get; private set; }
    public int NodeCount => topology.IsCreated ? topology.Length : 0;
    public int LoopCount { get; private set; }
    public int LastCandidateInspectionCount { get; private set; }
    public int LastRoutingPassCount { get; private set; }

    public void EnsureTopology(
        uint revision,
        Entity[] beltEntities,
        Belt[] belts,
        Entity[] mergerEntities,
        Merger[] mergers,
        Entity[] splitterEntities,
        Splitter[] splitters,
        bool forceRebuild = false)
    {
        ThrowIfDisposed();
        ValidateSnapshots(
            beltEntities,
            belts,
            mergerEntities,
            mergers,
            splitterEntities,
            splitters);

        if (!forceRebuild &&
            hasCachedRevision &&
            revision == cachedRevision &&
            belts.Length == cachedBeltCount &&
            mergers.Length == cachedMergerCount &&
            splitters.Length == cachedSplitterCount)
        {
            return;
        }

        RebuildTopology(
            revision,
            beltEntities,
            belts,
            mergerEntities,
            mergers,
            splitterEntities,
            splitters);
    }

    public bool TryGetTransportIndex(
        int2 cell,
        out FactoryTransportIndex result)
    {
        ThrowIfDisposed();
        if (indexByCell.TryGetValue(cell, out int nodeIndex))
        {
            TopologyNode node = topology[nodeIndex];
            result = new FactoryTransportIndex(
                node.Kind,
                node.SourceIndex);
            return true;
        }

        result = default;
        return false;
    }

    public void Resolve(
        Entity[] beltEntities,
        Belt[] belts,
        Entity[] mergerEntities,
        Merger[] mergers,
        Entity[] splitterEntities,
        Splitter[] splitters,
        HashSet<Entity> processedJunctions,
        out int readyRequestCount,
        out int acceptedTransferCount)
    {
        ThrowIfDisposed();
        ValidateSnapshots(
            beltEntities,
            belts,
            mergerEntities,
            mergers,
            splitterEntities,
            splitters);
        if (processedJunctions == null)
        {
            throw new ArgumentNullException(nameof(processedJunctions));
        }

        int expectedNodeCount = belts.Length + mergers.Length +
            splitters.Length;
        if (topology.Length != expectedNodeCount)
        {
            throw new InvalidOperationException(
                "Transport topology must be built before resolving a tick.");
        }

        LoadDynamicState(belts, mergers, splitters);
        readyRequestCount = 0;
        acceptedTransferCount = 0;
        LastCandidateInspectionCount = 0;
        LastRoutingPassCount = 0;

        // A splitter has at most three outputs. Retrying at most three
        // arbitration waves therefore bounds the work by a constant instead
        // of the old JunctionCount + 1 full-graph loop.
        for (int pass = 0; pass < MaxRoutingPasses; pass++)
        {
            LastRoutingPassCount++;
            int passReadyCount = BuildOutgoingRequests(
                processedJunctions);
            readyRequestCount += passReadyCount;
            if (passReadyCount == 0)
            {
                break;
            }

            SelectIncomingCandidates();
            ResolveCandidates();
            int passAcceptedCount = Commit(processedJunctions);
            acceptedTransferCount += passAcceptedCount;
            if (passAcceptedCount == 0)
            {
                break;
            }
        }

        StoreDynamicState(belts, mergers, splitters);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        DisposeIfCreated(ref indexByCell);
        DisposeIfCreated(ref topology);
        DisposeIfCreated(ref dynamicNodes);
        DisposeIfCreated(ref candidateForTarget);
        DisposeIfCreated(ref resolutionStates);
        DisposeIfCreated(ref accepted);
        DisposeIfCreated(ref transferredItems);
        DisposeIfCreated(ref resolutionStack);
        DisposeIfCreated(ref loopVisitState);
        DisposeIfCreated(ref loopPathIndex);
        DisposeIfCreated(ref loopPath);
    }

    private void RebuildTopology(
        uint revision,
        Entity[] beltEntities,
        Belt[] belts,
        Entity[] mergerEntities,
        Merger[] mergers,
        Entity[] splitterEntities,
        Splitter[] splitters)
    {
        int nodeCount = belts.Length + mergers.Length + splitters.Length;
        EnsureCapacity(nodeCount);
        topology.ResizeUninitialized(nodeCount);
        indexByCell.Clear();
        if (indexByCell.Capacity < math.max(16, nodeCount))
        {
            indexByCell.Capacity = math.max(16, nodeCount);
        }

        int nodeIndex = 0;
        for (int i = 0; i < belts.Length; i++, nodeIndex++)
        {
            Belt belt = belts[i];
            belt.IsLoop = false;
            belt.HasOutput = false;
            belts[i] = belt;
            SetTopologyNode(
                nodeIndex,
                beltEntities[i],
                FactoryTransportKind.Belt,
                i,
                belt.Cell,
                belt.Direction);
        }

        for (int i = 0; i < mergers.Length; i++, nodeIndex++)
        {
            Merger merger = mergers[i];
            SetTopologyNode(
                nodeIndex,
                mergerEntities[i],
                FactoryTransportKind.Merger,
                i,
                merger.Cell,
                merger.Direction);
        }

        for (int i = 0; i < splitters.Length; i++, nodeIndex++)
        {
            Splitter splitter = splitters[i];
            SetTopologyNode(
                nodeIndex,
                splitterEntities[i],
                FactoryTransportKind.Splitter,
                i,
                splitter.Cell,
                splitter.Direction);
        }

        BuildConnections();
        LoopCount = MarkBeltLoops(belts);
        MarkBeltOutputs(belts);

        cachedRevision = revision;
        cachedBeltCount = belts.Length;
        cachedMergerCount = mergers.Length;
        cachedSplitterCount = splitters.Length;
        hasCachedRevision = true;
        TopologyRebuildCount++;
    }

    private void SetTopologyNode(
        int nodeIndex,
        Entity entity,
        FactoryTransportKind kind,
        int sourceIndex,
        int2 cell,
        int2 direction)
    {
        topology[nodeIndex] = new TopologyNode
        {
            Entity = entity,
            Kind = kind,
            SourceIndex = sourceIndex,
            Cell = cell,
            Direction = direction,
            Input0 = -1,
            Input1 = -1,
            Input2 = -1,
            Output0 = -1,
            Output1 = -1,
            Output2 = -1
        };
        indexByCell[cell] = nodeIndex;
    }

    private void BuildConnections()
    {
        for (int targetIndex = 0;
             targetIndex < topology.Length;
             targetIndex++)
        {
            TopologyNode target = topology[targetIndex];
            switch (target.Kind)
            {
                case FactoryTransportKind.Belt:
                    ConnectPreferredBeltInput(targetIndex, target);
                    break;
                case FactoryTransportKind.Merger:
                    TryConnectInput(
                        targetIndex,
                        0,
                        target.Direction);
                    TryConnectInput(
                        targetIndex,
                        1,
                        RotateClockwise(target.Direction));
                    TryConnectInput(
                        targetIndex,
                        2,
                        RotateCounterClockwise(target.Direction));
                    break;
                case FactoryTransportKind.Splitter:
                    TryConnectInput(
                        targetIndex,
                        0,
                        target.Direction);
                    break;
            }
        }
    }

    private void ConnectPreferredBeltInput(
        int targetIndex,
        TopologyNode target)
    {
        int2 straight = target.Direction;
        if (TryConnectInput(targetIndex, 0, straight))
        {
            return;
        }

        int2 right = RotateClockwise(straight);
        if (TryConnectInput(targetIndex, 0, right))
        {
            return;
        }

        TryConnectInput(
            targetIndex,
            0,
            RotateCounterClockwise(straight));
    }

    private bool TryConnectInput(
        int targetIndex,
        int inputIndex,
        int2 travelDirection)
    {
        TopologyNode target = topology[targetIndex];
        int2 sourceCell = target.Cell - travelDirection;
        if (!indexByCell.TryGetValue(
                sourceCell,
                out int sourceIndex))
        {
            return false;
        }

        TopologyNode source = topology[sourceIndex];
        if (!TryGetOutputIndex(
                source,
                travelDirection,
                out int outputIndex))
        {
            return false;
        }

        SetInput(ref target, inputIndex, sourceIndex);
        SetOutput(ref source, outputIndex, targetIndex);
        target.InputCount = (byte)math.max(
            target.InputCount,
            inputIndex + 1);
        topology[targetIndex] = target;
        topology[sourceIndex] = source;
        return true;
    }

    private int MarkBeltLoops(Belt[] belts)
    {
        ResizeScratch(loopVisitState, topology.Length, (byte)0);
        ResizeScratch(loopPathIndex, topology.Length, -1);
        int loopCount = 0;

        for (int start = 0; start < topology.Length; start++)
        {
            if (topology[start].Kind != FactoryTransportKind.Belt ||
                loopVisitState[start] != 0)
            {
                continue;
            }

            loopPath.Clear();
            int current = start;
            while (current >= 0 &&
                   topology[current].Kind == FactoryTransportKind.Belt &&
                   loopVisitState[current] == 0)
            {
                loopVisitState[current] = 1;
                loopPathIndex[current] = loopPath.Length;
                loopPath.Add(current);
                current = topology[current].Output0;
            }

            if (current >= 0 &&
                topology[current].Kind == FactoryTransportKind.Belt &&
                loopVisitState[current] == 1 &&
                loopPathIndex[current] >= 0)
            {
                loopCount++;
                for (int i = loopPathIndex[current];
                     i < loopPath.Length;
                     i++)
                {
                    int memberIndex = loopPath[i];
                    TopologyNode member = topology[memberIndex];
                    member.IsLoop = 1;
                    topology[memberIndex] = member;

                    Belt belt = belts[member.SourceIndex];
                    belt.IsLoop = true;
                    belts[member.SourceIndex] = belt;
                }
            }

            for (int i = 0; i < loopPath.Length; i++)
            {
                int memberIndex = loopPath[i];
                loopVisitState[memberIndex] = 2;
                loopPathIndex[memberIndex] = -1;
            }
        }

        return loopCount;
    }

    private void MarkBeltOutputs(Belt[] belts)
    {
        for (int i = 0; i < topology.Length; i++)
        {
            TopologyNode node = topology[i];
            if (node.Kind != FactoryTransportKind.Belt)
            {
                continue;
            }

            Belt belt = belts[node.SourceIndex];
            belt.HasOutput = node.Output0 >= 0;
            belts[node.SourceIndex] = belt;
        }
    }

    private void LoadDynamicState(
        Belt[] belts,
        Merger[] mergers,
        Splitter[] splitters)
    {
        dynamicNodes.ResizeUninitialized(topology.Length);
        for (int i = 0; i < topology.Length; i++)
        {
            TopologyNode node = topology[i];
            DynamicNode state = new DynamicNode
            {
                TargetIndex = -1,
                OutputIndex = -1
            };
            switch (node.Kind)
            {
                case FactoryTransportKind.Belt:
                    Belt belt = belts[node.SourceIndex];
                    state.CurrentItem = belt.CurrentItem;
                    state.Progress = belt.Progress;
                    break;
                case FactoryTransportKind.Merger:
                    Merger merger = mergers[node.SourceIndex];
                    state.CurrentItem = merger.CurrentItem;
                    state.Progress = merger.CurrentItem == Entity.Null
                        ? 0f
                        : 1f;
                    state.Cursor = WrapThree(merger.NextInputIndex);
                    break;
                case FactoryTransportKind.Splitter:
                    Splitter splitter = splitters[node.SourceIndex];
                    state.CurrentItem = splitter.CurrentItem;
                    state.Progress = splitter.CurrentItem == Entity.Null
                        ? 0f
                        : 1f;
                    state.Cursor = WrapThree(splitter.NextOutputIndex);
                    break;
            }

            dynamicNodes[i] = state;
        }
    }

    private int BuildOutgoingRequests(
        HashSet<Entity> processedJunctions)
    {
        int readyCount = 0;
        for (int sourceIndex = 0;
             sourceIndex < topology.Length;
             sourceIndex++)
        {
            TopologyNode source = topology[sourceIndex];
            DynamicNode state = dynamicNodes[sourceIndex];
            state.IsReady = 0;
            state.TargetIndex = -1;
            state.OutputIndex = -1;

            if (state.CurrentItem == Entity.Null ||
                (source.Kind == FactoryTransportKind.Belt &&
                 state.Progress < 1f) ||
                (source.Kind != FactoryTransportKind.Belt &&
                 processedJunctions.Contains(source.Entity)))
            {
                dynamicNodes[sourceIndex] = state;
                continue;
            }

            if (source.Kind == FactoryTransportKind.Splitter)
            {
                SelectSplitterOutput(source, ref state);
            }
            else
            {
                state.TargetIndex = source.Output0;
                state.OutputIndex = source.Output0 >= 0 ? 0 : -1;
            }

            if (state.TargetIndex >= 0)
            {
                state.IsReady = 1;
                readyCount++;
            }

            dynamicNodes[sourceIndex] = state;
        }

        return readyCount;
    }

    private void SelectSplitterOutput(
        TopologyNode source,
        ref DynamicNode state)
    {
        int fallbackOutput = -1;
        int fallbackTarget = -1;

        for (int offset = 0; offset < 3; offset++)
        {
            int outputIndex = WrapThree(state.Cursor + offset);
            int targetIndex = GetOutput(source, outputIndex);
            if (targetIndex < 0)
            {
                continue;
            }

            if (fallbackOutput < 0)
            {
                fallbackOutput = outputIndex;
                fallbackTarget = targetIndex;
            }

            if (dynamicNodes[targetIndex].CurrentItem == Entity.Null)
            {
                state.OutputIndex = outputIndex;
                state.TargetIndex = targetIndex;
                return;
            }
        }

        state.OutputIndex = fallbackOutput;
        state.TargetIndex = fallbackTarget;
    }

    private void SelectIncomingCandidates()
    {
        ResizeScratch(candidateForTarget, topology.Length, -1);

        for (int targetIndex = 0;
             targetIndex < topology.Length;
             targetIndex++)
        {
            TopologyNode target = topology[targetIndex];
            int winner = -1;
            int winnerDistance = int.MaxValue;

            for (int inputIndex = 0;
                 inputIndex < target.InputCount;
                 inputIndex++)
            {
                LastCandidateInspectionCount++;
                int candidate = GetInput(target, inputIndex);
                if (candidate < 0)
                {
                    continue;
                }

                DynamicNode candidateState = dynamicNodes[candidate];
                if (candidateState.IsReady == 0 ||
                    candidateState.TargetIndex != targetIndex)
                {
                    continue;
                }

                int distance = target.Kind == FactoryTransportKind.Merger
                    ? WrapThree(inputIndex -
                        dynamicNodes[targetIndex].Cursor)
                    : 0;
                if (distance < winnerDistance ||
                    (distance == winnerDistance &&
                     (winner < 0 ||
                      CompareNodes(candidate, winner) < 0)))
                {
                    winner = candidate;
                    winnerDistance = distance;
                }
            }

            candidateForTarget[targetIndex] = winner;
        }
    }

    private void ResolveCandidates()
    {
        ResizeScratch(
            resolutionStates,
            topology.Length,
            (byte)ResolutionState.Unresolved);
        ResizeScratch(accepted, topology.Length, (byte)0);

        for (int targetIndex = 0;
             targetIndex < topology.Length;
             targetIndex++)
        {
            int candidate = candidateForTarget[targetIndex];
            if (candidate >= 0 && ResolveCandidate(candidate))
            {
                accepted[candidate] = 1;
            }
        }
    }

    private bool ResolveCandidate(int sourceIndex)
    {
        ResolutionState initialState =
            (ResolutionState)resolutionStates[sourceIndex];
        if (initialState == ResolutionState.Accepted)
        {
            return true;
        }
        if (initialState == ResolutionState.Rejected)
        {
            return false;
        }

        resolutionStack.Clear();
        int current = sourceIndex;
        bool canMove;

        while (true)
        {
            ResolutionState currentState =
                (ResolutionState)resolutionStates[current];
            if (currentState == ResolutionState.Accepted)
            {
                canMove = true;
                break;
            }
            if (currentState == ResolutionState.Rejected)
            {
                canMove = false;
                break;
            }
            if (currentState == ResolutionState.Resolving)
            {
                // Reaching the active path again means a full ring can move
                // atomically from the same snapshot.
                canMove = true;
                break;
            }

            resolutionStates[current] =
                (byte)ResolutionState.Resolving;
            resolutionStack.Add(current);

            DynamicNode source = dynamicNodes[current];
            int targetIndex = source.TargetIndex;
            if (targetIndex < 0)
            {
                canMove = false;
                break;
            }
            if (dynamicNodes[targetIndex].CurrentItem == Entity.Null)
            {
                canMove = true;
                break;
            }

            DynamicNode target = dynamicNodes[targetIndex];
            if (target.IsReady == 0 ||
                target.TargetIndex < 0 ||
                candidateForTarget[target.TargetIndex] != targetIndex)
            {
                canMove = false;
                break;
            }

            current = targetIndex;
        }

        byte finalState = canMove
            ? (byte)ResolutionState.Accepted
            : (byte)ResolutionState.Rejected;
        for (int i = resolutionStack.Length - 1; i >= 0; i--)
        {
            resolutionStates[resolutionStack[i]] = finalState;
        }

        return canMove;
    }

    private int Commit(HashSet<Entity> processedJunctions)
    {
        ResizeScratch(transferredItems, topology.Length, Entity.Null);
        int acceptedCount = 0;

        for (int sourceIndex = 0;
             sourceIndex < topology.Length;
             sourceIndex++)
        {
            if (accepted[sourceIndex] == 0)
            {
                continue;
            }

            TopologyNode source = topology[sourceIndex];
            DynamicNode state = dynamicNodes[sourceIndex];
            transferredItems[sourceIndex] = state.CurrentItem;
            state.CurrentItem = Entity.Null;
            state.Progress = 0f;
            if (source.Kind == FactoryTransportKind.Splitter)
            {
                state.Cursor = WrapThree(state.OutputIndex + 1);
            }
            if (source.Kind != FactoryTransportKind.Belt)
            {
                processedJunctions.Add(source.Entity);
            }

            dynamicNodes[sourceIndex] = state;
            acceptedCount++;
        }

        for (int sourceIndex = 0;
             sourceIndex < topology.Length;
             sourceIndex++)
        {
            if (accepted[sourceIndex] == 0)
            {
                continue;
            }

            DynamicNode source = dynamicNodes[sourceIndex];
            int targetIndex = source.TargetIndex;
            TopologyNode targetTopology = topology[targetIndex];
            DynamicNode target = dynamicNodes[targetIndex];
            target.CurrentItem = transferredItems[sourceIndex];
            target.Progress = 0f;
            if (targetTopology.Kind != FactoryTransportKind.Belt)
            {
                processedJunctions.Add(targetTopology.Entity);
            }
            if (targetTopology.Kind == FactoryTransportKind.Merger)
            {
                int inputIndex = FindInputIndex(
                    targetTopology,
                    sourceIndex);
                target.Cursor = WrapThree(inputIndex + 1);
            }

            dynamicNodes[targetIndex] = target;
        }

        return acceptedCount;
    }

    private void StoreDynamicState(
        Belt[] belts,
        Merger[] mergers,
        Splitter[] splitters)
    {
        for (int i = 0; i < topology.Length; i++)
        {
            TopologyNode node = topology[i];
            DynamicNode state = dynamicNodes[i];
            switch (node.Kind)
            {
                case FactoryTransportKind.Belt:
                    Belt belt = belts[node.SourceIndex];
                    belt.CurrentItem = state.CurrentItem;
                    belt.Progress = state.Progress;
                    belts[node.SourceIndex] = belt;
                    break;
                case FactoryTransportKind.Merger:
                    Merger merger = mergers[node.SourceIndex];
                    merger.CurrentItem = state.CurrentItem;
                    merger.NextInputIndex = state.Cursor;
                    mergers[node.SourceIndex] = merger;
                    break;
                case FactoryTransportKind.Splitter:
                    Splitter splitter = splitters[node.SourceIndex];
                    splitter.CurrentItem = state.CurrentItem;
                    splitter.NextOutputIndex = state.Cursor;
                    splitters[node.SourceIndex] = splitter;
                    break;
            }
        }
    }

    private static bool TryGetOutputIndex(
        TopologyNode source,
        int2 travelDirection,
        out int outputIndex)
    {
        if (source.Kind != FactoryTransportKind.Splitter)
        {
            outputIndex = 0;
            return math.all(source.Direction == travelDirection);
        }

        for (int i = 0; i < 3; i++)
        {
            if (math.all(
                    GetSplitterOutputDirection(source.Direction, i) ==
                    travelDirection))
            {
                outputIndex = i;
                return true;
            }
        }

        outputIndex = -1;
        return false;
    }

    private int CompareNodes(int leftIndex, int rightIndex)
    {
        TopologyNode left = topology[leftIndex];
        TopologyNode right = topology[rightIndex];
        int x = left.Cell.x.CompareTo(right.Cell.x);
        if (x != 0)
        {
            return x;
        }

        int y = left.Cell.y.CompareTo(right.Cell.y);
        return y != 0
            ? y
            : left.Entity.Index.CompareTo(right.Entity.Index);
    }

    private static int FindInputIndex(
        TopologyNode target,
        int sourceIndex)
    {
        for (int i = 0; i < target.InputCount; i++)
        {
            if (GetInput(target, i) == sourceIndex)
            {
                return i;
            }
        }

        return 0;
    }

    private static int GetInput(TopologyNode node, int index)
    {
        switch (index)
        {
            case 0:
                return node.Input0;
            case 1:
                return node.Input1;
            case 2:
                return node.Input2;
            default:
                return -1;
        }
    }

    private static void SetInput(
        ref TopologyNode node,
        int index,
        int value)
    {
        switch (index)
        {
            case 0:
                node.Input0 = value;
                break;
            case 1:
                node.Input1 = value;
                break;
            case 2:
                node.Input2 = value;
                break;
        }
    }

    private static int GetOutput(TopologyNode node, int index)
    {
        switch (index)
        {
            case 0:
                return node.Output0;
            case 1:
                return node.Output1;
            case 2:
                return node.Output2;
            default:
                return -1;
        }
    }

    private static void SetOutput(
        ref TopologyNode node,
        int index,
        int value)
    {
        switch (index)
        {
            case 0:
                node.Output0 = value;
                break;
            case 1:
                node.Output1 = value;
                break;
            case 2:
                node.Output2 = value;
                break;
        }
    }

    private void EnsureCapacity(int nodeCount)
    {
        int capacity = math.max(16, nodeCount);
        EnsureCapacity(topology, capacity);
        EnsureCapacity(dynamicNodes, capacity);
        EnsureCapacity(candidateForTarget, capacity);
        EnsureCapacity(resolutionStates, capacity);
        EnsureCapacity(accepted, capacity);
        EnsureCapacity(transferredItems, capacity);
        EnsureCapacity(resolutionStack, capacity);
        EnsureCapacity(loopVisitState, capacity);
        EnsureCapacity(loopPathIndex, capacity);
        EnsureCapacity(loopPath, capacity);
    }

    private static void EnsureCapacity<T>(
        NativeList<T> list,
        int capacity)
        where T : unmanaged
    {
        if (list.Capacity < capacity)
        {
            list.Capacity = capacity;
        }
    }

    private static void ResizeScratch<T>(
        NativeList<T> list,
        int length,
        T value)
        where T : unmanaged
    {
        list.ResizeUninitialized(length);
        for (int i = 0; i < length; i++)
        {
            list[i] = value;
        }
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

    private static void ValidateSnapshots(
        Entity[] beltEntities,
        Belt[] belts,
        Entity[] mergerEntities,
        Merger[] mergers,
        Entity[] splitterEntities,
        Splitter[] splitters)
    {
        if (beltEntities == null ||
            belts == null ||
            mergerEntities == null ||
            mergers == null ||
            splitterEntities == null ||
            splitters == null)
        {
            throw new ArgumentNullException(
                "Transport snapshots cannot be null.");
        }

        if (beltEntities.Length != belts.Length ||
            mergerEntities.Length != mergers.Length ||
            splitterEntities.Length != splitters.Length)
        {
            throw new ArgumentException(
                "Entity and component snapshots must have matching lengths.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(
                nameof(FactoryLinearTransferResolver));
        }
    }

    private static void DisposeIfCreated<TKey, TValue>(
        ref NativeParallelHashMap<TKey, TValue> container)
        where TKey : unmanaged, IEquatable<TKey>
        where TValue : unmanaged
    {
        if (container.IsCreated)
        {
            container.Dispose();
        }
    }

    private static void DisposeIfCreated<T>(
        ref NativeList<T> container)
        where T : unmanaged
    {
        if (container.IsCreated)
        {
            container.Dispose();
        }
    }
}
