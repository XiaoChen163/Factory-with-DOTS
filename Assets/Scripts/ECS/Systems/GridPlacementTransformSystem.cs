using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[UpdateBefore(typeof(GridOccupancyIndexSystem))]
public partial struct GridPlacementTransformSystem : ISystem
{
    private EntityQuery dirtyQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GridDefinition>();
        state.RequireForUpdate<GridTransformDirty>();
        dirtyQuery = state.GetEntityQuery(
            ComponentType.ReadOnly<GridPlacement>(),
            ComponentType.ReadWrite<LocalTransform>(),
            ComponentType.ReadOnly<GridTransformDirty>());
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        GridDefinition grid =
            SystemAPI.GetSingleton<GridDefinition>();
        WorldGridConfig worldGrid =
            SystemAPI.TryGetSingleton(out WorldGridConfig configured)
                ? configured
                : new WorldGridConfig
                {
                    CellSize = grid.CellSize,
                    LayerHeight = EcsGridUtility.DefaultLayerHeight,
                    Origin = grid.Origin
                };
        state.Dependency = new AlignToGridJob
        {
            Grid = grid,
            WorldGrid = worldGrid
        }.ScheduleParallel(state.Dependency);
        state.Dependency.Complete();
        state.EntityManager.RemoveComponent<GridTransformDirty>(dirtyQuery);
    }

    [BurstCompile]
    private partial struct AlignToGridJob : IJobEntity
    {
        public GridDefinition Grid;
        public WorldGridConfig WorldGrid;

        private void Execute(
            ref LocalTransform transform,
            in GridPlacement placement,
            in GridTransformDirty dirty)
        {
            Unity.Mathematics.float3 center =
                EcsGridUtility.CellToWorldCenter(
                    placement.AnchorCell,
                    WorldGrid);
            Unity.Mathematics.float2 visualOffset =
                EcsGridUtility.GetVisualCenterOffset(
                    placement.FootprintSize) *
                Grid.CellSize;
            center.x += visualOffset.x;
            center.z += visualOffset.y;
            transform.Position = center;
            transform.Rotation =
                EcsGridUtility.RotationFromQuarterTurns(
                    placement.QuarterTurns);
        }
    }
}
