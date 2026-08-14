using System;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

public enum FactoryTransportKind : byte
{
    Belt,
    Merger,
    Splitter
}

/// <summary>
/// Unmanaged per-port key used by the building-port reservation counters.
/// </summary>
public readonly struct TransportPortKey : IEquatable<TransportPortKey>
{
    public TransportPortKey(Entity owner, byte portIndex)
    {
        Owner = owner;
        PortIndex = portIndex;
    }

    public readonly Entity Owner;
    public readonly byte PortIndex;

    public bool Equals(TransportPortKey other)
    {
        return Owner == other.Owner && PortIndex == other.PortIndex;
    }

    public override bool Equals(object obj)
    {
        return obj is TransportPortKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return (Owner.GetHashCode() * 397) ^ PortIndex;
    }
}

/// <summary>
/// Prefab visual information captured on the main thread when the item
/// catalog changes. Passed into the arbitration job as a Native map so the
/// per-tick job does not create read dependencies on visual components.
/// </summary>
public struct ItemPrefabVisualInfo
{
    public Entity Prefab;
    public LocalTransform Transform;
    public byte HasTransform;
    public byte HasVisualState;
}

internal struct TransportTopologyNode
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
}

internal struct TransportDynamicNode
{
    public Entity CurrentItem;
    public float Progress;
    public int Cursor;
    public int TargetIndex;
    public int OutputIndex;
    public byte IsReady;
}

internal static class TransportTopologyAccess
{
    public static int GetInput(in TransportTopologyNode node, int index)
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

