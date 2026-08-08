using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

public static class ItemProcessUtility
{
    public static bool TrySelectRecipe(
        int recipeIndex,
        in FactoryRecipeRangeBlob range,
        ref FactoryDatabaseBlob database,
        DynamicBuffer<ProcessorItemSlot> slots,
        ref ItemProcessState process)
    {
        if (recipeIndex < 0 || recipeIndex >= range.Count ||
            process.Status != ItemProcessStatus.Idle || HasAnyItems(slots))
        {
            return false;
        }

        FactoryRecipeBlob recipe = database.Recipes[range.Start + recipeIndex];
        slots.Clear();
        for (int i = 0; i < recipe.InputCount; i++)
        {
            FactoryRecipeIngredientBlob ingredient =
                database.Inputs[recipe.InputStart + i];
            if (!TryAddSlot(
                    ref database,
                    ingredient,
                    (byte)i,
                    ProcessorSlotKind.Input,
                    slots))
            {
                slots.Clear();
                return false;
            }
        }
        for (int i = 0; i < recipe.OutputCount; i++)
        {
            FactoryRecipeIngredientBlob ingredient =
                database.Outputs[recipe.OutputStart + i];
            if (!TryAddSlot(
                    ref database,
                    ingredient,
                    (byte)i,
                    ProcessorSlotKind.Output,
                    slots))
            {
                slots.Clear();
                return false;
            }
        }

        process.SelectedRecipeIndex = recipeIndex;
        process.InventoryRevision++;
        return true;
    }

    public static bool TryStart(
        int recipeIndex,
        ushort workRatePermille,
        in FactoryRecipeBlob recipe,
        DynamicBuffer<ProcessorItemSlot> slots,
        ref ItemProcessState process)
    {
        if (process.Status != ItemProcessStatus.Idle ||
            recipe.DurationTicks <= 0 || recipe.OutputCount == 0)
        {
            return false;
        }

        for (int i = 0; i < slots.Length; i++)
        {
            ProcessorItemSlot slot = slots[i];
            int batchCount = slot.RequiredOrProducedCount;
            if (slot.Kind == ProcessorSlotKind.Input)
            {
                if (slot.Count < batchCount)
                    return false;
            }
            else if (slot.Count + batchCount > slot.Capacity)
            {
                return false;
            }
        }

        for (int i = 0; i < slots.Length; i++)
        {
            ProcessorItemSlot slot = slots[i];
            if (slot.Kind != ProcessorSlotKind.Input)
                continue;
            slot.Count = (ushort)(slot.Count - slot.RequiredOrProducedCount);
            slots[i] = slot;
        }

        process.ElapsedTicks = 0;
        int workRate = math.max(1, workRatePermille);
        long scaledDuration = (long)recipe.DurationTicks * 1000L;
        process.DurationTicks = math.max(
            1,
            (int)((scaledDuration + workRate - 1L) / workRate));
        process.ActiveRecipeIndex = recipeIndex;
        process.Status = ItemProcessStatus.Processing;
        process.InventoryRevision++;
        return true;
    }

    public static bool AdvanceOneTick(ref ItemProcessState process)
    {
        if (process.Status != ItemProcessStatus.Processing)
            return false;

        process.ElapsedTicks = math.min(
            process.ElapsedTicks + 1,
            process.DurationTicks);
        if (process.ElapsedTicks < process.DurationTicks)
            return false;

        process.Status = ItemProcessStatus.Completed;
        return true;
    }

    public static bool PublishCompletedOutputs(
        DynamicBuffer<ProcessorItemSlot> slots,
        ref ItemProcessState process)
    {
        if (process.Status != ItemProcessStatus.Completed)
            return false;

        for (int i = 0; i < slots.Length; i++)
        {
            ProcessorItemSlot slot = slots[i];
            if (slot.Kind != ProcessorSlotKind.Output)
                continue;
            slot.Count = (ushort)(slot.Count + slot.RequiredOrProducedCount);
            slots[i] = slot;
        }
        process.Status = ItemProcessStatus.OutputBlocked;
        process.InventoryRevision++;
        return true;
    }

