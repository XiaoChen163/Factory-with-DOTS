using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;


[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(BeltTransferSystem))]
public partial struct BeltItemPositionSystem : ISystem
{
    private const float TransferSpeedMultiplier = 2f;
    private const float MinimumVisualSpeed = 0.01f;
    private const float DefaultJunctionVisualSpeed =
        TransferSpeedMultiplier;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        BeltItemPositionJob job = new BeltItemPositionJob
        {
            DeltaTime = deltaTime,
            ItemLookup = SystemAPI.GetComponentLookup<Item>(false),
            TransformLookup =
                SystemAPI.GetComponentLookup<LocalTransform>(false)
        };
        state.Dependency = job.ScheduleParallel(state.Dependency);
        state.Dependency = new MergerItemPositionJob
        {
            DeltaTime = deltaTime,
            ItemLookup = SystemAPI.GetComponentLookup<Item>(false),
            TransformLookup =
                SystemAPI.GetComponentLookup<LocalTransform>(false)
        }.ScheduleParallel(state.Dependency);
        state.Dependency = new SplitterItemPositionJob
        {
            DeltaTime = deltaTime,
            ItemLookup = SystemAPI.GetComponentLookup<Item>(false),
            TransformLookup =
                SystemAPI.GetComponentLookup<LocalTransform>(false)
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct BeltItemPositionJob : IJobEntity
    {
        public float DeltaTime;

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

            float3 targetPosition = new float3(
                belt.Cell.x + 0.5f,
                0.535f,
                belt.Cell.y + 0.5f);
            float visualSpeed = math.max(
                MinimumVisualSpeed,
                belt.CellsPerSecond * TransferSpeedMultiplier);
            MoveItemTowards(
                belt.CurrentItem,
                targetPosition,
                visualSpeed * DeltaTime,
                ItemLookup,
                TransformLookup);
        }
    }

    [BurstCompile]
    private partial struct MergerItemPositionJob : IJobEntity
    {
        public float DeltaTime;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<Item> ItemLookup;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalTransform> TransformLookup;

        private void Execute(in Merger merger)
        {
            MoveItemTowards(
                merger.CurrentItem,
                new float3(
                    merger.Cell.x + 0.5f,
                    0.535f,
                    merger.Cell.y + 0.5f),
                DefaultJunctionVisualSpeed * DeltaTime,
                ItemLookup,
                TransformLookup);
        }
    }

    [BurstCompile]
    private partial struct SplitterItemPositionJob : IJobEntity
    {
        public float DeltaTime;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<Item> ItemLookup;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalTransform> TransformLookup;

        private void Execute(in Splitter splitter)
        {
            MoveItemTowards(
                splitter.CurrentItem,
                new float3(
                    splitter.Cell.x + 0.5f,
                    0.535f,
                    splitter.Cell.y + 0.5f),
                DefaultJunctionVisualSpeed * DeltaTime,
                ItemLookup,
                TransformLookup);
        }
    }

    private static void MoveItemTowards(
        Entity itemEntity,
        float3 targetPosition,
        float maxDistanceDelta,
        ComponentLookup<Item> itemLookup,
        ComponentLookup<LocalTransform> transformLookup)
    {
        if (itemEntity == Entity.Null ||
            !itemLookup.HasComponent(itemEntity))
        {
            return;
        }

        Item item = itemLookup[itemEntity];
        float3 offset = targetPosition - item.Position;
        float distance = math.length(offset);
        item.Position =
            distance <= maxDistanceDelta || distance <= math.EPSILON
                ? targetPosition
                : item.Position +
                  offset * (maxDistanceDelta / distance);
        itemLookup[itemEntity] = item;

        if (!transformLookup.HasComponent(itemEntity))
        {
            return;
        }

        LocalTransform itemTransform = transformLookup[itemEntity];
        itemTransform.Position = item.Position;
        transformLookup[itemEntity] = itemTransform;
    }
}
