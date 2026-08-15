using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Factory.Tests
{
    public sealed class MultiLevelStage2Tests : FactoryWorldFixture
    {
        [Test]
        public void BuildingRevision_DoesNotInvalidateTransportTopology()
        {
            Entity grid = EntityManager.CreateEntity();
            EntityManager.AddComponentData(grid, new GridDefinition
            {
                Size = new int2(16, 16),
                CellSize = 1f,
                Revision = 1
            });
            EntityManager.AddComponentData(
                grid,
                new BuildingOccupancyRevision { Value = 1 });
            EntityManager.AddComponentData(
                grid,
                new TransportTopologyRevision { Value = 1 });

            BeltTransferSystem transfer =
                GetOrCreateManagedSystem<BeltTransferSystem>();
            UpdateSystem(transfer);
            int initialRebuilds = transfer.TransportTopologyRebuildCount;

            EntityManager.SetComponentData(
                grid,
                new BuildingOccupancyRevision { Value = 2 });
            UpdateSystem(transfer);

            Assert.That(
                transfer.TransportTopologyRebuildCount,
                Is.EqualTo(initialRebuilds));

            EntityManager.SetComponentData(
                grid,
                new TransportTopologyRevision { Value = 2 });
            UpdateSystem(transfer);

            Assert.That(
                transfer.TransportTopologyRebuildCount,
                Is.EqualTo(initialRebuilds + 1));
        }

        [Test]
        public void PlacementTransform_ChangesOnlyWhenExplicitlyDirty()
        {
            Entity grid = EntityManager.CreateEntity();
            EntityManager.AddComponentData(grid, new GridDefinition
            {
                Size = new int2(16, 16),
                Origin = new float3(10f, 0f, 20f),
                CellSize = 2f,
                Revision = 1
            });

            Entity building = EntityManager.CreateEntity();
            EntityManager.AddComponentData(building, new GridPlacement
            {
                AnchorCell = new GridCell(2, 0, 3),
                FootprintSize = new int2(1, 1),
                QuarterTurns = 1,
                Kind = BuildingKind.Storage
            });
            EntityManager.AddComponentData(
                building,
                LocalTransform.FromPosition(new float3(99f, 4f, 99f)));

            SystemHandle transformSystem =
                TestWorld.GetOrCreateSystem<GridPlacementTransformSystem>();
            transformSystem.Update(TestWorld.Unmanaged);
            EntityManager.CompleteAllTrackedJobs();
            Assert.That(
                EntityManager.GetComponentData<LocalTransform>(building).Position,
                Is.EqualTo(new float3(99f, 4f, 99f)));

            EntityManager.AddComponent<GridTransformDirty>(building);
            transformSystem.Update(TestWorld.Unmanaged);
            EntityManager.CompleteAllTrackedJobs();

            LocalTransform aligned =
                EntityManager.GetComponentData<LocalTransform>(building);
            Assert.That(aligned.Position, Is.EqualTo(new float3(15f, 0f, 27f)));
            Assert.That(
                EntityManager.HasComponent<GridTransformDirty>(building),
                Is.False);
        }

        [Test]
        public void OccupancyRevision_RebuildsAfterExplicitPlacementEdit()
        {
            Entity grid = EntityManager.CreateEntity();
            EntityManager.AddComponentData(grid, new GridDefinition
            {
                Size = new int2(16, 16),
                CellSize = 1f
            });
            EntityManager.AddComponentData(
                grid,
                new BuildingOccupancyRevision { Value = 1 });

            Entity building = EntityManager.CreateEntity();
            EntityManager.AddComponentData(building, new GridPlacement
            {
                AnchorCell = new GridCell(1, 0, 1),
                FootprintSize = new int2(1, 1),
                Kind = BuildingKind.Storage
            });
            EntityManager.AddBuffer<OccupiedCellOffset>(building).Add(
                new OccupiedCellOffset { Value = int2.zero });

            GridOccupancyIndexSystem occupancy =
                GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
            UpdateSystem(occupancy);
            Assert.That(occupancy.TryGetOccupant(new GridCell(1, 0, 1), out _));

            GridPlacement placement =
                EntityManager.GetComponentData<GridPlacement>(building);
            placement.AnchorCell = new GridCell(4, 0, 5);
            EntityManager.SetComponentData(building, placement);
            EntityManager.SetComponentData(
                grid,
                new BuildingOccupancyRevision { Value = 2 });
            UpdateSystem(occupancy);

            Assert.That(
                occupancy.TryGetOccupant(new GridCell(1, 0, 1), out _),
                Is.False);
            Assert.That(
                occupancy.TryGetOccupant(new GridCell(4, 0, 5), out Entity owner),
                Is.True);
            Assert.That(owner, Is.EqualTo(building));
        }
    }
}
