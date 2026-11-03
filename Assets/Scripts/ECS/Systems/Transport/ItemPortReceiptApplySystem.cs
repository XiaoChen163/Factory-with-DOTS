using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[BurstCompile]
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateBefore(typeof(RecipeSelectionCommandSystem))]
public partial struct ItemPortReceiptApplySystem : ISystem
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
        state.Dependency = new ProcessorReceiptJob().ScheduleParallel(
            state.Dependency);
        state.Dependency = new StorageReceiptJob
        {
            Database = database
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct ProcessorReceiptJob : IJobEntity
    {
        private void Execute(
            ref ItemProcessState process,
            DynamicBuffer<ProcessorItemSlot> slots,
            in DynamicBuffer<ItemTransferReceiptNext> receipts)
        {
            for (int i = 0; i < receipts.Length; i++)
            {
                ItemTransferReceipt receipt = receipts[i].Value;
                if (receipt.Count <= 0)
                    continue;
                if (receipt.Kind == ItemTransferReceiptKind.OutputTransferred)
                {
                    ItemProcessUtility.AcknowledgeOutput(
                        receipt.ItemType,
                        receipt.Count,
                        slots,
                        ref process);
                    continue;
                }

                for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
                {
                    ProcessorItemSlot slot = slots[slotIndex];
                    if (slot.Kind != ProcessorSlotKind.Input ||
                        slot.AcceptedItemType != receipt.ItemType)
                    {
                        continue;
                    }
                    slot.Count = (ushort)math.min(
                        slot.Capacity,
                        slot.Count + receipt.Count);
                    slots[slotIndex] = slot;
                    process.InventoryRevision++;
                    break;
                }
            }
        }
    }

    [BurstCompile]
    private partial struct StorageReceiptJob : IJobEntity
    {
        [ReadOnly] public BlobAssetReference<FactoryDatabaseBlob> Database;

        private void Execute(
            ref StorageState storage,
            DynamicBuffer<InventorySlot> slots,
            in DynamicBuffer<ItemTransferReceiptNext> receipts)
        {
            ref FactoryDatabaseBlob database = ref Database.Value;
            for (int i = 0; i < receipts.Length; i++)
            {
                ItemTransferReceipt receipt = receipts[i].Value;
                if (receipt.Kind != ItemTransferReceiptKind.InputAccepted ||
                    receipt.Count <= 0)
                {
                    continue;
                }
                if (ItemPortAdapterSystem.AddInventoryItem(
                        ref database,
                        slots,
                        receipt.ItemType,
                        receipt.Count))
                {
                    storage.TotalStored += receipt.Count;
                    storage.Revision++;
                }
            }
        }
    }
}
