using Unity.Entities;
using Unity.Mathematics;

public struct Belt : IComponentData
{
    public float Speed;
    public int2 Cell;
    public int2 Direction;
    public int2 NextCell;
    public Entity CurrentItem;
    public float Progress;
    public bool IsLoop;
}

public struct Item : IComponentData
{
    public Entity ItemType;
    public float3 Position;
}

public struct Stage3SimulationStats : IComponentData
{
    public int BeltCount;
    public int LoopCount;
    public int ReadyRequestCount;
    public int AcceptedTransferCount;
    public ulong TickCount;
}
