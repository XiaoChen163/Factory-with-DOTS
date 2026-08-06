using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Static, low-frequency belt data. Never written by the fixed-tick
/// simulation after a building is placed.
/// </summary>
public struct BeltTopology : IComponentData
{
    public float CellsPerSecond;
    public int2 Cell;
    public int2 Direction;
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
    public int2 Cell;
    public int2 Direction;
    public Entity CurrentItem;
    public int NextInputIndex;
}

public struct Splitter : IComponentData
{
    public int2 Cell;
    public int2 Direction;
    public Entity CurrentItem;
    public int NextOutputIndex;
}

public struct Item : IComponentData
{
    public ItemId ItemType;
    public float3 Position;
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
