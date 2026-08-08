using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct PlayerBootstrapSystem : ISystem
{
    public const ushort DefaultSlotCount = 24;

    public void OnCreate(ref SystemState state)
    {
        EntityQuery query = state.GetEntityQuery(
            ComponentType.ReadOnly<PlayerIdentity>());
        if (!query.IsEmptyIgnoreFilter)
        {
            return;
        }

        Entity player = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(player, new PlayerIdentity
        {
            Value = new PlayerId { Value = 1 }
        });
        state.EntityManager.AddComponentData(player, new PlayerInventory
        {
            SlotCount = DefaultSlotCount
        });
        state.EntityManager.AddComponentData(player, new ItemContainerIdentity
        {
            RuntimeId = 1
        });
        DynamicBuffer<InventorySlot> slots =
            state.EntityManager.AddBuffer<InventorySlot>(player);
        slots.ResizeUninitialized(DefaultSlotCount);
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = default;
        }
        state.EntityManager.AddBuffer<MoveItemPlayerCommand>(player);
        state.EntityManager.AddBuffer<MoveItemPlayerResult>(player);
        state.EntityManager.AddBuffer<RecipeSelectionCommand>(player);
        state.EntityManager.AddBuffer<RecipeSelectionResult>(player);
        state.EntityManager.AddBuffer<GridBuildPlayerCommand>(player);
        state.EntityManager.AddBuffer<GridBuildPlayerResult>(player);
        state.EntityManager.AddComponentData(player, new PlayerCommandSequenceState());
    }

    public void OnUpdate(ref SystemState state)
    {
    }
}
