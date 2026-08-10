using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using System;

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
        using NativeArray<Entity> commandEntities =
            commandQuery.ToEntityArray(Allocator.Temp);
        using NativeList<CommandEnvelope> pending = new(Allocator.Temp);
        for (int entityIndex = 0; entityIndex < commandEntities.Length; entityIndex++)
        {
            Entity owner = commandEntities[entityIndex];
            DynamicBuffer<RecipeSelectionCommand> buffer =
                state.EntityManager.GetBuffer<RecipeSelectionCommand>(owner);
            for (int commandIndex = 0; commandIndex < buffer.Length; commandIndex++)
                pending.Add(new CommandEnvelope(owner, buffer[commandIndex]));
            buffer.Clear();
        }
        pending.Sort();
        using NativeArray<Entity> processors =
            processorQuery.ToEntityArray(Allocator.Temp);
        BlobAssetReference<FactoryDatabaseBlob> databaseReference =
            SystemAPI.GetSingleton<FactoryDatabase>().Value;
        ref FactoryDatabaseBlob database = ref databaseReference.Value;

        for (int i = 0; i < pending.Length; i++)
        {
            CommandEnvelope envelope = pending[i];
            RecipeSelectionCommand command = envelope.Command;
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
                bool isPlayer = state.EntityManager.HasComponent<PlayerIdentity>(
                    envelope.Owner);
                if (isPlayer &&
                    state.EntityManager.HasComponent<PlayerInventory>(envelope.Owner) &&
                    state.EntityManager.HasBuffer<InventorySlot>(envelope.Owner))
                {
                    PlayerInventory inventory = state.EntityManager.GetComponentData<
                        PlayerInventory>(envelope.Owner);
                    DynamicBuffer<InventorySlot> inventorySlots =
                        state.EntityManager.GetBuffer<InventorySlot>(envelope.Owner);
                    success = ItemProcessUtility.TryChangeRecipe(
                        selectedIndex,
                        range,
                        ref database,
                        slots,
                        ref process,
                        inventorySlots,
                        ref inventory);
                    if (success)
                    {
                        state.EntityManager.SetComponentData(
                            envelope.Owner,
                            inventory);
                    }
                }
                else if (!isPlayer)
                {
                    success = ItemProcessUtility.TryChangeRecipeWithoutPlayer(
                        selectedIndex,
                        range,
                        ref database,
                        slots,
                        ref process);
                }
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

            state.EntityManager.GetBuffer<RecipeSelectionResult>(envelope.Owner).Add(
                new RecipeSelectionResult
            {
                Header = command.Header,
                BuildingCell = command.BuildingCell,
                Recipe = command.Recipe,
                Success = success ? (byte)1 : (byte)0,
                FailureReason = failure
            });
        }
    }

    private readonly struct CommandEnvelope : IComparable<CommandEnvelope>
    {
        public CommandEnvelope(Entity owner, RecipeSelectionCommand command)
        {
            Owner = owner;
            Command = command;
        }
        public Entity Owner { get; }
        public RecipeSelectionCommand Command { get; }
        public int CompareTo(CommandEnvelope other)
        {
            int player = Command.Header.Player.Value.CompareTo(
                other.Command.Header.Player.Value);
            return player != 0 ? player : Command.Header.ClientSequence.CompareTo(
                other.Command.Header.ClientSequence);
        }
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
