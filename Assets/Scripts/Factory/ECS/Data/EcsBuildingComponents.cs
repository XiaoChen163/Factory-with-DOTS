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

public struct MinerState : IComponentData
{
    public Entity OutputItemType;
    public Entity PendingOutput;
    public float ProductionInterval;
    public float ProductionElapsed;
}

public struct FurnaceState : IComponentData
{
    public Entity InputItemType;
    public Entity OutputItemType;
    public Entity PendingOutput;
    public int RequiredInputCount;
    public int OutputCount;
    public int BufferedInputs;
    public int PendingOutputCount;
    public float CraftTime;
    public float CraftProgress;
    public byte IsCrafting;
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
