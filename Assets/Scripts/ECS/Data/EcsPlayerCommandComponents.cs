using Unity.Entities;
using Unity.Mathematics;

public struct PlayerCommandHeader
{
    public PlayerId Player;
    public ulong RequestId;
    public ulong ClientSequence;
}

public struct PlayerCommandSequenceState : IComponentData
{
    public ulong LastAcceptedSequence;
}

public enum PlayerCommandKind : byte
{
    SelectRecipe,
    MoveItem,
    GridBuild
}

public enum PlayerCommandFailureReason : byte
{
    None,
    PlayerNotFound,
    SequenceDuplicate,
    BuildingNotFound,
    RecipeNotFound,
    MachineTypeMismatch,
    RecipeBusy,
    OwnerNotFound,
    InvalidSlot,
    EmptySource,
    StaleSnapshot,
    DestinationRejected,
    CapacityExceeded,
    GridRejected
}

public struct RecipeSelectionCommand : IBufferElementData
{
    public PlayerCommandHeader Header;
    public GridCell BuildingCell;
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
    public GridCell BuildingCell;
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
    PlayerNotFound,
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

public struct GridBuildPlayerCommand : IBufferElementData
{
    public PlayerCommandHeader Header;
    public GridBuildCommandType Type;
    public BuildingKind Kind;
    public BuildingLevelId BuildingLevel;
    public GridCell StartCell;
    public GridCell EndCell;
    public byte QuarterTurns;
    public byte HorizontalFirst;
}

public struct GridBuildPlayerResult : IBufferElementData
{
    public PlayerCommandHeader Header;
    public byte Success;
    public int AffectedCount;
    public GridBuildFailureReason FailureReason;
}

public struct PlayerGridCommandPending : IBufferElementData
{
    public uint GridRequestId;
    public PlayerCommandHeader Header;
}

public struct PlayerGridCommandAdapterState : IComponentData
{
    public uint NextGridRequestId;
}
