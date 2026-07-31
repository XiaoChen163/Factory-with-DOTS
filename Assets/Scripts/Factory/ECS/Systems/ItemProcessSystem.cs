using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

public static class ItemProcessUtility
{
    public static bool TryStart(
        int recipeIndex,
        in ItemProcessRecipe recipe,
        DynamicBuffer<ItemProcessInput> inputs,
        ref ItemProcessState process)
    {
        if (process.Status != ItemProcessStatus.Idle ||
            process.PendingOutput != Entity.Null ||
            process.PendingOutputCount > 0 ||
            recipe.OutputItemType == Entity.Null ||
            recipe.OutputCount <= 0 ||
            recipe.DurationTicks <= 0)
        {
            return false;
        }

        int requiredInputs = math.max(0, recipe.RequiredInputCount);
        int inputIndex = -1;
        if (requiredInputs > 0)
        {
            if (recipe.InputItemType == Entity.Null)
            {
                return false;
            }

            for (int i = 0; i < inputs.Length; i++)
            {
                ItemProcessInput input = inputs[i];
                if (input.ItemType == recipe.InputItemType &&
                    input.Count >= requiredInputs)
                {
                    inputIndex = i;
                    break;
                }
            }

            if (inputIndex < 0)
            {
                return false;
            }
        }

        if (inputIndex >= 0)
        {
            ItemProcessInput input = inputs[inputIndex];
            input.Count -= requiredInputs;
            inputs[inputIndex] = input;
        }

        process.ElapsedTicks = 0;
        process.DurationTicks = math.max(1, recipe.DurationTicks);
        process.ActiveRecipeIndex = recipeIndex;
        process.Status = ItemProcessStatus.Processing;
        return true;
    }

    public static bool TrySelectRecipe(
        int recipeIndex,
        int recipeCount,
        ref ItemProcessState process)
    {
        if (recipeIndex < 0 || recipeIndex >= recipeCount)
        {
            return false;
        }

        process.SelectedRecipeIndex = recipeIndex;
        return true;
    }

    public static bool AdvanceOneTick(ref ItemProcessState process)
    {
        if (process.Status != ItemProcessStatus.Processing)
        {
            return false;
        }

        process.ElapsedTicks = math.min(
            process.ElapsedTicks + 1,
            process.DurationTicks);
        if (process.ElapsedTicks < process.DurationTicks)
        {
            return false;
        }

        process.Status = ItemProcessStatus.Completed;
        return true;
    }

    public static bool PublishCompletedOutput(
        in ItemProcessRecipe recipe,
        ref ItemProcessState process)
    {
        if (process.Status != ItemProcessStatus.Completed)
        {
            return false;
        }

        process.PendingOutputCount = math.max(1, recipe.OutputCount);
        process.Status = ItemProcessStatus.OutputBlocked;
        return true;
    }

    public static void AcknowledgeOutput(
        int transferredCount,
        ref ItemProcessState process)
    {
        if (transferredCount <= 0 ||
            process.Status != ItemProcessStatus.OutputBlocked)
        {
            return;
        }

        process.PendingOutputCount = math.max(
            0,
            process.PendingOutputCount - transferredCount);
        if (process.PendingOutputCount > 0 ||
            process.PendingOutput != Entity.Null)
        {
            return;
        }

        process.ElapsedTicks = 0;
        process.DurationTicks = 0;
        process.ActiveRecipeIndex = -1;
        process.Status = ItemProcessStatus.Idle;
    }

    public static float GetNormalizedProgress(
        in ItemProcessState process)
    {
        if (process.Status == ItemProcessStatus.Idle ||
            process.DurationTicks <= 0)
        {
            return 0f;
        }

        return math.saturate(
            process.ElapsedTicks / (float)process.DurationTicks);
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
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new ItemProcessJob()
            .ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct ItemProcessJob : IJobEntity
    {
        private void Execute(
            in DynamicBuffer<ItemProcessRecipe> recipes,
            DynamicBuffer<ItemProcessInput> inputs,
            ref ItemProcessState process)
        {
            int selectedRecipeIndex = process.SelectedRecipeIndex;
            if (process.Status == ItemProcessStatus.Idle &&
                selectedRecipeIndex >= 0 &&
                selectedRecipeIndex < recipes.Length)
            {
                ItemProcessRecipe selectedRecipe =
                    recipes[selectedRecipeIndex];
                ItemProcessUtility.TryStart(
                    selectedRecipeIndex,
                    selectedRecipe,
                    inputs,
                    ref process);
            }

            ItemProcessUtility.AdvanceOneTick(ref process);

            int activeRecipeIndex = process.ActiveRecipeIndex;
            if (process.Status == ItemProcessStatus.Completed &&
                activeRecipeIndex >= 0 &&
                activeRecipeIndex < recipes.Length)
            {
                ItemProcessRecipe activeRecipe =
                    recipes[activeRecipeIndex];
                ItemProcessUtility.PublishCompletedOutput(
                    activeRecipe,
                    ref process);
            }
        }
    }
}
