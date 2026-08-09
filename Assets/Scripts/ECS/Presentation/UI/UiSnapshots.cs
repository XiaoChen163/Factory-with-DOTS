using System;
using Unity.Mathematics;

public enum UiDockRegion : byte
{
    Left,
    Right,
    Bottom,
    Overlay
}

public enum UiWindowId : byte
{
    Building,
    Backpack,
    BuildCatalog,
    RecipePicker
}

public enum UiSlotAccess : byte
{
    ReadOnly,
    Insert,
    Extract,
    InsertAndExtract
}

[Serializable]
public readonly struct BuildingRuntimeId : IEquatable<BuildingRuntimeId>
{
    public BuildingRuntimeId(ulong value) => Value = value;
    public ulong Value { get; }
    public bool IsValid => Value != 0;
    public bool Equals(BuildingRuntimeId other) => Value == other.Value;
    public override bool Equals(object obj) =>
        obj is BuildingRuntimeId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
}

public readonly struct ItemSlotSnapshot
{
    public ItemSlotSnapshot(
        ushort slotIndex,
        ItemId itemId,
        ushort count,
        ushort capacity,
        ItemId acceptedItemId,
        UiSlotAccess access)
    {
        SlotIndex = slotIndex;
        ItemId = itemId;
        Count = count;
        Capacity = capacity;
        AcceptedItemId = acceptedItemId;
        Access = access;
    }

    public ushort SlotIndex { get; }
    public ItemId ItemId { get; }
    public ushort Count { get; }
    public ushort Capacity { get; }
    public ItemId AcceptedItemId { get; }
    public UiSlotAccess Access { get; }
}

public sealed class InventorySnapshot
{
    public InventorySnapshot(
        PlayerId playerId,
        uint revision,
        ItemSlotSnapshot[] slots,
        bool isAvailable = true)
    {
        PlayerId = playerId;
        Revision = revision;
        Slots = slots ?? Array.Empty<ItemSlotSnapshot>();
        IsAvailable = isAvailable;
    }

    public PlayerId PlayerId { get; }
    public uint Revision { get; }
    public ItemSlotSnapshot[] Slots { get; }
    public bool IsAvailable { get; }
}

public sealed class ProcessorSnapshot
{
    public RecipeId SelectedRecipeId { get; set; }
    public ItemProcessStatus Status { get; set; }
    public float Progress01 { get; set; }
    public uint InventoryRevision { get; set; }
    public ItemSlotSnapshot[] Inputs { get; set; } =
        Array.Empty<ItemSlotSnapshot>();
    public ItemSlotSnapshot[] Outputs { get; set; } =
        Array.Empty<ItemSlotSnapshot>();
    public RecipeId[] AvailableRecipes { get; set; } =
        Array.Empty<RecipeId>();
}

public sealed class BuildingSnapshot
{
    public BuildingRuntimeId RuntimeId { get; set; }
    public BuildingLevelId BuildingLevelId { get; set; }
    public BuildingKind Kind { get; set; }
    public uint Revision { get; set; }
    public bool IsAvailable { get; set; }
    public int2 GridCell { get; set; }
    public ProcessorSnapshot Processor { get; set; }
    public ItemSlotSnapshot[] StorageSlots { get; set; } =
        Array.Empty<ItemSlotSnapshot>();
}

public readonly struct BuildCatalogSnapshot
{
    public BuildCatalogSnapshot(BuildingLevelId[] levels)
    {
        Levels = levels ?? Array.Empty<BuildingLevelId>();
    }

    public BuildingLevelId[] Levels { get; }
}