    public static void AcknowledgeOutput(
        ItemId itemType,
        int transferredCount,
        DynamicBuffer<ProcessorItemSlot> slots,
        ref ItemProcessState process)
    {
        if (transferredCount <= 0 ||
            process.Status != ItemProcessStatus.OutputBlocked)
        {
            return;
        }

        int remaining = transferredCount;
        for (int i = 0; i < slots.Length && remaining > 0; i++)
        {
            ProcessorItemSlot slot = slots[i];
            if (slot.Kind != ProcessorSlotKind.Output ||
                slot.AcceptedItemType != itemType)
            {
                continue;
            }

            int removed = math.min(remaining, slot.Count);
            slot.Count = (ushort)(slot.Count - removed);
            slots[i] = slot;
            remaining -= removed;
        }

        if (remaining != transferredCount)
            process.InventoryRevision++;
        if (HasOutputItems(slots))
            return;

        process.ElapsedTicks = 0;
        process.DurationTicks = 0;
        process.ActiveRecipeIndex = -1;
        process.Status = ItemProcessStatus.Idle;
    }

    public static float GetNormalizedProgress(in ItemProcessState process)
    {
        if (process.Status == ItemProcessStatus.Idle ||
            process.DurationTicks <= 0)
        {
            return 0f;
        }
        return math.saturate(process.ElapsedTicks / (float)process.DurationTicks);
    }

    private static bool TryAddSlot(
        ref FactoryDatabaseBlob database,
        in FactoryRecipeIngredientBlob ingredient,
        byte recipeSlotIndex,
        ProcessorSlotKind kind,
        DynamicBuffer<ProcessorItemSlot> slots)
    {
        if (!FactoryDatabaseUtility.IsValidItem(ref database, ingredient.ItemId) ||
            ingredient.Count <= 0 || ingredient.Count > ushort.MaxValue)
        {
            return false;
        }

        slots.Add(new ProcessorItemSlot
        {
            AcceptedItemType = ingredient.ItemId,
            Capacity = database.ItemsById[ingredient.ItemId.Value].MaxStack,
            RequiredOrProducedCount = (ushort)ingredient.Count,
            RecipeSlotIndex = recipeSlotIndex,
            Kind = kind
        });
        return true;
    }

    private static bool HasAnyItems(in DynamicBuffer<ProcessorItemSlot> slots)
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].Count > 0)
                return true;
        return false;
    }

    private static bool HasOutputItems(in DynamicBuffer<ProcessorItemSlot> slots)
    {
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].Kind == ProcessorSlotKind.Output && slots[i].Count > 0)
                return true;
        return false;
    }
}

[BurstCompile]
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateBefore(typeof(BeltProgressSystem))]
public partial struct ItemProcessSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ItemProcessState>();
        state.RequireForUpdate<FactoryDatabase>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new ItemProcessJob
        {
            Database = SystemAPI.GetSingleton<FactoryDatabase>().Value
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct ItemProcessJob : IJobEntity
    {
        public BlobAssetReference<FactoryDatabaseBlob> Database;

        private void Execute(
            in ItemProcessor processor,
            DynamicBuffer<ProcessorItemSlot> slots,
            ref ItemProcessState process)
        {
            ref FactoryDatabaseBlob database = ref Database.Value;
            FactoryRecipeRangeBlob range = FactoryDatabaseUtility.GetRecipeRange(
                ref database,
                processor.MachineType);
            int selected = process.SelectedRecipeIndex;
            if (process.Status == ItemProcessStatus.Idle &&
                selected >= 0 && selected < range.Count)
            {
                int databaseRecipeIndex = range.Start + selected;
                ItemProcessUtility.TryStart(
                    databaseRecipeIndex,
                    processor.WorkRatePermille,
                    database.Recipes[databaseRecipeIndex],
                    slots,
                    ref process);
            }

            ItemProcessUtility.AdvanceOneTick(ref process);
            ItemProcessUtility.PublishCompletedOutputs(slots, ref process);
        }
    }
}
