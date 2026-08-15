using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Static, low-frequency belt data. Never written by the fixed-tick
/// simulation after a building is placed.
/// </summary>
public struct BeltTopology : IComponentData
{
    public float CellsPerSecond;
    public GridCell Cell;
    public int2 Direction;
}

/// <summary>
/// A directed transport edge that cannot be inferred from same-level planar
/// adjacency. Connector construction writes these edges to the grid buffer;
/// the topology rebuild resolves the entity endpoints to continuous node
/// indices. Fixed-tick simulation never performs spatial connector queries.
/// </summary>
[InternalBufferCapacity(8)]
public struct TransportExplicitEdge : IBufferElementData
{
    public Entity Source;
    public Entity Target;
    public byte SourceOutputIndex;
    public byte TargetInputIndex;
}

/// <summary>
/// High-frequency belt state written every fixed tick by BeltProgressSystem
/// and BeltTransferSystem.
/// </summary>
public struct BeltState : IComponentData
{
    public Entity CurrentItem;
    public float Progress;
}

public struct BeltVisualParts : IComponentData
{
    public Entity EastEdge;
    public Entity NorthEdge;
    public Entity WestEdge;
    public Entity SouthEdge;
    public Entity DirectionTriangle;
}

public struct Merger : IComponentData
{
    public GridCell Cell;
    public int2 Direction;
    public Entity CurrentItem;
    public int NextInputIndex;
}

public struct Splitter : IComponentData
{
    public GridCell Cell;
    public int2 Direction;
    public Entity CurrentItem;
    public int NextOutputIndex;
}

public struct Item : IComponentData, IEnableableComponent
{
    public ItemId ItemType;
}

/// <summary>
/// Interpolation endpoints captured once per fixed tick. The presentation
/// system reads this component from an item-centered query and writes the
/// final LocalTransform once per render frame.
/// </summary>
public struct ItemVisualState : IComponentData
{
    public float3 FromPosition;
    public float3 ToPosition;
    public float Progress;
}

/// <summary>
/// Persistent free-list for inactive Item entities. Pooled items keep their
/// Item component disabled until a building output reuses them.
/// </summary>
public struct ItemPool : IComponentData
{
    public ItemId ItemType;
    public Entity Prefab;
    public int FreeCursor;
}

public struct ItemPoolEntry : IBufferElementData
{
    public Entity Entity;
}

public struct Stage3SimulationStats : IComponentData
{
    public int BeltCount;
    public int MergerCount;
    public int SplitterCount;
    public int LoopCount;
    public int ReadyRequestCount;
    public int AcceptedTransferCount;
    public ulong TickCount;
    public ulong TotalReadyRequestCount;
    public ulong TotalAcceptedTransferCount;
}
