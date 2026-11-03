using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateBefore(typeof(UiSnapshotExportSystem))]
public partial class PlayerCommandResultExportSystem : SystemBase
{
    protected override void OnUpdate()
    {
        if (!PlayerCommandRuntimeServices.TryGetMailbox(World, out PlayerCommandMailbox mailbox))
            return;
        EntityQuery query = GetEntityQuery(ComponentType.ReadOnly<PlayerIdentity>());
        using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            ExportRecipe(mailbox, entity);
            ExportMoves(mailbox, entity);
            ExportBuilds(mailbox, entity);
        }
    }

    private void ExportRecipe(PlayerCommandMailbox mailbox, Entity entity)
    {
        if (!EntityManager.HasBuffer<RecipeSelectionResult>(entity)) return;
        DynamicBuffer<RecipeSelectionResult> values = EntityManager.GetBuffer<RecipeSelectionResult>(entity);
        for (int i = 0; i < values.Length; i++)
            mailbox.Publish(new PlayerCommandResult(values[i].Header,
                PlayerCommandKind.SelectRecipe, values[i].Success != 0,
                Map(values[i].FailureReason)));
        values.Clear();
    }

    private void ExportMoves(PlayerCommandMailbox mailbox, Entity entity)
    {
        if (!EntityManager.HasBuffer<MoveItemPlayerResult>(entity)) return;
        DynamicBuffer<MoveItemPlayerResult> values = EntityManager.GetBuffer<MoveItemPlayerResult>(entity);
        for (int i = 0; i < values.Length; i++)
            mailbox.Publish(new PlayerCommandResult(values[i].Header,
                PlayerCommandKind.MoveItem, values[i].Success != 0,
                Map(values[i].FailureReason)));
        values.Clear();
    }

    private void ExportBuilds(PlayerCommandMailbox mailbox, Entity entity)
    {
        if (!EntityManager.HasBuffer<GridBuildPlayerResult>(entity)) return;
        DynamicBuffer<GridBuildPlayerResult> values = EntityManager.GetBuffer<GridBuildPlayerResult>(entity);
        for (int i = 0; i < values.Length; i++)
            mailbox.Publish(new PlayerCommandResult(values[i].Header,
                PlayerCommandKind.GridBuild, values[i].Success != 0,
                values[i].Success != 0 ? PlayerCommandFailureReason.None : PlayerCommandFailureReason.GridRejected));
        values.Clear();
    }

    private static PlayerCommandFailureReason Map(RecipeSelectionFailureReason value) => value switch
    {
        RecipeSelectionFailureReason.BuildingNotFound => PlayerCommandFailureReason.BuildingNotFound,
        RecipeSelectionFailureReason.RecipeNotFound => PlayerCommandFailureReason.RecipeNotFound,
        RecipeSelectionFailureReason.MachineTypeMismatch => PlayerCommandFailureReason.MachineTypeMismatch,
        RecipeSelectionFailureReason.RecipeBusy => PlayerCommandFailureReason.RecipeBusy,
        _ => PlayerCommandFailureReason.None
    };

    private static PlayerCommandFailureReason Map(MoveItemFailureReason value) => value switch
    {
        MoveItemFailureReason.PlayerNotFound => PlayerCommandFailureReason.PlayerNotFound,
        MoveItemFailureReason.OwnerNotFound => PlayerCommandFailureReason.OwnerNotFound,
        MoveItemFailureReason.InvalidSlot => PlayerCommandFailureReason.InvalidSlot,
        MoveItemFailureReason.EmptySource => PlayerCommandFailureReason.EmptySource,
        MoveItemFailureReason.StaleSnapshot => PlayerCommandFailureReason.StaleSnapshot,
        MoveItemFailureReason.DestinationRejected => PlayerCommandFailureReason.DestinationRejected,
        MoveItemFailureReason.CapacityExceeded => PlayerCommandFailureReason.CapacityExceeded,
        _ => PlayerCommandFailureReason.None
    };
}
