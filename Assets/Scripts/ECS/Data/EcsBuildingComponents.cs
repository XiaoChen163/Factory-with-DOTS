using Unity.Entities;
using Unity.Mathematics;
using Unity.Collections;

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
    RemoveBeltLine,
    PlaceFoundation,
    RemoveFoundation,
    PlaceFoundationArea
}

public struct GridBuildCommand : IBufferElementData
{
    public uint RequestId;
    public PlayerId Player;
    public GridBuildCommandType Type;
    public BuildingKind Kind;
    public BuildingLevelId BuildingLevel;
    public GridCell StartCell;
    public GridCell EndCell;
    public byte QuarterTurns;
    public byte HorizontalFirst;
    public ushort VisualMaterialId;
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
    TargetIsNotBelt,
    SurfaceMissing,
    FoundationAlreadyExists,
    FoundationUnsupported,
    FoundationSupportsBuilding,
    InvalidFoundationLevel,
    FoundationAreaTooLarge
}

public struct GridBuildResult : IBufferElementData
{
    public uint RequestId;
    public GridBuildCommandType Type;
    public BuildingKind Kind;
    public BuildingLevelId BuildingLevel;
    public GridCell Cell;
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

public enum ProcessorSlotKind : byte
{
    Input,
    Output
}

[InternalBufferCapacity(8)]
public struct ProcessorItemSlot : IBufferElementData
{
    public ItemId AcceptedItemType;
    public ushort Count;
    public ushort Capacity;
    public ushort RequiredOrProducedCount;
    public byte RecipeSlotIndex;
    public ProcessorSlotKind Kind;
}

public struct ItemProcessState : IComponentData
{
    public int ElapsedTicks;
    public int DurationTicks;
    public int SelectedRecipeIndex;
    public int ActiveRecipeIndex;
    public uint InventoryRevision;
    public ItemProcessStatus Status;
}

public struct StorageState : IComponentData
{
    public int TotalStored;
    public int Capacity;
    public ushort SlotCount;
    public uint Revision;
}

[InternalBufferCapacity(24)]
public struct InventorySlot : IBufferElementData
{
    public ItemId ItemType;
    public ushort Count;
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

    public static Entity GetPrefab(
        in NativeArray<BuildingVisualPrefabEntry> prefabs,
        BuildingLevelId buildingLevel)
    {
        for (int i = 0; i < prefabs.Length; i++)
            if (prefabs[i].BuildingLevel == buildingLevel)
                return prefabs[i].Prefab;
        return Entity.Null;
    }
}

public struct BuildingRuntimeIdAllocator : IComponentData
{
    public ulong NextValue;

    public ulong Allocate()
    {
        if (NextValue < 0x8000000000000000UL)
        {
            NextValue = 0x8000000000000000UL;
        }

        if (NextValue == ulong.MaxValue)
        {
            throw new System.InvalidOperationException(
                "Building runtime ID space is exhausted.");
        }

        return NextValue++;
    }
}
