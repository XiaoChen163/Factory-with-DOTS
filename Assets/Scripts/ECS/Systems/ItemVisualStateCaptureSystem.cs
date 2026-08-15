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
        WorldGridConfig worldGrid =
            SystemAPI.TryGetSingleton(out WorldGridConfig configured)
                ? configured
                : new WorldGridConfig
                {
                    CellSize = EcsGridUtility.DefaultCellSize,
                    LayerHeight = EcsGridUtility.DefaultLayerHeight
                };
        state.Dependency = new CaptureBeltVisualStateJob
        {
            WorldGrid = worldGrid,
            ItemLookup = SystemAPI.GetComponentLookup<Item>(true),
            VisualStateLookup =
                SystemAPI.GetComponentLookup<ItemVisualState>(false)
        }.ScheduleParallel(state.Dependency);
        state.Dependency = new CaptureMergerVisualStateJob
        {
            WorldGrid = worldGrid,
            ItemLookup = SystemAPI.GetComponentLookup<Item>(true),
            VisualStateLookup =
                SystemAPI.GetComponentLookup<ItemVisualState>(false)
        }.ScheduleParallel(state.Dependency);
        state.Dependency = new CaptureRampBeltVisualStateJob
        {
            WorldGrid = worldGrid,
            ItemLookup = SystemAPI.GetComponentLookup<Item>(true),
            VisualStateLookup =
                SystemAPI.GetComponentLookup<ItemVisualState>(false)
        }.ScheduleParallel(state.Dependency);
        state.Dependency = new CaptureSplitterVisualStateJob
        {
            WorldGrid = worldGrid,
            ItemLookup = SystemAPI.GetComponentLookup<Item>(true),
            VisualStateLookup =
                SystemAPI.GetComponentLookup<ItemVisualState>(false)
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithNone(typeof(RampBelt))]
    private partial struct CaptureBeltVisualStateJob : IJobEntity
    {
        public WorldGridConfig WorldGrid;
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
                GetTargetPosition(topology.Cell, WorldGrid),
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
        public WorldGridConfig WorldGrid;
        [ReadOnly]
        public ComponentLookup<Item> ItemLookup;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<ItemVisualState> VisualStateLookup;

        private void Execute(in Merger merger)
        {
            Capture(
                merger.CurrentItem,
                GetTargetPosition(merger.Cell, WorldGrid),
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
        public WorldGridConfig WorldGrid;
        [ReadOnly]
        public ComponentLookup<Item> ItemLookup;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<ItemVisualState> VisualStateLookup;

        private void Execute(in Splitter splitter)
        {
            Capture(
                splitter.CurrentItem,
                GetTargetPosition(splitter.Cell, WorldGrid),
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
    private partial struct CaptureRampBeltVisualStateJob : IJobEntity
    {
        public WorldGridConfig WorldGrid;
        [ReadOnly] public ComponentLookup<Item> ItemLookup;
        [NativeDisableParallelForRestriction]
        public ComponentLookup<ItemVisualState> VisualStateLookup;

        private void Execute(
            in BeltTopology topology,
            in BeltState state,
            in RampBelt ramp)
        {
            Entity item = state.CurrentItem;
            if (item == Entity.Null ||
                !ItemLookup.HasComponent(item) ||
                !ItemLookup.IsComponentEnabled(item) ||
                !VisualStateLookup.HasComponent(item))
                return;

            float3 target = EcsGridUtility.CellToWorldCenter(topology.Cell, WorldGrid);
            target.y = (ramp.EntryHeight.ToWorldY(WorldGrid) +
                        ramp.ExitHeight.ToWorldY(WorldGrid)) * 0.5f + 0.535f;
            ItemVisualState visual = VisualStateLookup[item];
            if (math.distance(visual.ToPosition, target) > 0.0001f)
            {
                visual.FromPosition = visual.ToPosition;
                visual.ToPosition = target;
            }
            visual.Progress = math.saturate(state.Progress);
            VisualStateLookup[item] = visual;
        }
    }

    private static float3 GetTargetPosition(
        GridCell cell,
        in WorldGridConfig grid)
    {
        float3 position = EcsGridUtility.CellToWorldCenter(cell, grid);
        position.y += 0.535f;
        return position;
    }
}
