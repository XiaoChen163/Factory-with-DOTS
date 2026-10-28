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
        state.RequireForUpdate<WorldGridConfig>();
        state.RequireForUpdate<GridTransformDirty>();
        dirtyQuery = new EntityQueryBuilder(Unity.Collections.Allocator.Temp)
            .WithAll<GridPlacement, GridTransformDirty>()
            .WithAllRW<LocalTransform>().Build(ref state);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        WorldGridConfig worldGrid =
            SystemAPI.GetSingleton<WorldGridConfig>();
        state.Dependency = new AlignToGridJob
        {
            WorldGrid = worldGrid
        }.ScheduleParallel(state.Dependency);
        state.Dependency.Complete();
        state.EntityManager.RemoveComponent<GridTransformDirty>(dirtyQuery);
    }

    [BurstCompile]
    private partial struct AlignToGridJob : IJobEntity
    {
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
                WorldGrid.CellSize;
            center.x += visualOffset.x;
            center.z += visualOffset.y;
            transform.Position = center;
            transform.Rotation =
                EcsGridUtility.RotationFromQuarterTurns(
                    placement.QuarterTurns);
        }
    }
}
