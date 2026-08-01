using Unity.Entities;
using Unity.Mathematics;

public struct BuildingPrefabCatalog : IComponentData
{
    public Entity Belt;
    public Entity Miner;
    public Entity Furnace;
    public Entity Storage;
    public Entity Merger;
    public Entity Splitter;
    public Entity InputPortVisual;
    public Entity OutputPortVisual;
}

public enum GridBuildCommandType : byte
{
    Place,
    Remove,
    PlaceBeltPath,
    RemoveBeltLine
}

public struct GridBuildCommand : IBufferElementData
{
    public uint RequestId;
    public GridBuildCommandType Type;
    public BuildingKind Kind;
    public int2 StartCell;
    public int2 EndCell;
    public byte QuarterTurns;
    public byte HorizontalFirst;
}

public enum GridBuildFailureReason : byte
{
    None,
    GridNotReady,
    MissingPrefab,
    OutsideGrid,
    CellOccupied,
    NothingToRemove,
    SplitterInputConflict,
    SplitterDirectionConflict,
    MergerOutputFaceConflict,
    InvalidPath,
    TargetIsNotBelt
}

public struct GridBuildResult : IBufferElementData
{
    public uint RequestId;
    public GridBuildCommandType Type;
    public BuildingKind Kind;
    public int2 Cell;
    public byte Success;
    public int AffectedCount;
    public GridBuildFailureReason FailureReason;
}

public enum ItemProcessStatus : byte
{
    Idle,
    Processing,
    Completed,
    OutputBlocked
}

[InternalBufferCapacity(4)]
public struct ItemProcessInput : IBufferElementData
{
    public ItemId ItemType;
    public int Count;
}

public struct ItemProcessState : IComponentData
{
    // Output remains logical until the transfer middleware accepts it.
    public int PendingOutputCount;
    public int ElapsedTicks;
    public int DurationTicks;
    public int SelectedRecipeIndex;
    public int ActiveRecipeIndex;
    public ItemProcessStatus Status;
}

public struct StorageState : IComponentData
{
    public int TotalStored;
    public int Capacity;
}

[InternalBufferCapacity(4)]
public struct StoredItemCount : IBufferElementData
{
    public ItemId ItemType;
    public int Count;
}

public static class BuildingPrefabCatalogUtility
{
    public static Entity GetPrefab(
        in BuildingPrefabCatalog catalog,
        BuildingKind kind)
    {
        switch (kind)
        {
            case BuildingKind.Belt:
                return catalog.Belt;
            case BuildingKind.Miner:
                return catalog.Miner;
            case BuildingKind.Furnace:
                return catalog.Furnace;
            case BuildingKind.Storage:
                return catalog.Storage;
            case BuildingKind.Merger:
                return catalog.Merger;
            case BuildingKind.Splitter:
                return catalog.Splitter;
            default:
                return Entity.Null;
        }
    }
}
