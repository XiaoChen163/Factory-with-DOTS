using Unity.Entities;

public enum ItemPortFilterMode : byte
{
    ExactItemType,
    Any
}

public struct ItemInputPortSnapshot
{
    public ItemId AcceptedItemType;
    public int FreeCapacity;
    public ulong AppliedTransferCount;
    public ulong ReservedTransferCount;
    public byte PortIndex;
    public byte Enabled;
    public ItemPortFilterMode FilterMode;
}

public struct ItemOutputPortSnapshot
{
    public ItemId ItemType;
    public int AvailableCount;
    public ulong AppliedTransferCount;
    public ulong ReservedTransferCount;
    public byte PortIndex;
    public byte Enabled;
}

public struct ItemPortBufferGeneration : IComponentData
{
    public byte Value;
}

[InternalBufferCapacity(2)]
public struct ItemInputPortCurrent : IBufferElementData
{
    public ItemInputPortSnapshot Value;
}

[InternalBufferCapacity(2)]
public struct ItemInputPortNext : IBufferElementData
{
    public ItemInputPortSnapshot Value;
}

[InternalBufferCapacity(2)]
public struct ItemOutputPortCurrent : IBufferElementData
{
    public ItemOutputPortSnapshot Value;
}

[InternalBufferCapacity(2)]
public struct ItemOutputPortNext : IBufferElementData
{
    public ItemOutputPortSnapshot Value;
}

public enum ItemTransferReceiptKind : byte
{
    InputAccepted,
    OutputTransferred
}

public struct ItemTransferReceipt
{
    public ItemId ItemType;
    public int Count;
    public byte PortIndex;
    public ItemTransferReceiptKind Kind;
}

[InternalBufferCapacity(4)]
public struct ItemTransferReceiptCurrent : IBufferElementData
{
    public ItemTransferReceipt Value;
}

[InternalBufferCapacity(4)]
public struct ItemTransferReceiptNext : IBufferElementData
{
    public ItemTransferReceipt Value;
}

public struct ItemPrefabEntry : IBufferElementData
{
    public ItemId ItemType;
    public Entity Prefab;
}
