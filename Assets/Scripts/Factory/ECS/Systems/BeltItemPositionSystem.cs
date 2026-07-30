using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;


[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(BeltTransferSystem))]
public partial struct BeltItemPositionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Belt>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        BeltItemPositionJob job = new BeltItemPositionJob
        {
            ItemLookup = SystemAPI.GetComponentLookup<Item>(false),
            TransformLookup =
                SystemAPI.GetComponentLookup<LocalTransform>(false)
        };
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct BeltItemPositionJob : IJobEntity
    {
        [NativeDisableParallelForRestriction]
        public ComponentLookup<Item> ItemLookup;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalTransform> TransformLookup;


        private void Execute(in Belt belt)
        {
            if (belt.CurrentItem == Entity.Null ||
                !ItemLookup.HasComponent(belt.CurrentItem))
            {
                return;
            }

            Item item = ItemLookup[belt.CurrentItem];
            float3 start = new float3(
                belt.Cell.x,
                0.65f,
                belt.Cell.y);
            float3 end = new float3(
                belt.NextCell.x,
                0.65f,
                belt.NextCell.y);
            item.Position = math.lerp(start, end, belt.Progress);
            ItemLookup[belt.CurrentItem] = item;

            if (TransformLookup.HasComponent(belt.CurrentItem))
            {
                LocalTransform itemTransform =
                    TransformLookup[belt.CurrentItem];
                itemTransform.Position = item.Position;
                TransformLookup[belt.CurrentItem] = itemTransform;
            }

        }
    }
}
