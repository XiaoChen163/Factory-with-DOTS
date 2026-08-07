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
        state.Dependency = new ProcessorPortAdapterJob
        {
            Database = SystemAPI.GetSingleton<FactoryDatabase>().Value
        }
            .ScheduleParallel(state.Dependency);
        state.Dependency = new StoragePortAdapterJob()
            .ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct ProcessorPortAdapterJob : IJobEntity
    {
        [ReadOnly] public BlobAssetReference<FactoryDatabaseBlob> Database;

        private void Execute(
            ref ItemProcessState process,
            in ItemProcessor processor,
            DynamicBuffer<ItemProcessInput> inputs,
            in DynamicBuffer<BuildingPort> buildingPorts,
            in ItemPortBufferGeneration generation,
            in DynamicBuffer<ItemInputPortCurrent> inputCurrent,
            DynamicBuffer<ItemInputPortNext> inputNext,
            in DynamicBuffer<ItemOutputPortCurrent> outputCurrent,
            DynamicBuffer<ItemOutputPortNext> outputNext,
            DynamicBuffer<ItemTransferReceiptNext> receipts)
        {
            ref FactoryDatabaseBlob database = ref Database.Value;
            ApplyProcessorReceipts(receipts, inputs, ref process);

            DynamicBuffer<ItemInputPortSnapshot> currentInputs;
            DynamicBuffer<ItemInputPortSnapshot> nextInputs;
            DynamicBuffer<ItemOutputPortSnapshot> currentOutputs;
            DynamicBuffer<ItemOutputPortSnapshot> nextOutputs;
            if (generation.Value == 0)
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

            nextInputs.Clear();
            nextOutputs.Clear();
            for (int i = 0; i < buildingPorts.Length; i++)
            {
                BuildingPort port = buildingPorts[i];
                if (port.Type == BuildingPortType.Input)
                {
                    PublishProcessorInput(
                        port.Index,
                        ref database,
                        processor.MachineType,
                        inputs,
                        process,
                        currentInputs,
                        receipts,
                        nextInputs);
                }
                else
                {
                    PublishProcessorOutput(
                        port.Index,
                        ref database,
                        process,
                        currentOutputs,
                        receipts,
                        nextOutputs);
                }
            }

            receipts.Clear();
        }
    }

    [BurstCompile]
    private partial struct StoragePortAdapterJob : IJobEntity
    {
        private void Execute(
            ref StorageState storage,
            DynamicBuffer<StoredItemCount> storedItems,
            in DynamicBuffer<BuildingPort> buildingPorts,
            in ItemPortBufferGeneration generation,
            in DynamicBuffer<ItemInputPortCurrent> inputCurrent,
            DynamicBuffer<ItemInputPortNext> inputNext,
            in DynamicBuffer<ItemOutputPortCurrent> outputCurrent,
            DynamicBuffer<ItemOutputPortNext> outputNext,
            DynamicBuffer<ItemTransferReceiptNext> receipts)
        {
            for (int i = 0; i < receipts.Length; i++)
            {
                ItemTransferReceipt receipt = receipts[i].Value;
                if (receipt.Kind != ItemTransferReceiptKind.InputAccepted ||
                    receipt.Count <= 0)
                {
                    continue;
                }

                AddStoredItem(storedItems, receipt.ItemType, receipt.Count);
                storage.TotalStored += receipt.Count;
            }

            DynamicBuffer<ItemInputPortSnapshot> currentInputs;
            DynamicBuffer<ItemInputPortSnapshot> nextInputs;
            DynamicBuffer<ItemOutputPortSnapshot> currentOutputs;
            DynamicBuffer<ItemOutputPortSnapshot> nextOutputs;
            if (generation.Value == 0)
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

            nextInputs.Clear();
            nextOutputs.Clear();
            for (int i = 0; i < buildingPorts.Length; i++)
            {
                BuildingPort port = buildingPorts[i];
                if (port.Type != BuildingPortType.Input)
                {
                    continue;
                }

                ulong applied = GetInputAppliedCount(
                    currentInputs,
                    port.Index) +
                    CountReceipts(
                        receipts,
                        port.Index,
                        ItemTransferReceiptKind.InputAccepted);
                nextInputs.Add(new ItemInputPortSnapshot
                {
                    AcceptedItemType = ItemId.Invalid,
                    FreeCapacity = math.max(
                        0,
                        storage.Capacity - storage.TotalStored),
                    AppliedTransferCount = applied,
                    ReservedTransferCount = math.max(
                        applied,
                        GetInputReservedCount(
                            currentInputs,
                            port.Index)),
                    PortIndex = port.Index,
                    Enabled = 1,
                    FilterMode = ItemPortFilterMode.Any
                });
            }

            receipts.Clear();
        }
    }

    private static void ApplyProcessorReceipts(
        in DynamicBuffer<ItemTransferReceiptNext> receipts,
        DynamicBuffer<ItemProcessInput> inputs,
        ref ItemProcessState process)
    {
        for (int i = 0; i < receipts.Length; i++)
        {
            ItemTransferReceipt receipt = receipts[i].Value;
            if (receipt.Count <= 0)
            {
                continue;
            }

            if (receipt.Kind == ItemTransferReceiptKind.InputAccepted)
            {
                AddProcessInput(inputs, receipt.ItemType, receipt.Count);
            }
            else
            {
                ItemProcessUtility.AcknowledgeOutput(
                    receipt.Count,
                    ref process);
            }
        }
    }

    private static void PublishProcessorInput(
        byte portIndex,
        ref FactoryDatabaseBlob database,
        MachineTypeId machineType,
        in DynamicBuffer<ItemProcessInput> inputs,
        in ItemProcessState process,
        in DynamicBuffer<ItemInputPortSnapshot> current,
        in DynamicBuffer<ItemTransferReceiptNext> receipts,
        DynamicBuffer<ItemInputPortSnapshot> next)
    {
        ItemId acceptedItemType = ItemId.Invalid;
        int slotCapacity = 0;
        FactoryRecipeRangeBlob range =
            FactoryDatabaseUtility.GetRecipeRange(
                ref database,
                machineType);
        int selected = process.SelectedRecipeIndex;
        if (selected >= 0 && selected < range.Count)
        {
            FactoryRecipeBlob recipe =
                database.Recipes[range.Start + selected];
            if (recipe.InputCount > 0)
            {
                FactoryRecipeIngredientBlob ingredient =
                    database.Inputs[recipe.InputStart];
                acceptedItemType = ingredient.ItemId;
                if (FactoryDatabaseUtility.IsValidItem(
                        ref database,
                        acceptedItemType))
                {
                    slotCapacity = math.max(
                        1,
                        database.ItemsById[acceptedItemType.Value].MaxStack);
                }
            }
        }

        int bufferedCount = 0;
        for (int i = 0; i < inputs.Length; i++)
        {
            if (inputs[i].ItemType == acceptedItemType)
                bufferedCount += math.max(0, inputs[i].Count);
        }

        ulong applied = GetInputAppliedCount(current, portIndex) +
            CountReceipts(
                receipts,
                portIndex,
                ItemTransferReceiptKind.InputAccepted);
        next.Add(new ItemInputPortSnapshot
        {
            AcceptedItemType = acceptedItemType,
            FreeCapacity = math.max(0, slotCapacity - bufferedCount),
            AppliedTransferCount = applied,
            ReservedTransferCount = math.max(
                applied,
                GetInputReservedCount(current, portIndex)),
            PortIndex = portIndex,
            Enabled = !acceptedItemType.IsValid
                ? (byte)0
                : (byte)1,
            FilterMode = ItemPortFilterMode.ExactItemType
        });
    }

    private static void PublishProcessorOutput(
        byte portIndex,
        ref FactoryDatabaseBlob database,
        in ItemProcessState process,
        in DynamicBuffer<ItemOutputPortSnapshot> current,
        in DynamicBuffer<ItemTransferReceiptNext> receipts,
        DynamicBuffer<ItemOutputPortSnapshot> next)
    {
        ItemId itemType = ItemId.Invalid;
        int active = process.ActiveRecipeIndex;
        if (active >= 0 && active < database.Recipes.Length)
        {
            FactoryRecipeBlob recipe = database.Recipes[active];
            if (recipe.OutputCount > 0)
            {
                itemType = database.Outputs[recipe.OutputStart].ItemId;
            }
        }

        ulong applied = GetOutputAppliedCount(current, portIndex) +
            CountReceipts(
                receipts,
                portIndex,
                ItemTransferReceiptKind.OutputTransferred);
        next.Add(new ItemOutputPortSnapshot
        {
            ItemType = itemType,
            AvailableCount = math.max(0, process.PendingOutputCount),
            AppliedTransferCount = applied,
            ReservedTransferCount = math.max(
                applied,
                GetOutputReservedCount(current, portIndex)),
            PortIndex = portIndex,
            Enabled = process.Status == ItemProcessStatus.OutputBlocked &&
                      itemType.IsValid &&
                      process.PendingOutputCount > 0
                ? (byte)1
                : (byte)0
        });
    }

    private static ulong CountReceipts(
        in DynamicBuffer<ItemTransferReceiptNext> receipts,
        byte portIndex,
        ItemTransferReceiptKind kind)
    {
        ulong count = 0;
        for (int i = 0; i < receipts.Length; i++)
        {
            ItemTransferReceipt receipt = receipts[i].Value;
            if (receipt.PortIndex == portIndex && receipt.Kind == kind)
            {
                count += (ulong)math.max(0, receipt.Count);
            }
        }

        return count;
    }

    private static ulong GetInputAppliedCount(
        in DynamicBuffer<ItemInputPortSnapshot> ports,
        byte portIndex)
    {
        for (int i = 0; i < ports.Length; i++)
        {
            if (ports[i].PortIndex == portIndex)
            {
                return ports[i].AppliedTransferCount;
            }
        }

        return 0;
    }

    private static ulong GetOutputAppliedCount(
        in DynamicBuffer<ItemOutputPortSnapshot> ports,
        byte portIndex)
    {
        for (int i = 0; i < ports.Length; i++)
        {
            if (ports[i].PortIndex == portIndex)
            {
                return ports[i].AppliedTransferCount;
            }
        }

        return 0;
    }

    private static ulong GetInputReservedCount(
        in DynamicBuffer<ItemInputPortSnapshot> ports,
        byte portIndex)
    {
        for (int i = 0; i < ports.Length; i++)
        {
            if (ports[i].PortIndex == portIndex)
            {
                return ports[i].ReservedTransferCount;
            }
        }

        return 0;
    }

    private static ulong GetOutputReservedCount(
        in DynamicBuffer<ItemOutputPortSnapshot> ports,
        byte portIndex)
    {
        for (int i = 0; i < ports.Length; i++)
        {
            if (ports[i].PortIndex == portIndex)
            {
                return ports[i].ReservedTransferCount;
            }
        }

        return 0;
    }

    private static void AddProcessInput(
        DynamicBuffer<ItemProcessInput> inputs,
        ItemId itemType,
        int count)
    {
        for (int i = 0; i < inputs.Length; i++)
        {
            ItemProcessInput input = inputs[i];
            if (input.ItemType != itemType)
            {
                continue;
            }

            input.Count += count;
            inputs[i] = input;
            return;
        }

        inputs.Add(new ItemProcessInput
        {
            ItemType = itemType,
            Count = count
        });
    }

    private static void AddStoredItem(
        DynamicBuffer<StoredItemCount> items,
        ItemId itemType,
        int count)
    {
        for (int i = 0; i < items.Length; i++)
        {
            StoredItemCount item = items[i];
            if (item.ItemType != itemType)
            {
                continue;
            }

            item.Count += count;
            items[i] = item;
            return;
        }

        items.Add(new StoredItemCount
        {
            ItemType = itemType,
            Count = count
        });
    }
}
