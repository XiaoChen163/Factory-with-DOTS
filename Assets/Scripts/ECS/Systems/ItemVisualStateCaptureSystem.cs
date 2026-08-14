using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Captures the latest transport position of every active item once per fixed
/// tick. The presentation system later reads these snapshots and writes the
/// visual transform exactly once per render frame.
/// </summary>
[BurstCompile]
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(ItemPortBufferSwapSystem))]
public partial struct ItemVisualStateCaptureSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ItemVisualState>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new CaptureBeltVisualStateJob
        {
            ItemLookup = SystemAPI.GetComponentLookup<Item>(true),
            VisualStateLookup =
                SystemAPI.GetComponentLookup<ItemVisualState>(false)
        }.ScheduleParallel(state.Dependency);
        state.Dependency = new CaptureMergerVisualStateJob
        {
            ItemLookup = SystemAPI.GetComponentLookup<Item>(true),
            VisualStateLookup =
                SystemAPI.GetComponentLookup<ItemVisualState>(false)
        }.ScheduleParallel(state.Dependency);
        state.Dependency = new CaptureSplitterVisualStateJob
        {
            ItemLookup = SystemAPI.GetComponentLookup<Item>(true),
            VisualStateLookup =
                SystemAPI.GetComponentLookup<ItemVisualState>(false)
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct CaptureBeltVisualStateJob : IJobEntity
    {
        [ReadOnly]
        public ComponentLookup<Item> ItemLookup;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<ItemVisualState> VisualStateLookup;

        private void Execute(
            in BeltTopology topology,
            in BeltState state)
        {
            Capture(
                state.CurrentItem,
                new float3(
                    topology.Cell.X + 0.5f,
                    0.535f,
                    topology.Cell.Z + 0.5f),
                state.Progress);
        }

        private void Capture(
            Entity item,
            float3 targetPosition,
            float progress)
        {
            if (item == Entity.Null ||
                !ItemLookup.HasComponent(item) ||
                !ItemLookup.IsComponentEnabled(item) ||
                !VisualStateLookup.HasComponent(item))
            {
                return;
            }

            ItemVisualState visualState = VisualStateLookup[item];
            if (math.distance(
                    visualState.ToPosition,
                    targetPosition) > 0.0001f)
            {
                visualState.FromPosition = visualState.ToPosition;
                visualState.ToPosition = targetPosition;
            }
            visualState.Progress = math.saturate(progress);
            VisualStateLookup[item] = visualState;
        }
    }

    [BurstCompile]
    private partial struct CaptureMergerVisualStateJob : IJobEntity
    {
        [ReadOnly]
        public ComponentLookup<Item> ItemLookup;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<ItemVisualState> VisualStateLookup;

        private void Execute(in Merger merger)
        {
            Capture(
                merger.CurrentItem,
                new float3(
                    merger.Cell.X + 0.5f,
                    0.535f,
                    merger.Cell.Z + 0.5f),
                1f);
        }

        private void Capture(
            Entity item,
            float3 targetPosition,
            float progress)
        {
            if (item == Entity.Null ||
                !ItemLookup.HasComponent(item) ||
                !ItemLookup.IsComponentEnabled(item) ||
                !VisualStateLookup.HasComponent(item))
            {
                return;
            }

            ItemVisualState visualState = VisualStateLookup[item];
            if (math.distance(
                    visualState.ToPosition,
                    targetPosition) > 0.0001f)
            {
                visualState.FromPosition = visualState.ToPosition;
                visualState.ToPosition = targetPosition;
            }
            visualState.Progress = math.saturate(progress);
            VisualStateLookup[item] = visualState;
        }
    }

    [BurstCompile]
    private partial struct CaptureSplitterVisualStateJob : IJobEntity
    {
        [ReadOnly]
        public ComponentLookup<Item> ItemLookup;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<ItemVisualState> VisualStateLookup;

        private void Execute(in Splitter splitter)
        {
            Capture(
                splitter.CurrentItem,
                new float3(
                    splitter.Cell.X + 0.5f,
                    0.535f,
                    splitter.Cell.Z + 0.5f),
                1f);
        }

        private void Capture(
            Entity item,
            float3 targetPosition,
            float progress)
        {
            if (item == Entity.Null ||
                !ItemLookup.HasComponent(item) ||
                !ItemLookup.IsComponentEnabled(item) ||
                !VisualStateLookup.HasComponent(item))
            {
                return;
            }

            ItemVisualState visualState = VisualStateLookup[item];
            if (math.distance(
                    visualState.ToPosition,
                    targetPosition) > 0.0001f)
            {
                visualState.FromPosition = visualState.ToPosition;
                visualState.ToPosition = targetPosition;
            }
            visualState.Progress = math.saturate(progress);
            VisualStateLookup[item] = visualState;
        }
    }
}
