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

// Immutable recipe data. Keep it separate from ItemProcessState so the
// fixed-step progress job only writes the small, hot runtime component.
[InternalBufferCapacity(4)]
public struct ItemProcessRecipe : IBufferElementData
{
    public Entity InputItemType;
    public Entity OutputItemType;
    public int RequiredInputCount;
    public int OutputCount;
    public int DurationTicks;
}

[InternalBufferCapacity(4)]
public struct ItemProcessInput : IBufferElementData
{
    public Entity ItemType;
    public int Count;
}

public struct ItemProcessState : IComponentData
{
    // The transport stage owns this entity reference. The processing stage
    // only publishes PendingOutputCount and never performs structural changes.
    public Entity PendingOutput;
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
}

[InternalBufferCapacity(4)]
public struct StoredItemCount : IBufferElementData
{
    public Entity ItemType;
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
