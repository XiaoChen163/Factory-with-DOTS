using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateBefore(typeof(ItemProcessSystem))]
public partial struct RecipeSelectionCommandSystem : ISystem
{
    private EntityQuery commandQuery;
    private EntityQuery processorQuery;

    public void OnCreate(ref SystemState state)
    {
        commandQuery = state.GetEntityQuery(
            ComponentType.ReadWrite<RecipeSelectionCommand>(),
            ComponentType.ReadWrite<RecipeSelectionResult>());
        processorQuery = state.GetEntityQuery(
            ComponentType.ReadOnly<GridPlacement>(),
            ComponentType.ReadOnly<ItemProcessor>(),
            ComponentType.ReadWrite<ItemProcessState>(),
            ComponentType.ReadWrite<ProcessorItemSlot>());
        state.RequireForUpdate(commandQuery);
        state.RequireForUpdate<FactoryDatabase>();
    }

    public void OnUpdate(ref SystemState state)
    {
        state.Dependency.Complete();
        Entity commandEntity = commandQuery.GetSingletonEntity();
        DynamicBuffer<RecipeSelectionCommand> commands =
            state.EntityManager.GetBuffer<RecipeSelectionCommand>(commandEntity);
        DynamicBuffer<RecipeSelectionResult> results =
            state.EntityManager.GetBuffer<RecipeSelectionResult>(commandEntity);
        using NativeArray<Entity> processors =
            processorQuery.ToEntityArray(Allocator.Temp);
        BlobAssetReference<FactoryDatabaseBlob> databaseReference =
            SystemAPI.GetSingleton<FactoryDatabase>().Value;
        ref FactoryDatabaseBlob database = ref databaseReference.Value;

        for (int i = 0; i < commands.Length; i++)
        {
            RecipeSelectionCommand command = commands[i];
            RecipeSelectionFailureReason failure =
                RecipeSelectionFailureReason.BuildingNotFound;
            bool success = false;
            for (int p = 0; p < processors.Length; p++)
            {
                Entity processorEntity = processors[p];
                GridPlacement placement = state.EntityManager.GetComponentData<
                    GridPlacement>(processorEntity);
                if (!placement.AnchorCell.Equals(command.BuildingCell))
                    continue;

                ItemProcessor processor = state.EntityManager.GetComponentData<
                    ItemProcessor>(processorEntity);
                FactoryRecipeRangeBlob range =
                    FactoryDatabaseUtility.GetRecipeRange(
                        ref database,
                        processor.MachineType);
                int selectedIndex = -1;
                for (int recipeIndex = 0; recipeIndex < range.Count; recipeIndex++)
                {
                    FactoryRecipeBlob recipe =
                        database.Recipes[range.Start + recipeIndex];
                    if (recipe.Id == command.Recipe)
                    {
                        selectedIndex = recipeIndex;
                        break;
                    }
                }

                if (selectedIndex < 0)
                {
                    failure = IsKnownRecipe(ref database, command.Recipe)
                        ? RecipeSelectionFailureReason.MachineTypeMismatch
                        : RecipeSelectionFailureReason.RecipeNotFound;
                    break;
                }

                ItemProcessState process = state.EntityManager.GetComponentData<
                    ItemProcessState>(processorEntity);
                DynamicBuffer<ProcessorItemSlot> slots =
                    state.EntityManager.GetBuffer<ProcessorItemSlot>(processorEntity);
                success = ItemProcessUtility.TrySelectRecipe(
                    selectedIndex,
                    range,
                    ref database,
                    slots,
                    ref process);
                if (success)
                {
                    state.EntityManager.SetComponentData(processorEntity, process);
                    failure = RecipeSelectionFailureReason.None;
                }
                else
                {
                    failure = RecipeSelectionFailureReason.RecipeBusy;
                }
                break;
            }

            results.Add(new RecipeSelectionResult
            {
                Header = command.Header,
                BuildingCell = command.BuildingCell,
                Recipe = command.Recipe,
                Success = success ? (byte)1 : (byte)0,
                FailureReason = failure
            });
        }
        commands.Clear();
    }

    private static bool IsKnownRecipe(
        ref FactoryDatabaseBlob database,
        RecipeId recipeId)
    {
        for (int i = 0; i < database.Recipes.Length; i++)
            if (database.Recipes[i].Id == recipeId)
                return true;
        return false;
    }
}
