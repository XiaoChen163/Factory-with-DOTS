using Unity.Entities;
using Unity.Mathematics;

public struct PlayerCommandHeader
{
    public PlayerId Player;
    public ulong RequestId;
    public ulong ClientSequence;
}

public struct RecipeSelectionCommand : IBufferElementData
{
    public PlayerCommandHeader Header;
    public int2 BuildingCell;
    public RecipeId Recipe;
}

public enum RecipeSelectionFailureReason : byte
{
    None,
    BuildingNotFound,
    RecipeNotFound,
    MachineTypeMismatch,
    RecipeBusy
}

public struct RecipeSelectionResult : IBufferElementData
{
    public PlayerCommandHeader Header;
    public int2 BuildingCell;
    public RecipeId Recipe;
    public byte Success;
    public RecipeSelectionFailureReason FailureReason;
}

public enum ItemOwnerKind : byte
{
    Player,
    Storage,
    Processor
}

public enum ItemSlotDomain : byte
{
    Inventory,
    ProcessorInput,
    ProcessorOutput
}

public struct ItemEndpoint
{
    public ItemOwnerKind OwnerKind;
    public ulong OwnerRuntimeId;
    public ItemSlotDomain Domain;
    public ushort SlotIndex;
}

public struct MoveItemPlayerCommand : IBufferElementData
{
    public PlayerCommandHeader Header;
    public ItemEndpoint Source;
    public ItemEndpoint Destination;
    public ItemId ExpectedItemType;
    public ushort Amount;
}

public enum MoveItemFailureReason : byte
{
    None,
    OwnerNotFound,
    InvalidSlot,
    EmptySource,
    StaleSnapshot,
    DestinationRejected,
    CapacityExceeded
}

public struct MoveItemPlayerResult : IBufferElementData
{
    public PlayerCommandHeader Header;
    public byte Success;
    public ushort MovedAmount;
    public MoveItemFailureReason FailureReason;
}
