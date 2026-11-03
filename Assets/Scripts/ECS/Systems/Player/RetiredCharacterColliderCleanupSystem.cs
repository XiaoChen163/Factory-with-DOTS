using Unity.Collections;
using Unity.Entities;
using Unity.Physics.Systems;

[UpdateInGroup(typeof(AfterPhysicsSystemGroup), OrderLast = true)]
public partial class RetiredCharacterColliderCleanupSystem : SystemBase
{
    private EntityQuery retired;
    protected override void OnCreate()
    {
        retired = GetEntityQuery(ComponentType.ReadOnly<RetiredCharacterCollider>());
        RequireForUpdate(retired);
    }
    protected override void OnUpdate() => DisposeRetired();
    protected override void OnDestroy() => DisposeRetired();

    private void DisposeRetired()
    {
        EntityManager.CompleteAllTrackedJobs();
        using var entities = retired.ToEntityArray(Allocator.Temp);
        foreach (Entity entity in entities)
        {
            var collider = EntityManager.GetComponentData<RetiredCharacterCollider>(entity).Value;
            if (collider.IsCreated) collider.Dispose();
            EntityManager.DestroyEntity(entity);
        }
    }
}
