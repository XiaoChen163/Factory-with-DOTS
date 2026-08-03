using Unity.Entities;
using Unity.Mathematics;

public struct Belt : IComponentData
{
    public float CellsPerSecond;
    public int2 Cell;
    public int2 Direction;
    public int2 NextCell;
    public Entity CurrentItem;
    public float Progress;
    public bool IsLoop;
    public bool HasOutput;
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
    public float TransferElapsed;
    public float InputInterval;
    public int NextInputIndex;
}

public struct Splitter : IComponentData
{
    public int2 Cell;
    public int2 Direction;
    public Entity CurrentItem;
    public float TransferElapsed;
    public float InputInterval;
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
}
