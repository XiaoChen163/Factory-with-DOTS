using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// Item-centered presentation system. It runs once per render frame and
/// interpolates the two latest fixed-tick ItemVisualState snapshots, so the
/// visual transform is never written multiple times for a frame that contains
/// several fixed ticks.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateBefore(typeof(EntitiesGraphicsSystem))]
public partial struct ItemTransformPresentationSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ItemVisualState>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new WriteItemTransformJob
        {
            PostTransformMatrixLookup =
                SystemAPI.GetComponentLookup<PostTransformMatrix>(true),
            LocalToWorldLookup =
                SystemAPI.GetComponentLookup<LocalToWorld>(false)
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct WriteItemTransformJob : IJobEntity
    {
        [ReadOnly]
        public ComponentLookup<PostTransformMatrix> PostTransformMatrixLookup;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalToWorld> LocalToWorldLookup;

        private void Execute(
            Entity entity,
            ref LocalTransform transform,
            in Item item,
            in ItemVisualState visualState)
        {
            float3 position = math.lerp(
                visualState.FromPosition,
                visualState.ToPosition,
                math.saturate(visualState.Progress));
            transform.Position = position;

            if (LocalToWorldLookup.HasComponent(entity))
            {
                float4x4 localToWorld = transform.ToMatrix();
                if (PostTransformMatrixLookup.HasComponent(entity))
                {
                    localToWorld = math.mul(
                        localToWorld,
                        PostTransformMatrixLookup[entity].Value);
                }
                LocalToWorldLookup[entity] =
                    new LocalToWorld
                    {
                        Value = localToWorld
                    };
            }
        }
    }
}
