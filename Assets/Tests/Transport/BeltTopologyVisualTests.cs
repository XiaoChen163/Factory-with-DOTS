using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Factory.Tests
{
    public sealed class BeltTopologyVisualTests : FactoryWorldFixture
    {
        private static readonly int2 East = new int2(1, 0);

        [Test]
        public void BeltVisualDirtyBuffer_IsClearedAfterLocalRefresh()
        {
            Entity grid = CreateTransportGrid();
            EntityManager.AddBuffer<BeltVisualDirtyCell>(grid).Add(
                new BeltVisualDirtyCell
                {
                    Value = new GridCell(5, 0, 5)
                });

            GridOccupancyIndexSystem occupancy =
                GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
            UpdateSystem(occupancy);

            BeltTopologyVisualSystem visuals =
                GetOrCreateManagedSystem<BeltTopologyVisualSystem>();
            UpdateSystem(visuals);

            DynamicBuffer<BeltVisualDirtyCell> dirty =
                EntityManager.GetBuffer<BeltVisualDirtyCell>(grid);
            Assert.That(dirty.IsEmpty, Is.True);
        }

        [Test]
        public void BeltTopologyRefresh_KeepsConnectedEdgeHidden()
        {
            Entity grid = CreateTransportGrid();
            EntityManager.AddBuffer<BeltVisualDirtyCell>(grid).Add(
                new BeltVisualDirtyCell
                {
                    Value = new GridCell(1, 0, 0)
                });

            CreateVisualBelt(
                new GridCell(0, 0, 0),
                East,
                BuildingPortType.Output);
            Entity target = CreateVisualBelt(
                new GridCell(1, 0, 0),
                East,
                BuildingPortType.Input);
            Entity targetVisual = EntityManager
                .GetComponentData<BuildingVisualReference>(target)
                .Value;
            Entity westEdge = EntityManager
                .GetComponentData<BeltVisualParts>(targetVisual)
                .WestEdge;
            EntityManager.AddComponent<DisableRendering>(westEdge);

            GridOccupancyIndexSystem occupancy =
                GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
            UpdateSystem(occupancy);
            Assert.That(
                occupancy.TryGetOccupant(new GridCell(0, 0, 0), out _),
                Is.True);
            Assert.That(
                occupancy.TryGetOccupant(new GridCell(1, 0, 0), out _),
                Is.True);

            BeltTopologyVisualSystem visuals =
                GetOrCreateManagedSystem<BeltTopologyVisualSystem>();
            UpdateSystem(visuals);

            Assert.That(
                EntityManager.HasComponent<DisableRendering>(westEdge),
                Is.True,
                "The connected edge must remain hidden after a full refresh.");
            Assert.That(
                EntityManager.HasComponent<DisableRendering>(
                    EntityManager
                        .GetComponentData<BeltVisualParts>(targetVisual)
                        .EastEdge),
                Is.False,
                "Unconnected edges should become visible after a refresh.");
        }

        [Test]
        public void RampBeltTopologyRefresh_HidesPlanarConnectionEdges()
        {
            Entity grid = CreateTransportGrid();
            DynamicBuffer<BeltVisualDirtyCell> dirty =
                EntityManager.AddBuffer<BeltVisualDirtyCell>(grid);
            dirty.Add(new BeltVisualDirtyCell { Value = new GridCell(0, 0, 0) });
            dirty.Add(new BeltVisualDirtyCell { Value = new GridCell(1, 0, 0) });

            Entity source = CreateVisualBelt(
                new GridCell(0, 0, 0), East, BuildingPortType.Output);
            Entity target = CreateVisualBelt(
                new GridCell(1, 0, 0), East, BuildingPortType.Input);
            EntityManager.AddComponentData(target, new RampBelt
            {
                TravelDirection = East,
                EntryHeight = new GridHeight(0),
                ExitHeight = new GridHeight(4)
            });

            GridOccupancyIndexSystem occupancy =
                GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
            UpdateSystem(occupancy);
            UpdateSystem(GetOrCreateManagedSystem<BeltTopologyVisualSystem>());

            BeltVisualParts sourceParts = EntityManager.GetComponentData<
                BeltVisualParts>(EntityManager.GetComponentData<
                    BuildingVisualReference>(source).Value);
            BeltVisualParts targetParts = EntityManager.GetComponentData<
                BeltVisualParts>(EntityManager.GetComponentData<
                    BuildingVisualReference>(target).Value);
            Assert.That(EntityManager.HasComponent<DisableRendering>(
                sourceParts.EastEdge), Is.True);
            Assert.That(EntityManager.HasComponent<DisableRendering>(
                targetParts.WestEdge), Is.True);
        }

        [Test]
        public void RampBeltTopologyRefresh_AlwaysHidesHighAndLowEdges()
        {
            Entity grid = CreateTransportGrid();
            EntityManager.AddBuffer<BeltVisualDirtyCell>(grid).Add(
                new BeltVisualDirtyCell { Value = new GridCell(1, 0, 0) });

            Entity ramp = CreateVisualBelt(
                new GridCell(1, 0, 0), East, BuildingPortType.Input);
            EntityManager.AddComponentData(ramp, new RampBelt
            {
                TravelDirection = East,
                EntryHeight = new GridHeight(0),
                ExitHeight = new GridHeight(4)
            });

            GridOccupancyIndexSystem occupancy =
                GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
            UpdateSystem(occupancy);
            UpdateSystem(GetOrCreateManagedSystem<BeltTopologyVisualSystem>());

            BeltVisualParts parts = EntityManager.GetComponentData<
                BeltVisualParts>(EntityManager.GetComponentData<
                    BuildingVisualReference>(ramp).Value);
            Assert.That(EntityManager.HasComponent<DisableRendering>(
                parts.EastEdge), Is.True,
                "A ramp belt's high edge must always be hidden.");
            Assert.That(EntityManager.HasComponent<DisableRendering>(
                parts.WestEdge), Is.True,
                "A ramp belt's low edge must always be hidden.");
            Assert.That(EntityManager.HasComponent<DisableRendering>(
                parts.NorthEdge), Is.False,
                "A ramp belt's lateral edges must remain visible.");
            Assert.That(EntityManager.HasComponent<DisableRendering>(
                parts.SouthEdge), Is.False,
                "A ramp belt's lateral edges must remain visible.");
        }

        [Test]
        public void RampBeltTopologyRefresh_UsesBeltTopologyForBothSides()
        {
            Entity grid = CreateTransportGrid();
            DynamicBuffer<BeltVisualDirtyCell> dirty =
                EntityManager.AddBuffer<BeltVisualDirtyCell>(grid);
            dirty.Add(new BeltVisualDirtyCell { Value = new GridCell(0, 0, 0) });
            dirty.Add(new BeltVisualDirtyCell { Value = new GridCell(1, 0, 0) });

            Entity source = CreateVisualBelt(
                new GridCell(0, 0, 0), East, BuildingPortType.Output);
            Entity target = CreateVisualBelt(
                new GridCell(1, 0, 0), East, BuildingPortType.Input);
            EntityManager.AddComponentData(target, new RampBelt
            {
                TravelDirection = East,
                EntryHeight = new GridHeight(0),
                ExitHeight = new GridHeight(4)
            });
            EntityManager.RemoveComponent<BuildingPort>(source);
            EntityManager.RemoveComponent<BuildingPort>(target);

            GridOccupancyIndexSystem occupancy =
                GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
            UpdateSystem(occupancy);
            UpdateSystem(GetOrCreateManagedSystem<BeltTopologyVisualSystem>());

            BeltVisualParts sourceParts = EntityManager.GetComponentData<
                BeltVisualParts>(EntityManager.GetComponentData<
                    BuildingVisualReference>(source).Value);
            BeltVisualParts targetParts = EntityManager.GetComponentData<
                BeltVisualParts>(EntityManager.GetComponentData<
                    BuildingVisualReference>(target).Value);
            Assert.That(EntityManager.HasComponent<DisableRendering>(
                sourceParts.EastEdge), Is.True);
            Assert.That(EntityManager.HasComponent<DisableRendering>(
                targetParts.WestEdge), Is.True);
        }

        [Test]
        public void RampBeltTopologyRefresh_HidesExplicitConnectionEdges()
        {
            Entity grid = CreateTransportGrid();
            DynamicBuffer<BeltVisualDirtyCell> dirty =
                EntityManager.AddBuffer<BeltVisualDirtyCell>(grid);
            dirty.Add(new BeltVisualDirtyCell { Value = new GridCell(0, 0, 0) });
            dirty.Add(new BeltVisualDirtyCell { Value = new GridCell(1, 0, 0) });

            Entity source = CreateVisualBelt(
                new GridCell(0, 0, 0), East, BuildingPortType.Output);
            Entity target = CreateVisualBelt(
                new GridCell(1, 0, 0), East, BuildingPortType.Input);
            BeltTopology sourceTopology =
                EntityManager.GetComponentData<BeltTopology>(source);
            sourceTopology.ConnectionMode = TransportConnectionMode.ExplicitOnly;
            EntityManager.SetComponentData(source, sourceTopology);
            BeltTopology targetTopology =
                EntityManager.GetComponentData<BeltTopology>(target);
            targetTopology.ConnectionMode = TransportConnectionMode.ExplicitOnly;
            EntityManager.SetComponentData(target, targetTopology);
            EntityManager.AddComponentData(source, new RampBelt());
            EntityManager.AddComponentData(target, new RampBelt());
            EntityManager.AddBuffer<TransportExplicitEdge>(grid).Add(
                new TransportExplicitEdge
                {
                    Source = source,
                    Target = target
                });

            GridOccupancyIndexSystem occupancy =
                GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
            UpdateSystem(occupancy);
            UpdateSystem(GetOrCreateManagedSystem<BeltTopologyVisualSystem>());

            BeltVisualParts sourceParts = EntityManager.GetComponentData<
                BeltVisualParts>(EntityManager.GetComponentData<
                    BuildingVisualReference>(source).Value);
            BeltVisualParts targetParts = EntityManager.GetComponentData<
                BeltVisualParts>(EntityManager.GetComponentData<
                    BuildingVisualReference>(target).Value);
            Assert.That(EntityManager.HasComponent<DisableRendering>(
                sourceParts.EastEdge), Is.True);
            Assert.That(EntityManager.HasComponent<DisableRendering>(
                targetParts.WestEdge), Is.True);
        }

        [Test]
        public void BeltTopologyRefresh_UsesConnectedGridCellsForExplicitEdges()
        {
            Entity grid = CreateTransportGrid();
            DynamicBuffer<BeltVisualDirtyCell> dirty =
                EntityManager.AddBuffer<BeltVisualDirtyCell>(grid);
            dirty.Add(new BeltVisualDirtyCell { Value = new GridCell(0, 0, 0) });
            dirty.Add(new BeltVisualDirtyCell { Value = new GridCell(0, 0, 1) });

            Entity source = CreateVisualBelt(
                new GridCell(0, 0, 0), East, BuildingPortType.Output);
            Entity target = CreateVisualBelt(
                new GridCell(0, 0, 1), East, BuildingPortType.Input);
            BeltTopology sourceTopology =
                EntityManager.GetComponentData<BeltTopology>(source);
            sourceTopology.ConnectionMode = TransportConnectionMode.ExplicitOnly;
            EntityManager.SetComponentData(source, sourceTopology);
            BeltTopology targetTopology =
                EntityManager.GetComponentData<BeltTopology>(target);
            targetTopology.ConnectionMode = TransportConnectionMode.ExplicitOnly;
            EntityManager.SetComponentData(target, targetTopology);
            EntityManager.AddBuffer<TransportExplicitEdge>(grid).Add(
                new TransportExplicitEdge
                {
                    Source = source,
                    Target = target
                });

            GridOccupancyIndexSystem occupancy =
                GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
            UpdateSystem(occupancy);
            UpdateSystem(GetOrCreateManagedSystem<BeltTopologyVisualSystem>());

            BeltVisualParts sourceParts = EntityManager.GetComponentData<
                BeltVisualParts>(EntityManager.GetComponentData<
                    BuildingVisualReference>(source).Value);
            BeltVisualParts targetParts = EntityManager.GetComponentData<
                BeltVisualParts>(EntityManager.GetComponentData<
                    BuildingVisualReference>(target).Value);
            Assert.That(EntityManager.HasComponent<DisableRendering>(
                sourceParts.NorthEdge), Is.True);
            Assert.That(EntityManager.HasComponent<DisableRendering>(
                targetParts.SouthEdge), Is.True);
            Assert.That(EntityManager.HasComponent<DisableRendering>(
                sourceParts.EastEdge), Is.False);
            Assert.That(EntityManager.HasComponent<DisableRendering>(
                targetParts.WestEdge), Is.False);
        }

        private Entity CreateVisualBelt(
            GridCell cell,
            int2 direction,
            BuildingPortType portType)
        {
            Entity building = EntityManager.CreateEntity();
            EntityManager.AddComponentData(
                building,
                new BeltTopology
                {
                    Cell = cell,
                    Direction = direction,
                    CellsPerSecond = 1f
                });
            EntityManager.AddComponentData(
                building,
                new GridPlacement
                {
                    AnchorCell = cell,
                    FootprintSize = new int2(1, 1),
                    QuarterTurns = 0,
                    Kind = BuildingKind.Belt
                });
            EntityManager.AddBuffer<OccupiedCellOffset>(building).Add(
                new OccupiedCellOffset
                {
                    Value = int2.zero
                });
            EntityManager.AddBuffer<BuildingPort>(building).Add(
                new BuildingPort
                {
                    CellOffset = portType == BuildingPortType.Output
                        ? direction
                        : -direction,
                    Direction = direction,
                    Type = portType,
                    Index = 0
                });

            Entity visual = EntityManager.CreateEntity();
            EntityManager.AddComponentData(
                visual,
                new BeltVisualParts
                {
                    EastEdge = CreateEdgeEntity(),
                    NorthEdge = CreateEdgeEntity(),
                    WestEdge = CreateEdgeEntity(),
                    SouthEdge = CreateEdgeEntity(),
                    DirectionTriangle = Entity.Null
                });
            EntityManager.AddComponentData(
                building,
                new BuildingVisualReference
                {
                    Value = visual
                });
            return building;
        }

        private Entity CreateEdgeEntity()
        {
            Entity edge = EntityManager.CreateEntity();
            EntityManager.AddComponent<DisableRendering>(edge);
            return edge;
        }
    }
}
