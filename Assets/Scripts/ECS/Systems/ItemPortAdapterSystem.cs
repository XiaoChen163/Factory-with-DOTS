using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(ItemProcessSystem))]
[UpdateBefore(typeof(BeltProgressSystem))]
public partial struct ItemPortAdapterSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ItemTransferReceiptNext>();
        state.RequireForUpdate<FactoryDatabase>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BlobAssetReference<FactoryDatabaseBlob> database =
            SystemAPI.GetSingleton<FactoryDatabase>().Value;
        state.Dependency = new ProcessorPortAdapterJob
        {
            Database = database
        }.ScheduleParallel(state.Dependency);
        state.Dependency = new StoragePortAdapterJob
        {
            Database = database
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct ProcessorPortAdapterJob : IJobEntity
    {
        [ReadOnly] public BlobAssetReference<FactoryDatabaseBlob> Database;

        private void Execute(
            ref ItemProcessState process,
            DynamicBuffer<ProcessorItemSlot> slots,
            in DynamicBuffer<BuildingPort> buildingPorts,
            in ItemPortBufferGeneration generation,
            DynamicBuffer<ItemInputPortCurrent> inputCurrent,
            DynamicBuffer<ItemInputPortNext> inputNext,
            DynamicBuffer<ItemOutputPortCurrent> outputCurrent,
            DynamicBuffer<ItemOutputPortNext> outputNext,
            DynamicBuffer<ItemTransferReceiptNext> receipts)
        {
            GetBuffers(
                generation.Value,
                inputCurrent,
                inputNext,
                outputCurrent,
                outputNext,
                out DynamicBuffer<ItemInputPortSnapshot> currentInputs,
                out DynamicBuffer<ItemInputPortSnapshot> nextInputs,
                out DynamicBuffer<ItemOutputPortSnapshot> currentOutputs,
                out DynamicBuffer<ItemOutputPortSnapshot> nextOutputs);

            nextInputs.Clear();
            nextOutputs.Clear();
            for (int portIndex = 0; portIndex < buildingPorts.Length; portIndex++)
            {
                BuildingPort port = buildingPorts[portIndex];
                for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                {
                    ProcessorItemSlot slot = slots[slotIndex];
                    if (port.Type == BuildingPortType.Input &&
                        slot.Kind == ProcessorSlotKind.Input)
                    {
                        PublishProcessorInput(
                            port.Index,
                            slot,
                            currentInputs,
                            receipts,
                            nextInputs);
                    }
                    else if (port.Type == BuildingPortType.Output &&
                             slot.Kind == ProcessorSlotKind.Output)
                    {
                        PublishProcessorOutput(
                            port.Index,
                            slot,
                            currentOutputs,
                            receipts,
                            nextOutputs);
                    }
                }
            }
            receipts.Clear();
        }
    }

    [BurstCompile]
    private partial struct StoragePortAdapterJob : IJobEntity
    {
        [ReadOnly] public BlobAssetReference<FactoryDatabaseBlob> Database;

        private void Execute(
            ref StorageState storage,
            DynamicBuffer<InventorySlot> slots,
            in DynamicBuffer<BuildingPort> buildingPorts,
            in ItemPortBufferGeneration generation,
            DynamicBuffer<ItemInputPortCurrent> inputCurrent,
            DynamicBuffer<ItemInputPortNext> inputNext,
            DynamicBuffer<ItemOutputPortCurrent> outputCurrent,
            DynamicBuffer<ItemOutputPortNext> outputNext,
            DynamicBuffer<ItemTransferReceiptNext> receipts)
        {
            ref FactoryDatabaseBlob database = ref Database.Value;

            GetBuffers(
                generation.Value,
                inputCurrent,
                inputNext,
                outputCurrent,
                outputNext,
                out DynamicBuffer<ItemInputPortSnapshot> currentInputs,
                out DynamicBuffer<ItemInputPortSnapshot> nextInputs,
                out DynamicBuffer<ItemOutputPortSnapshot> currentOutputs,
                out DynamicBuffer<ItemOutputPortSnapshot> nextOutputs);
            nextInputs.Clear();
            nextOutputs.Clear();

            int totalFree = math.max(0, storage.Capacity - storage.TotalStored);
            for (int p = 0; p < buildingPorts.Length; p++)
            {
                BuildingPort port = buildingPorts[p];
                if (port.Type != BuildingPortType.Input)
                    continue;

                for (int itemIndex = 1;
                     itemIndex < database.ItemsById.Length;
                     itemIndex++)
                {
                    FactoryItemBlob item = database.ItemsById[itemIndex];
                    if (!item.Id.IsValid)
                        continue;
                    int free = math.min(
                        totalFree,
                        GetInventoryFreeCapacity(slots, item.Id, item.MaxStack));
                    ulong applied = GetInputAppliedCount(
                        currentInputs,
                        port.Index,
                        item.Id) + CountReceipts(
                            receipts,
                            port.Index,
                            item.Id,
                            ItemTransferReceiptKind.InputAccepted);
                    nextInputs.Add(new ItemInputPortSnapshot
                    {
                        AcceptedItemType = item.Id,
                        FreeCapacity = free,
                        AppliedTransferCount = applied,
                        ReservedTransferCount = math.max(
                            applied,
                            GetInputReservedCount(
                                currentInputs,
                                port.Index,
                                item.Id)),
                        PortIndex = port.Index,
                        Enabled = free > 0 ? (byte)1 : (byte)0,
                        FilterMode = ItemPortFilterMode.ExactItemType
                    });
                }
            }
            receipts.Clear();
        }
    }

    private static void GetBuffers(
        byte generation,
        DynamicBuffer<ItemInputPortCurrent> inputCurrent,
        DynamicBuffer<ItemInputPortNext> inputNext,
        DynamicBuffer<ItemOutputPortCurrent> outputCurrent,
        DynamicBuffer<ItemOutputPortNext> outputNext,
        out DynamicBuffer<ItemInputPortSnapshot> currentInputs,
        out DynamicBuffer<ItemInputPortSnapshot> nextInputs,
        out DynamicBuffer<ItemOutputPortSnapshot> currentOutputs,
        out DynamicBuffer<ItemOutputPortSnapshot> nextOutputs)
    {
        if (generation == 0)
        {
            currentInputs = inputCurrent.Reinterpret<ItemInputPortSnapshot>();
            nextInputs = inputNext.Reinterpret<ItemInputPortSnapshot>();
            currentOutputs = outputCurrent.Reinterpret<ItemOutputPortSnapshot>();
            nextOutputs = outputNext.Reinterpret<ItemOutputPortSnapshot>();
        }
        else
        {
            currentInputs = inputNext.Reinterpret<ItemInputPortSnapshot>();
            nextInputs = inputCurrent.Reinterpret<ItemInputPortSnapshot>();
            currentOutputs = outputNext.Reinterpret<ItemOutputPortSnapshot>();
            nextOutputs = outputCurrent.Reinterpret<ItemOutputPortSnapshot>();
        }
    }

    private static void PublishProcessorInput(
        byte portIndex,
        in ProcessorItemSlot slot,
        in DynamicBuffer<ItemInputPortSnapshot> current,
        in DynamicBuffer<ItemTransferReceiptNext> receipts,
        DynamicBuffer<ItemInputPortSnapshot> next)
    {
        ulong applied = GetInputAppliedCount(
            current,
            portIndex,
            slot.AcceptedItemType) + CountReceipts(
                receipts,
                portIndex,
                slot.AcceptedItemType,
                ItemTransferReceiptKind.InputAccepted);
        int free = math.max(0, slot.Capacity - slot.Count);
        next.Add(new ItemInputPortSnapshot
        {
            AcceptedItemType = slot.AcceptedItemType,
            FreeCapacity = free,
            AppliedTransferCount = applied,
            ReservedTransferCount = math.max(
                applied,
                GetInputReservedCount(current, portIndex, slot.AcceptedItemType)),
            PortIndex = portIndex,
            Enabled = free > 0 ? (byte)1 : (byte)0,
            FilterMode = ItemPortFilterMode.ExactItemType
        });
    }

    private static void PublishProcessorOutput(
        byte portIndex,
        in ProcessorItemSlot slot,
        in DynamicBuffer<ItemOutputPortSnapshot> current,
        in DynamicBuffer<ItemTransferReceiptNext> receipts,
        DynamicBuffer<ItemOutputPortSnapshot> next)
    {
        ulong applied = GetOutputAppliedCount(
            current,
            portIndex,
            slot.AcceptedItemType) + CountReceipts(
                receipts,
                portIndex,
                slot.AcceptedItemType,
                ItemTransferReceiptKind.OutputTransferred);
        next.Add(new ItemOutputPortSnapshot
        {
            ItemType = slot.AcceptedItemType,
            AvailableCount = slot.Count,
            AppliedTransferCount = applied,
            ReservedTransferCount = math.max(
                applied,
                GetOutputReservedCount(current, portIndex, slot.AcceptedItemType)),
            PortIndex = portIndex,
            Enabled = slot.Count > 0 ? (byte)1 : (byte)0
        });
    }

    private static ulong CountReceipts(
        in DynamicBuffer<ItemTransferReceiptNext> receipts,
        byte portIndex,
        ItemId itemType,
        ItemTransferReceiptKind kind)
    {
        ulong count = 0;
        for (int i = 0; i < receipts.Length; i++)
        {
            ItemTransferReceipt receipt = receipts[i].Value;
            if (receipt.PortIndex == portIndex && receipt.Kind == kind &&
                receipt.ItemType == itemType)
            {
                count += (ulong)math.max(0, receipt.Count);
            }
        }
        return count;
    }

    private static ulong GetInputAppliedCount(
        in DynamicBuffer<ItemInputPortSnapshot> ports,
        byte portIndex,
        ItemId itemType)
    {
        for (int i = 0; i < ports.Length; i++)
            if (ports[i].PortIndex == portIndex &&
                ports[i].AcceptedItemType == itemType)
                return ports[i].AppliedTransferCount;
        return 0;
    }

    private static ulong GetInputReservedCount(
        in DynamicBuffer<ItemInputPortSnapshot> ports,
        byte portIndex,
        ItemId itemType)
    {
        for (int i = 0; i < ports.Length; i++)
            if (ports[i].PortIndex == portIndex &&
                ports[i].AcceptedItemType == itemType)
                return ports[i].ReservedTransferCount;
        return 0;
    }

    private static ulong GetOutputAppliedCount(
        in DynamicBuffer<ItemOutputPortSnapshot> ports,
        byte portIndex,
        ItemId itemType)
    {
        for (int i = 0; i < ports.Length; i++)
            if (ports[i].PortIndex == portIndex && ports[i].ItemType == itemType)
                return ports[i].AppliedTransferCount;
        return 0;
    }

    private static ulong GetOutputReservedCount(
        in DynamicBuffer<ItemOutputPortSnapshot> ports,
        byte portIndex,
        ItemId itemType)
    {
        for (int i = 0; i < ports.Length; i++)
            if (ports[i].PortIndex == portIndex && ports[i].ItemType == itemType)
                return ports[i].ReservedTransferCount;
        return 0;
    }

    private static int GetInventoryFreeCapacity(
        in DynamicBuffer<InventorySlot> slots,
        ItemId itemType,
        int maxStack)
    {
        int free = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            InventorySlot slot = slots[i];
            if (!slot.ItemType.IsValid)
                free += maxStack;
            else if (slot.ItemType == itemType)
                free += math.max(0, maxStack - slot.Count);
        }
        return free;
    }

    public static bool AddInventoryItem(
        ref FactoryDatabaseBlob database,
        DynamicBuffer<InventorySlot> slots,
        ItemId itemType,
        int count)
    {
        if (!FactoryDatabaseUtility.IsValidItem(ref database, itemType))
            return false;
        int maxStack = database.ItemsById[itemType.Value].MaxStack;
        if (GetInventoryFreeCapacity(slots, itemType, maxStack) < count)
            return false;

        int remaining = count;
        for (int i = 0; i < slots.Length && remaining > 0; i++)
        {
            InventorySlot slot = slots[i];
            if (slot.ItemType != itemType || slot.Count >= maxStack)
                continue;
            int added = math.min(remaining, maxStack - slot.Count);
            slot.Count = (ushort)(slot.Count + added);
            slots[i] = slot;
            remaining -= added;
        }
        for (int i = 0; i < slots.Length && remaining > 0; i++)
        {
            InventorySlot slot = slots[i];
            if (slot.ItemType.IsValid)
                continue;
            int added = math.min(remaining, maxStack);
            slots[i] = new InventorySlot
            {
                ItemType = itemType,
                Count = (ushort)added
            };
            remaining -= added;
        }
        return remaining == 0;
    }
}
