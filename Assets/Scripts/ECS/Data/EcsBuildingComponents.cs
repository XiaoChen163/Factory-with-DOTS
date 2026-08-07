using Unity.Entities;
using Unity.Mathematics;

public struct BuildingPrefabCatalog : IComponentData
{
    public Entity InputPortVisual;
    public Entity OutputPortVisual;
}

public struct BuildingVisualPrefabEntry : IBufferElementData
{
    public BuildingLevelId BuildingLevel;
    public Entity Prefab;
}

public struct BuildingVisualReference : IComponentData
{
    public Entity Value;
}

public struct BeltVisualNeedsRefresh : IComponentData
{
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
    public BuildingLevelId BuildingLevel;
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
    public BuildingLevelId BuildingLevel;
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
        in DynamicBuffer<BuildingVisualPrefabEntry> prefabs,
        BuildingLevelId buildingLevel)
    {
        for (int i = 0; i < prefabs.Length; i++)
        {
            BuildingVisualPrefabEntry entry = prefabs[i];
            if (entry.BuildingLevel == buildingLevel)
                return entry.Prefab;
        }
        return Entity.Null;
    }
}