    public static void SetInput(
        ref TransportTopologyNode node,
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

    public static int GetOutput(in TransportTopologyNode node, int index)
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

    public static void SetOutput(
        ref TransportTopologyNode node,
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

    public static int WrapThree(int value)
    {
        int wrapped = value % 3;
        return wrapped < 0 ? wrapped + 3 : wrapped;
    }

    public static int2 RotateClockwise(int2 direction)
    {
        return new int2(direction.y, -direction.x);
    }

    public static int2 RotateCounterClockwise(int2 direction)
    {
        return new int2(-direction.y, direction.x);
    }

    public static int2 GetSplitterOutputDirection(
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

    public static int GetSplitterOutputIndex(
        int2 direction,
        int2 outputDirection)
    {
        for (int i = 0; i < 3; i++)
        {
            if (math.all(
                    GetSplitterOutputDirection(direction, i) ==
                    outputDirection))
            {
                return i;
            }
        }

        return -1;
    }

    public static int GetMergerInputIndex(
        int2 direction,
        int2 travelDirection)
    {
        if (math.all(travelDirection == direction))
        {
            return 0;
        }

        if (math.all(travelDirection == RotateClockwise(direction)))
        {
            return 1;
        }

        if (math.all(travelDirection == RotateCounterClockwise(direction)))
        {
            return 2;
        }

        return -1;
    }
}
/// <summary>
/// Phase 3 transfer resolver. Static connections and loop data live in
/// persistent native containers and are rebuilt only when the grid revision
/// changes. The per-tick arbitration runs inside
/// <see cref="FactoryTransferArbitrationJob"/>, a Burst-compilable single job
/// that also commits building-port transfers. The steady-state tick path no
/// longer converts native snapshots to managed arrays and never writes
/// transport components back from the main thread.
/// </summary>
public sealed class FactoryLinearTransferResolver : IDisposable
{
    private NativeParallelHashMap<int2, int> indexByCell;
    private NativeList<TransportTopologyNode> topology;
    private NativeList<byte> loopVisitState;
    private NativeList<int> loopPathIndex;
    private NativeList<int> loopPath;
    private NativeList<TransportDynamicNode> dynamicNodes;
    private NativeList<int> candidateForTarget;
    private NativeList<byte> resolutionStates;
    private NativeList<byte> accepted;
    private NativeList<Entity> transferredItems;
    private NativeList<int> resolutionStack;
    private NativeHashSet<Entity> processedJunctions;
    private NativeHashSet<Entity> reservedTargets;
    private NativeReference<int> candidateInspectionRef;
    private NativeReference<int> routingPassCountRef;
    private NativeReference<int> readyRequestCountRef;
    private NativeReference<int> acceptedTransferCountRef;
    private NativeParallelHashMap<ItemId, Entity> itemPoolByType;
    private NativeParallelHashMap<ItemId, ItemPrefabVisualInfo>
        itemPrefabVisualInfo;
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
        topology = new NativeList<TransportTopologyNode>(
            16,
            Allocator.Persistent);
        loopVisitState = new NativeList<byte>(16, Allocator.Persistent);
        loopPathIndex = new NativeList<int>(16, Allocator.Persistent);
        loopPath = new NativeList<int>(16, Allocator.Persistent);
        dynamicNodes = new NativeList<TransportDynamicNode>(
            16,
            Allocator.Persistent);
        candidateForTarget = new NativeList<int>(16, Allocator.Persistent);
        resolutionStates = new NativeList<byte>(16, Allocator.Persistent);
        accepted = new NativeList<byte>(16, Allocator.Persistent);
        transferredItems = new NativeList<Entity>(16, Allocator.Persistent);
        resolutionStack = new NativeList<int>(16, Allocator.Persistent);
        processedJunctions = new NativeHashSet<Entity>(
            16,
            Allocator.Persistent);
        reservedTargets = new NativeHashSet<Entity>(
            16,
            Allocator.Persistent);
        candidateInspectionRef =
            new NativeReference<int>(Allocator.Persistent);
        routingPassCountRef =
            new NativeReference<int>(Allocator.Persistent);
        readyRequestCountRef =
            new NativeReference<int>(Allocator.Persistent);
        acceptedTransferCountRef =
            new NativeReference<int>(Allocator.Persistent);
        itemPoolByType =
            new NativeParallelHashMap<ItemId, Entity>(
                16,
                Allocator.Persistent);
        itemPrefabVisualInfo =
            new NativeParallelHashMap<ItemId, ItemPrefabVisualInfo>(
                16,
                Allocator.Persistent);
    }

    public int TopologyRebuildCount { get; private set; }
    public int NodeCount => topology.IsCreated ? topology.Length : 0;
    public int LastTopologyRebuildNodeCount { get; private set; }
    public double LastTopologyRebuildMilliseconds { get; private set; }
    public double TotalTopologyRebuildMilliseconds { get; private set; }
    public int LoopCount { get; private set; }
    public int LastCandidateInspectionCount =>
        candidateInspectionRef.IsCreated
            ? candidateInspectionRef.Value
            : 0;
    public int LastRoutingPassCount =>
        routingPassCountRef.IsCreated
            ? routingPassCountRef.Value
            : 0;
    public int LastReadyRequestCount =>
        readyRequestCountRef.IsCreated
            ? readyRequestCountRef.Value
            : 0;
    public int LastAcceptedTransferCount =>
        acceptedTransferCountRef.IsCreated
            ? acceptedTransferCountRef.Value
            : 0;

    public void EnsureTopology(
        uint revision,
        NativeArray<Entity> beltEntities,
        NativeArray<BeltTopology> beltTopologies,
        NativeArray<Entity> mergerEntities,
        NativeArray<Merger> mergers,
        NativeArray<Entity> splitterEntities,
        NativeArray<Splitter> splitters,
        bool forceRebuild = false)
    {
        ThrowIfDisposed();
        ValidateSnapshots(
            beltEntities,
            beltTopologies,
            mergerEntities,
            mergers,
            splitterEntities,
            splitters);

        if (!forceRebuild &&
            hasCachedRevision &&
            revision == cachedRevision &&
            beltTopologies.Length == cachedBeltCount &&
            mergers.Length == cachedMergerCount &&
            splitters.Length == cachedSplitterCount)
        {
            return;
        }

        RebuildTopology(
            revision,
            beltEntities,
            beltTopologies,
            mergerEntities,
            mergers,
            splitterEntities,
            splitters);
    }

    /// <summary>
    /// Creates a transfer arbitration job. The caller must assign the
    /// component/buffer lookups, port-owner arrays and ECB for the ECS path,
    /// or leave them default and assign the state arrays for the standalone
    /// regression path.
    /// </summary>
    public FactoryTransferArbitrationJob CreateArbitrationJob()
    {
        ThrowIfDisposed();
        return new FactoryTransferArbitrationJob
        {
            IndexByCell = indexByCell,
            Topology = topology,
            DynamicNodes = dynamicNodes,
            CandidateForTarget = candidateForTarget,
            ResolutionStates = resolutionStates,
            Accepted = accepted,
            TransferredItems = transferredItems,
            ResolutionStack = resolutionStack,
            ProcessedJunctions = processedJunctions,
            ReservedTargets = reservedTargets,
            CandidateInspectionRef = candidateInspectionRef,
            RoutingPassCountRef = routingPassCountRef,
            ReadyRequestCountRef = readyRequestCountRef,
            AcceptedTransferCountRef = acceptedTransferCountRef,
            ItemPoolByType = itemPoolByType,
            ItemPrefabVisualInfo = itemPrefabVisualInfo,
            BeltCount = cachedBeltCount,
            MergerCount = cachedMergerCount,
            SplitterCount = cachedSplitterCount,
            LoopCount = LoopCount
        };
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
        DisposeIfCreated(ref loopVisitState);
        DisposeIfCreated(ref loopPathIndex);
        DisposeIfCreated(ref loopPath);
        DisposeIfCreated(ref dynamicNodes);
        DisposeIfCreated(ref candidateForTarget);
        DisposeIfCreated(ref resolutionStates);
        DisposeIfCreated(ref accepted);
        DisposeIfCreated(ref transferredItems);
        DisposeIfCreated(ref resolutionStack);
        DisposeIfCreated(ref processedJunctions);
        DisposeIfCreated(ref reservedTargets);
        DisposeIfCreated(ref candidateInspectionRef);
        DisposeIfCreated(ref routingPassCountRef);
        DisposeIfCreated(ref readyRequestCountRef);
        DisposeIfCreated(ref acceptedTransferCountRef);
        DisposeIfCreated(ref itemPoolByType);
        DisposeIfCreated(ref itemPrefabVisualInfo);
    }
    private void RebuildTopology(
        uint revision,
        NativeArray<Entity> beltEntities,
        NativeArray<BeltTopology> beltTopologies,
        NativeArray<Entity> mergerEntities,
        NativeArray<Merger> mergers,
        NativeArray<Entity> splitterEntities,
        NativeArray<Splitter> splitters)
    {
        long rebuildStarted = Stopwatch.GetTimestamp();
        int nodeCount =
            beltTopologies.Length + mergers.Length + splitters.Length;
        EnsureCapacity(nodeCount);
        topology.ResizeUninitialized(nodeCount);
        indexByCell.Clear();
        if (indexByCell.Capacity < math.max(16, nodeCount))
        {
            indexByCell.Capacity = math.max(16, nodeCount);
        }

        int nodeIndex = 0;
        for (int i = 0; i < beltTopologies.Length; i++, nodeIndex++)
        {
            SetTopologyNode(
                nodeIndex,
                beltEntities[i],
                FactoryTransportKind.Belt,
                i,
                beltTopologies[i].Cell,
                beltTopologies[i].Direction);
        }

        for (int i = 0; i < mergers.Length; i++, nodeIndex++)
        {
            SetTopologyNode(
                nodeIndex,
                mergerEntities[i],
                FactoryTransportKind.Merger,
                i,
                mergers[i].Cell,
                mergers[i].Direction);
        }

        for (int i = 0; i < splitters.Length; i++, nodeIndex++)
        {
            SetTopologyNode(
                nodeIndex,
                splitterEntities[i],
                FactoryTransportKind.Splitter,
                i,
                splitters[i].Cell,
                splitters[i].Direction);
        }

        BuildConnections();
        LoopCount = MarkBeltLoops();

        cachedRevision = revision;
        cachedBeltCount = beltTopologies.Length;
        cachedMergerCount = mergers.Length;
        cachedSplitterCount = splitters.Length;
        hasCachedRevision = true;
        TopologyRebuildCount++;
        LastTopologyRebuildNodeCount = nodeCount;
        LastTopologyRebuildMilliseconds =
            (Stopwatch.GetTimestamp() - rebuildStarted) * 1000.0 /
            Stopwatch.Frequency;
        TotalTopologyRebuildMilliseconds +=
            LastTopologyRebuildMilliseconds;
    }

    private void SetTopologyNode(
        int nodeIndex,
        Entity entity,
        FactoryTransportKind kind,
        int sourceIndex,
        int2 cell,
        int2 direction)
    {
        topology[nodeIndex] = new TransportTopologyNode
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
            TransportTopologyNode target = topology[targetIndex];
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
                        TransportTopologyAccess.RotateClockwise(
                            target.Direction));
                    TryConnectInput(
                        targetIndex,
                        2,
                        TransportTopologyAccess.RotateCounterClockwise(
                            target.Direction));
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
        TransportTopologyNode target)
    {
        int2 straight = target.Direction;
        if (TryConnectInput(targetIndex, 0, straight))
        {
            return;
        }

        int2 right = TransportTopologyAccess.RotateClockwise(straight);
        if (TryConnectInput(targetIndex, 0, right))
        {
            return;
        }

        TryConnectInput(
            targetIndex,
            0,
            TransportTopologyAccess.RotateCounterClockwise(straight));
    }

    private bool TryConnectInput(
        int targetIndex,
        int inputIndex,
        int2 travelDirection)
    {
        TransportTopologyNode target = topology[targetIndex];
        int2 sourceCell = target.Cell - travelDirection;
        if (!indexByCell.TryGetValue(
                sourceCell,
                out int sourceIndex))
        {
            return false;
        }

        TransportTopologyNode source = topology[sourceIndex];
        if (!TryGetOutputIndex(
                source,
                travelDirection,
                out int outputIndex))
        {
            return false;
        }

        TransportTopologyAccess.SetInput(
            ref target,
            inputIndex,
            sourceIndex);
        TransportTopologyAccess.SetOutput(
            ref source,
            outputIndex,
            targetIndex);
        target.InputCount = (byte)math.max(
            target.InputCount,
            inputIndex + 1);
        topology[targetIndex] = target;
        topology[sourceIndex] = source;
        return true;
    }

    private int MarkBeltLoops()
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

    private static bool TryGetOutputIndex(
        in TransportTopologyNode source,
        int2 travelDirection,
        out int outputIndex)
    {
        if (source.Kind != FactoryTransportKind.Splitter)
        {
            outputIndex = 0;
            return math.all(source.Direction == travelDirection);
        }

        outputIndex = TransportTopologyAccess.GetSplitterOutputIndex(
            source.Direction,
            travelDirection);
        return outputIndex >= 0;
    }

    private void EnsureCapacity(int nodeCount)
    {
        int capacity = math.max(16, nodeCount);
        EnsureCapacity(topology, capacity);
        EnsureCapacity(loopVisitState, capacity);
        EnsureCapacity(loopPathIndex, capacity);
        EnsureCapacity(loopPath, capacity);
        EnsureCapacity(dynamicNodes, capacity);
        EnsureCapacity(candidateForTarget, capacity);
        EnsureCapacity(resolutionStates, capacity);
        EnsureCapacity(accepted, capacity);
        EnsureCapacity(transferredItems, capacity);
        EnsureCapacity(resolutionStack, capacity);
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

    private static void ValidateSnapshots(
        NativeArray<Entity> beltEntities,
        NativeArray<BeltTopology> beltTopologies,
        NativeArray<Entity> mergerEntities,
        NativeArray<Merger> mergers,
        NativeArray<Entity> splitterEntities,
        NativeArray<Splitter> splitters)
    {
        if (!beltEntities.IsCreated ||
            !beltTopologies.IsCreated ||
            !mergerEntities.IsCreated ||
            !mergers.IsCreated ||
            !splitterEntities.IsCreated ||
            !splitters.IsCreated)
        {
            throw new ArgumentException(
                "Transport snapshots must be created native containers.");
        }

        if (beltEntities.Length != beltTopologies.Length ||
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

    private static void DisposeIfCreated<T>(
        ref NativeHashSet<T> container)
        where T : unmanaged, IEquatable<T>
    {
        if (container.IsCreated)
        {
            container.Dispose();
        }
    }

    private static void DisposeIfCreated(
        ref NativeReference<int> container)
    {
        if (container.IsCreated)
        {
            container.Dispose();
        }
    }
}
/// <summary>
/// Burst-compilable single-threaded arbitration job for one fixed tick.
/// When <see cref="EnableInterface"/> is set the dynamic transport state is
/// loaded from and stored to component lookups and building-port transfers are
/// executed. Otherwise the job operates purely on the supplied state arrays,
/// which is the standalone regression path.
/// </summary>
[BurstCompile]
public partial struct FactoryTransferArbitrationJob : IJob
{
    private const int MaxRoutingPasses = 3;

    private enum ResolutionState : byte
    {
        Unresolved,
        Resolving,
        Accepted,
        Rejected
    }

    [ReadOnly]
    internal NativeParallelHashMap<int2, int> IndexByCell;
    [ReadOnly]
    internal NativeList<TransportTopologyNode> Topology;

    internal NativeList<TransportDynamicNode> DynamicNodes;
    internal NativeList<int> CandidateForTarget;
    internal NativeList<byte> ResolutionStates;
    internal NativeList<byte> Accepted;
    internal NativeList<Entity> TransferredItems;
    internal NativeList<int> ResolutionStack;
    internal NativeHashSet<Entity> ProcessedJunctions;
    internal NativeHashSet<Entity> ReservedTargets;
    internal NativeReference<int> CandidateInspectionRef;
    internal NativeReference<int> RoutingPassCountRef;
    internal NativeReference<int> ReadyRequestCountRef;
    internal NativeReference<int> AcceptedTransferCountRef;

    public NativeArray<BeltState> BeltStates;
    public NativeArray<Merger> Mergers;
    public NativeArray<Splitter> Splitters;

    [ReadOnly]
    public NativeArray<Entity> InputPortOwners;
    [ReadOnly]
    public NativeArray<Entity> OutputPortOwners;

    public ComponentLookup<BeltState> BeltStateLookup;
    public ComponentLookup<Merger> MergerLookup;
    public ComponentLookup<Splitter> SplitterLookup;
    [ReadOnly]
    public ComponentLookup<GridPlacement> GridPlacementLookup;
    [ReadOnly]
    public ComponentLookup<Item> ItemLookup;
    [ReadOnly]
    public ComponentLookup<ItemPortBufferGeneration> GenerationLookup;
    [ReadOnly]
    public BufferLookup<BuildingPort> BuildingPortLookup;
    public BufferLookup<ItemInputPortCurrent> InputPortCurrentLookup;
    public BufferLookup<ItemInputPortNext> InputPortNextLookup;
    public BufferLookup<ItemOutputPortCurrent> OutputPortCurrentLookup;
    public BufferLookup<ItemOutputPortNext> OutputPortNextLookup;
    public BufferLookup<ItemTransferReceiptNext> ReceiptNextLookup;
    public ComponentLookup<Stage3SimulationStats> StatsLookup;

    public Entity StatsEntity;
    public int BeltCount;
    public int MergerCount;
    public int SplitterCount;
    public int LoopCount;
    public byte EnableInterface;
    public EntityCommandBuffer Ecb;

    [ReadOnly]
    public NativeParallelHashMap<ItemId, Entity> ItemPoolByType;
    public ComponentLookup<ItemPool> ItemPoolLookup;
    public BufferLookup<ItemPoolEntry> ItemPoolBufferLookup;
    [ReadOnly]
    public ComponentLookup<DisableRendering> DisableRenderingLookup;
    [ReadOnly]
    public NativeParallelHashMap<ItemId, ItemPrefabVisualInfo>
        ItemPrefabVisualInfo;

    private int candidateInspectionCount;
    private int routingPassCount;

    public void Execute()
    {
        LoadDynamicState();
        ProcessedJunctions.Clear();
        ReservedTargets.Clear();

        candidateInspectionCount = 0;
        routingPassCount = 0;
        int interfaceRequestCount = 0;
        int interfaceAcceptedCount = 0;
        int readyRequestCount = 0;
        int acceptedTransferCount = 0;

        if (EnableInterface == 1)
        {
            interfaceAcceptedCount = ConsumeBuildingInputs(
                ref interfaceRequestCount);
        }

        for (int pass = 0; pass < MaxRoutingPasses; pass++)
        {
            routingPassCount++;
            int passReadyCount = BuildOutgoingRequests();
            readyRequestCount += passReadyCount;
            if (passReadyCount == 0)
            {
                break;
            }

            SelectIncomingCandidates();
            ResolveCandidates();
            int passAcceptedCount = Commit();
            acceptedTransferCount += passAcceptedCount;
            if (passAcceptedCount == 0)
            {
                break;
            }
        }

        if (EnableInterface == 1)
        {
            interfaceAcceptedCount += InjectBuildingOutputs(
                ref interfaceRequestCount);
        }

        acceptedTransferCount += interfaceAcceptedCount;
        readyRequestCount += interfaceRequestCount;

        StoreDynamicState();

        CandidateInspectionRef.Value = candidateInspectionCount;
        RoutingPassCountRef.Value = routingPassCount;
        ReadyRequestCountRef.Value = readyRequestCount;
        AcceptedTransferCountRef.Value = acceptedTransferCount;

        if (EnableInterface == 1)
        {
            UpdateStats(readyRequestCount, acceptedTransferCount);
        }
    }
    private void LoadDynamicState()
    {
        DynamicNodes.ResizeUninitialized(Topology.Length);
        for (int i = 0; i < Topology.Length; i++)
        {
            TransportTopologyNode node = Topology[i];
            TransportDynamicNode state = new TransportDynamicNode
            {
                TargetIndex = -1,
                OutputIndex = -1
            };
            switch (node.Kind)
            {
                case FactoryTransportKind.Belt:
                    BeltState belt = EnableInterface == 1
                        ? BeltStateLookup[node.Entity]
                        : BeltStates[node.SourceIndex];
                    state.CurrentItem = belt.CurrentItem;
                    state.Progress = belt.Progress;
                    break;
                case FactoryTransportKind.Merger:
                    Merger merger = EnableInterface == 1
                        ? MergerLookup[node.Entity]
                        : Mergers[node.SourceIndex];
                    state.CurrentItem = merger.CurrentItem;
                    state.Progress = merger.CurrentItem == Entity.Null
                        ? 0f
                        : 1f;
                    state.Cursor = TransportTopologyAccess.WrapThree(
                        merger.NextInputIndex);
                    break;
                case FactoryTransportKind.Splitter:
                    Splitter splitter = EnableInterface == 1
                        ? SplitterLookup[node.Entity]
                        : Splitters[node.SourceIndex];
                    state.CurrentItem = splitter.CurrentItem;
                    state.Progress = splitter.CurrentItem == Entity.Null
                        ? 0f
                        : 1f;
                    state.Cursor = TransportTopologyAccess.WrapThree(
                        splitter.NextOutputIndex);
                    break;
            }

            DynamicNodes[i] = state;
        }
    }

    private void StoreDynamicState()
    {
        for (int i = 0; i < Topology.Length; i++)
        {
            TransportTopologyNode node = Topology[i];
            TransportDynamicNode state = DynamicNodes[i];
            switch (node.Kind)
            {
                case FactoryTransportKind.Belt:
                    if (EnableInterface == 1)
                    {
                        BeltState belt = BeltStateLookup[node.Entity];
                        belt.CurrentItem = state.CurrentItem;
                        belt.Progress = state.Progress;
                        BeltStateLookup[node.Entity] = belt;
                    }
                    else
                    {
                        BeltState belt = BeltStates[node.SourceIndex];
                        belt.CurrentItem = state.CurrentItem;
                        belt.Progress = state.Progress;
                        BeltStates[node.SourceIndex] = belt;
                    }
                    break;
                case FactoryTransportKind.Merger:
                    if (EnableInterface == 1)
                    {
                        Merger merger = MergerLookup[node.Entity];
                        merger.CurrentItem = state.CurrentItem;
                        merger.NextInputIndex = state.Cursor;
                        MergerLookup[node.Entity] = merger;
                    }
                    else
                    {
                        Merger merger = Mergers[node.SourceIndex];
                        merger.CurrentItem = state.CurrentItem;
                        merger.NextInputIndex = state.Cursor;
                        Mergers[node.SourceIndex] = merger;
                    }
                    break;
                case FactoryTransportKind.Splitter:
                    if (EnableInterface == 1)
                    {
                        Splitter splitter = SplitterLookup[node.Entity];
                        splitter.CurrentItem = state.CurrentItem;
                        splitter.NextOutputIndex = state.Cursor;
                        SplitterLookup[node.Entity] = splitter;
                    }
                    else
                    {
                        Splitter splitter = Splitters[node.SourceIndex];
                        splitter.CurrentItem = state.CurrentItem;
                        splitter.NextOutputIndex = state.Cursor;
                        Splitters[node.SourceIndex] = splitter;
                    }
                    break;
            }
        }
    }

    private int ConsumeBuildingInputs(ref int requestCount)
    {
        int acceptedCount = 0;
        for (int ownerIndex = 0;
             ownerIndex < InputPortOwners.Length;
             ownerIndex++)
        {
            Entity owner = InputPortOwners[ownerIndex];
            if (!GridPlacementLookup.HasComponent(owner) ||
                !BuildingPortLookup.HasBuffer(owner) ||
                !InputPortCurrentLookup.HasBuffer(owner) ||
                !InputPortNextLookup.HasBuffer(owner) ||
                !ReceiptNextLookup.HasBuffer(owner))
            {
                continue;
            }

            GridPlacement placement = GridPlacementLookup[owner];
            DynamicBuffer<BuildingPort> buildingPorts =
                BuildingPortLookup[owner];
            DynamicBuffer<ItemInputPortSnapshot> ports =
                GetCurrentInputPorts(owner);
            DynamicBuffer<ItemTransferReceiptNext> receipts =
                ReceiptNextLookup[owner];

            for (int i = 0; i < ports.Length; i++)
            {
                ItemInputPortSnapshot port = ports[i];
                if (port.Enabled == 0)
                {
                    continue;
                }

                int availableCapacity = GetEffectiveCount(
                    port.ReservedTransferCount,
                    port.AppliedTransferCount,
                    port.FreeCapacity);
                if (availableCapacity <= 0 ||
                    !TryGetBuildingPort(
                        buildingPorts,
                        BuildingPortType.Input,
                        port.PortIndex,
                        out BuildingPort geometry))
                {
                    continue;
                }

                int2 sourceCell = EcsGridUtility.GetBuildingCell(
                    placement,
                    geometry.CellOffset);
                int2 direction = EcsGridUtility.Rotate(
                    geometry.Direction,
                    placement.QuarterTurns);
                if (!IndexByCell.TryGetValue(
                        sourceCell,
                        out int sourceIndex) ||
                    !TryGetReadyItem(
                        sourceIndex,
                        direction,
                        out Entity itemEntity,
                        out int sourceOutputIndex))
                {
                    continue;
                }

                requestCount++;
                if (itemEntity == Entity.Null ||
                    !ItemLookup.HasComponent(itemEntity))
                {
                    continue;
                }

                ItemId itemType = ItemLookup[itemEntity].ItemType;
                if (port.FilterMode ==
                        ItemPortFilterMode.ExactItemType &&
                    port.AcceptedItemType != itemType)
                {
                    continue;
                }

                ClearTransportItem(sourceIndex, sourceOutputIndex);
                ProcessedJunctions.Add(Topology[sourceIndex].Entity);
                receipts.Add(new ItemTransferReceiptNext
                {
                    Value = new ItemTransferReceipt
                    {
                        ItemType = itemType,
                        Count = 1,
                        PortIndex = port.PortIndex,
                        Kind = ItemTransferReceiptKind.InputAccepted
                    }
                });
                port.ReservedTransferCount++;
                ports[i] = port;
                ReturnItemToPool(itemEntity, itemType);
                acceptedCount++;
            }
        }

        return acceptedCount;
    }
    private int InjectBuildingOutputs(ref int requestCount)
    {
        ReservedTargets.Clear();
        int acceptedCount = 0;
        for (int ownerIndex = 0;
             ownerIndex < OutputPortOwners.Length;
             ownerIndex++)
        {
            Entity owner = OutputPortOwners[ownerIndex];
            if (!GridPlacementLookup.HasComponent(owner) ||
                !BuildingPortLookup.HasBuffer(owner) ||
                !OutputPortCurrentLookup.HasBuffer(owner) ||
                !OutputPortNextLookup.HasBuffer(owner) ||
                !ReceiptNextLookup.HasBuffer(owner))
            {
                continue;
            }

            GridPlacement placement = GridPlacementLookup[owner];
            DynamicBuffer<BuildingPort> buildingPorts =
                BuildingPortLookup[owner];
            DynamicBuffer<ItemOutputPortSnapshot> ports =
                GetCurrentOutputPorts(owner);
            DynamicBuffer<ItemTransferReceiptNext> receipts =
                ReceiptNextLookup[owner];

            for (int i = 0; i < ports.Length; i++)
            {
                ItemOutputPortSnapshot port = ports[i];
                if (port.Enabled == 0 || !port.ItemType.IsValid)
                {
                    continue;
                }

                int availableCount = GetEffectiveCount(
                    port.ReservedTransferCount,
                    port.AppliedTransferCount,
                    port.AvailableCount);
                if (availableCount <= 0 ||
                    !TryGetBuildingPort(
                        buildingPorts,
                        BuildingPortType.Output,
                        port.PortIndex,
                        out BuildingPort geometry))
                {
                    continue;
                }

                int2 targetCell = EcsGridUtility.GetBuildingCell(
                    placement,
                    geometry.CellOffset);
                int2 direction = EcsGridUtility.Rotate(
                    geometry.Direction,
                    placement.QuarterTurns);
                if (!IndexByCell.TryGetValue(
                        targetCell,
                        out int targetIndex))
                {
                    continue;
                }

                Entity targetEntity = Topology[targetIndex].Entity;
                if (ReservedTargets.Contains(targetEntity) ||
                    !CanInjectIntoTransport(targetIndex, direction))
                {
                    continue;
                }

                requestCount++;
                if (!TryGetItemPrefab(port.ItemType, out Entity prefab))
                {
                    continue;
                }

                ReservedTargets.Add(targetEntity);

                float3 position = new float3(
                    targetCell.x + 0.5f,
                    0.535f,
                    targetCell.y + 0.5f);
                bool reused = TryGetPooledItem(
                    port.ItemType,
                    out Entity item);
                if (!reused)
                {
                    item = Ecb.Instantiate(prefab);
                }
                else if (DisableRenderingLookup.HasComponent(item))
                {
                    Ecb.RemoveComponent<DisableRendering>(item);
                }

                Item itemData = new Item
                {
                    ItemType = port.ItemType
                };
                if (ItemLookup.HasComponent(prefab) || reused)
                {
                    Ecb.SetComponent(item, itemData);
                }
                else
                {
                    Ecb.AddComponent(item, itemData);
                }
                Ecb.SetComponentEnabled<Item>(item, true);

                ItemPrefabVisualInfo prefabInfo =
                    ItemPrefabVisualInfo[port.ItemType];
                if (prefabInfo.HasTransform != 0)
                {
                    LocalTransform transform = prefabInfo.Transform;
                    transform.Position = position;
                    Ecb.SetComponent(item, transform);
                }
                else
                {
                    Ecb.AddComponent(
                        item,
                        LocalTransform.FromPosition(position));
                }

                ItemVisualState visualState = new ItemVisualState
                {
                    FromPosition = position,
                    ToPosition = position,
                    Progress = 0f
                };
                if (prefabInfo.HasVisualState != 0)
                {
                    Ecb.SetComponent(item, visualState);
                }
                else
                {
                    Ecb.AddComponent(item, visualState);
                }

                SetTransportItem(targetIndex, item);
                RecordInjectedItemViaEcb(targetIndex, item);
                receipts.Add(new ItemTransferReceiptNext
                {
                    Value = new ItemTransferReceipt
                    {
                        ItemType = port.ItemType,
                        Count = 1,
                        PortIndex = port.PortIndex,
                        Kind = ItemTransferReceiptKind.OutputTransferred
                    }
                });
                port.ReservedTransferCount++;
                ports[i] = port;
                acceptedCount++;
            }
        }

        return acceptedCount;
    }
    private void RecordInjectedItemViaEcb(int targetIndex, Entity item)
    {
        TransportTopologyNode target = Topology[targetIndex];
        switch (target.Kind)
        {
            case FactoryTransportKind.Belt:
                BeltState belt = BeltStateLookup[target.Entity];
                belt.CurrentItem = item;
                belt.Progress = 0f;
                Ecb.SetComponent(target.Entity, belt);
                break;
            case FactoryTransportKind.Merger:
                Merger merger = MergerLookup[target.Entity];
                merger.CurrentItem = item;
                Ecb.SetComponent(target.Entity, merger);
                break;
            case FactoryTransportKind.Splitter:
                Splitter splitter = SplitterLookup[target.Entity];
                splitter.CurrentItem = item;
                Ecb.SetComponent(target.Entity, splitter);
                break;
        }
    }

    private int BuildOutgoingRequests()
    {
        int readyCount = 0;
        for (int sourceIndex = 0;
             sourceIndex < Topology.Length;
             sourceIndex++)
        {
            TransportTopologyNode source = Topology[sourceIndex];
            TransportDynamicNode state = DynamicNodes[sourceIndex];
            state.IsReady = 0;
            state.TargetIndex = -1;
            state.OutputIndex = -1;

            if (state.CurrentItem == Entity.Null ||
                (source.Kind == FactoryTransportKind.Belt &&
                 state.Progress < 1f) ||
                (source.Kind != FactoryTransportKind.Belt &&
                 ProcessedJunctions.Contains(source.Entity)))
            {
                DynamicNodes[sourceIndex] = state;
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

            DynamicNodes[sourceIndex] = state;
        }

        return readyCount;
    }

    private void SelectSplitterOutput(
        in TransportTopologyNode source,
        ref TransportDynamicNode state)
    {
        int fallbackOutput = -1;
        int fallbackTarget = -1;

        for (int offset = 0; offset < 3; offset++)
        {
            int outputIndex = TransportTopologyAccess.WrapThree(
                state.Cursor + offset);
            int targetIndex = TransportTopologyAccess.GetOutput(
                source,
                outputIndex);
            if (targetIndex < 0)
            {
                continue;
            }

            if (fallbackOutput < 0)
            {
                fallbackOutput = outputIndex;
                fallbackTarget = targetIndex;
            }

            if (DynamicNodes[targetIndex].CurrentItem == Entity.Null)
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
        ResizeScratch(CandidateForTarget, Topology.Length, -1);

        for (int targetIndex = 0;
             targetIndex < Topology.Length;
             targetIndex++)
        {
            TransportTopologyNode target = Topology[targetIndex];
            int winner = -1;
            int winnerDistance = int.MaxValue;

            for (int inputIndex = 0;
                 inputIndex < target.InputCount;
                 inputIndex++)
            {
                candidateInspectionCount++;
                int candidate = TransportTopologyAccess.GetInput(
                    target,
                    inputIndex);
                if (candidate < 0)
                {
                    continue;
                }

                TransportDynamicNode candidateState =
                    DynamicNodes[candidate];
                if (candidateState.IsReady == 0 ||
                    candidateState.TargetIndex != targetIndex)
                {
                    continue;
                }

                int distance =
                    target.Kind == FactoryTransportKind.Merger
                        ? TransportTopologyAccess.WrapThree(
                            inputIndex -
                            DynamicNodes[targetIndex].Cursor)
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

            CandidateForTarget[targetIndex] = winner;
        }
    }
    private void ResolveCandidates()
    {
        ResizeScratch(
            ResolutionStates,
            Topology.Length,
            (byte)ResolutionState.Unresolved);
        ResizeScratch(Accepted, Topology.Length, (byte)0);

        for (int targetIndex = 0;
             targetIndex < Topology.Length;
             targetIndex++)
        {
            int candidate = CandidateForTarget[targetIndex];
            if (candidate >= 0 && ResolveCandidate(candidate))
            {
                Accepted[candidate] = 1;
            }
        }
    }

    private bool ResolveCandidate(int sourceIndex)
    {
        ResolutionState initialState =
            (ResolutionState)ResolutionStates[sourceIndex];
        if (initialState == ResolutionState.Accepted)
        {
            return true;
        }
        if (initialState == ResolutionState.Rejected)
        {
            return false;
        }

        ResolutionStack.Clear();
        int current = sourceIndex;
        bool canMove;

        while (true)
        {
            ResolutionState currentState =
                (ResolutionState)ResolutionStates[current];
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

            ResolutionStates[current] =
                (byte)ResolutionState.Resolving;
            ResolutionStack.Add(current);

            TransportDynamicNode source = DynamicNodes[current];
            int targetIndex = source.TargetIndex;
            if (targetIndex < 0)
            {
                canMove = false;
                break;
            }
            if (DynamicNodes[targetIndex].CurrentItem == Entity.Null)
            {
                canMove = true;
                break;
            }

            TransportDynamicNode target = DynamicNodes[targetIndex];
            if (target.IsReady == 0 ||
                target.TargetIndex < 0 ||
                CandidateForTarget[target.TargetIndex] != targetIndex)
            {
                canMove = false;
                break;
            }

            current = targetIndex;
        }

        byte finalState = canMove
            ? (byte)ResolutionState.Accepted
            : (byte)ResolutionState.Rejected;
        for (int i = ResolutionStack.Length - 1; i >= 0; i--)
        {
            ResolutionStates[ResolutionStack[i]] = finalState;
        }

        return canMove;
    }

    private int Commit()
    {
        ResizeScratch(TransferredItems, Topology.Length, Entity.Null);
        int acceptedCount = 0;

        for (int sourceIndex = 0;
             sourceIndex < Topology.Length;
             sourceIndex++)
        {
            if (Accepted[sourceIndex] == 0)
            {
                continue;
            }

            TransportTopologyNode source = Topology[sourceIndex];
            TransportDynamicNode state = DynamicNodes[sourceIndex];
            TransferredItems[sourceIndex] = state.CurrentItem;
            state.CurrentItem = Entity.Null;
            state.Progress = 0f;
            if (source.Kind == FactoryTransportKind.Splitter)
            {
                state.Cursor = TransportTopologyAccess.WrapThree(
                    state.OutputIndex + 1);
            }
            if (source.Kind != FactoryTransportKind.Belt)
            {
                ProcessedJunctions.Add(source.Entity);
            }

            DynamicNodes[sourceIndex] = state;
            acceptedCount++;
        }

        for (int sourceIndex = 0;
             sourceIndex < Topology.Length;
             sourceIndex++)
        {
            if (Accepted[sourceIndex] == 0)
            {
                continue;
            }

            TransportDynamicNode source = DynamicNodes[sourceIndex];
            int targetIndex = source.TargetIndex;
            TransportTopologyNode targetTopology =
                Topology[targetIndex];
            TransportDynamicNode target = DynamicNodes[targetIndex];
            target.CurrentItem = TransferredItems[sourceIndex];
            target.Progress = 0f;
            if (targetTopology.Kind != FactoryTransportKind.Belt)
            {
                ProcessedJunctions.Add(targetTopology.Entity);
            }
            if (targetTopology.Kind == FactoryTransportKind.Merger)
            {
                int inputIndex = FindInputIndex(
                    targetTopology,
                    sourceIndex);
                target.Cursor = TransportTopologyAccess.WrapThree(
                    inputIndex + 1);
            }

            DynamicNodes[targetIndex] = target;
        }

        return acceptedCount;
    }

    private int CompareNodes(int leftIndex, int rightIndex)
    {
        TransportTopologyNode left = Topology[leftIndex];
        TransportTopologyNode right = Topology[rightIndex];
        if (left.Cell.x != right.Cell.x)
        {
            return left.Cell.x < right.Cell.x ? -1 : 1;
        }

        if (left.Cell.y != right.Cell.y)
        {
            return left.Cell.y < right.Cell.y ? -1 : 1;
        }

        if (left.Entity.Index != right.Entity.Index)
        {
            return left.Entity.Index < right.Entity.Index ? -1 : 1;
        }

        return 0;
    }
    private void UpdateStats(
        int readyRequestCount,
        int acceptedTransferCount)
    {
        if (StatsEntity == Entity.Null ||
            !StatsLookup.HasComponent(StatsEntity))
        {
            return;
        }

        Stage3SimulationStats stats = StatsLookup[StatsEntity];
        stats.BeltCount = BeltCount;
        stats.MergerCount = MergerCount;
        stats.SplitterCount = SplitterCount;
        stats.LoopCount = LoopCount;
        stats.ReadyRequestCount = readyRequestCount;
        stats.AcceptedTransferCount = acceptedTransferCount;
        stats.TickCount++;
        stats.TotalReadyRequestCount += (ulong)readyRequestCount;
        stats.TotalAcceptedTransferCount +=
            (ulong)acceptedTransferCount;
        StatsLookup[StatsEntity] = stats;
    }

    private bool TryGetReadyItem(
        int sourceIndex,
        int2 expectedDirection,
        out Entity item,
        out int outputIndex)
    {
        TransportTopologyNode source = Topology[sourceIndex];
        TransportDynamicNode state = DynamicNodes[sourceIndex];
        switch (source.Kind)
        {
            case FactoryTransportKind.Belt:
                item = state.CurrentItem;
                outputIndex = 0;
                return item != Entity.Null &&
                       state.Progress >= 1f &&
                       math.all(source.Direction == expectedDirection);
            case FactoryTransportKind.Merger:
                item = state.CurrentItem;
                outputIndex = 0;
                return item != Entity.Null &&
                       math.all(source.Direction == expectedDirection);
            case FactoryTransportKind.Splitter:
                item = state.CurrentItem;
                outputIndex = TransportTopologyAccess
                    .GetSplitterOutputIndex(
                        source.Direction,
                        expectedDirection);
                return item != Entity.Null && outputIndex >= 0;
            default:
                item = Entity.Null;
                outputIndex = -1;
                return false;
        }
    }

    private void ClearTransportItem(
        int sourceIndex,
        int outputIndex)
    {
        TransportTopologyNode source = Topology[sourceIndex];
        TransportDynamicNode state = DynamicNodes[sourceIndex];
        switch (source.Kind)
        {
            case FactoryTransportKind.Belt:
                state.CurrentItem = Entity.Null;
                state.Progress = 0f;
                break;
            case FactoryTransportKind.Merger:
                state.CurrentItem = Entity.Null;
                break;
            case FactoryTransportKind.Splitter:
                state.CurrentItem = Entity.Null;
                state.Cursor = TransportTopologyAccess.WrapThree(
                    outputIndex + 1);
                break;
        }

        DynamicNodes[sourceIndex] = state;
    }

    private bool CanInjectIntoTransport(
        int targetIndex,
        int2 direction)
    {
        TransportTopologyNode target = Topology[targetIndex];
        TransportDynamicNode state = DynamicNodes[targetIndex];
        switch (target.Kind)
        {
            case FactoryTransportKind.Belt:
                return state.CurrentItem == Entity.Null;
            case FactoryTransportKind.Merger:
                return state.CurrentItem == Entity.Null &&
                       TransportTopologyAccess.GetMergerInputIndex(
                           target.Direction,
                           direction) >= 0;
            case FactoryTransportKind.Splitter:
                return state.CurrentItem == Entity.Null &&
                       math.all(direction == target.Direction);
            default:
                return false;
        }
    }

    private void SetTransportItem(int targetIndex, Entity item)
    {
        TransportDynamicNode target = DynamicNodes[targetIndex];
        target.CurrentItem = item;
        target.Progress = 0f;
        DynamicNodes[targetIndex] = target;
    }

    private bool TryGetPooledItem(ItemId itemType, out Entity item)
    {
        item = Entity.Null;
        if (!ItemPoolByType.IsCreated ||
            !ItemPoolByType.TryGetValue(
                itemType,
                out Entity poolEntity) ||
            !ItemPoolLookup.HasComponent(poolEntity) ||
            !ItemPoolBufferLookup.HasBuffer(poolEntity))
        {
            return false;
        }

        ItemPool pool = ItemPoolLookup[poolEntity];
        DynamicBuffer<ItemPoolEntry> entries =
            ItemPoolBufferLookup[poolEntity];
        while (pool.FreeCursor < entries.Length)
        {
            Entity candidate = entries[pool.FreeCursor].Entity;
            entries[pool.FreeCursor] = default;
            pool.FreeCursor++;
            if (candidate != Entity.Null &&
                ItemLookup.HasComponent(candidate) &&
                !ItemLookup.IsComponentEnabled(candidate))
            {
                item = candidate;
                break;
            }
        }

        ItemPoolLookup[poolEntity] = pool;
        return item != Entity.Null;
    }

    private void ReturnItemToPool(Entity itemEntity, ItemId itemType)
    {
        if (!ItemPoolByType.IsCreated ||
            !ItemPoolByType.TryGetValue(
                itemType,
                out Entity poolEntity) ||
            !ItemPoolLookup.HasComponent(poolEntity) ||
            !ItemPoolBufferLookup.HasBuffer(poolEntity))
        {
            Ecb.DestroyEntity(itemEntity);
            return;
        }

        ItemPoolBufferLookup[poolEntity].Add(
            new ItemPoolEntry
            {
                Entity = itemEntity
            });
        if (!DisableRenderingLookup.HasComponent(itemEntity))
        {
            Ecb.AddComponent<DisableRendering>(itemEntity);
        }
        Ecb.SetComponentEnabled<Item>(itemEntity, false);
    }

    private bool TryGetItemPrefab(ItemId itemType, out Entity prefab)
    {
        if (ItemPrefabVisualInfo.TryGetValue(
                itemType,
                out ItemPrefabVisualInfo info))
        {
            prefab = info.Prefab;
            return prefab != Entity.Null;
        }

        prefab = Entity.Null;
        return false;
    }

    private static bool TryGetBuildingPort(
        in DynamicBuffer<BuildingPort> ports,
        BuildingPortType type,
        byte index,
        out BuildingPort result)
    {
        for (int i = 0; i < ports.Length; i++)
        {
            BuildingPort port = ports[i];
            if (port.Type == type && port.Index == index)
            {
                result = port;
                return true;
            }
        }

        result = default;
        return false;
    }

    private DynamicBuffer<ItemInputPortSnapshot> GetCurrentInputPorts(
        Entity owner)
    {
        byte generation = GenerationLookup.HasComponent(owner)
            ? GenerationLookup[owner].Value
            : (byte)0;
        return generation == 0
            ? InputPortCurrentLookup[owner]
                .Reinterpret<ItemInputPortSnapshot>()
            : InputPortNextLookup[owner]
                .Reinterpret<ItemInputPortSnapshot>();
    }

    private DynamicBuffer<ItemOutputPortSnapshot> GetCurrentOutputPorts(
        Entity owner)
    {
        byte generation = GenerationLookup.HasComponent(owner)
            ? GenerationLookup[owner].Value
            : (byte)0;
        return generation == 0
            ? OutputPortCurrentLookup[owner]
                .Reinterpret<ItemOutputPortSnapshot>()
            : OutputPortNextLookup[owner]
                .Reinterpret<ItemOutputPortSnapshot>();
    }

    private static int GetEffectiveCount(
        ulong reserved,
        ulong applied,
        int publishedCount)
    {
        ulong outstanding = reserved > applied
            ? reserved - applied
            : 0;
        return outstanding >= (ulong)math.max(0, publishedCount)
            ? 0
            : publishedCount - (int)outstanding;
    }

    private static int FindInputIndex(
        in TransportTopologyNode target,
        int sourceIndex)
    {
        for (int i = 0; i < target.InputCount; i++)
        {
            if (TransportTopologyAccess.GetInput(target, i) ==
                sourceIndex)
            {
                return i;
            }
        }

        return 0;
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
}
