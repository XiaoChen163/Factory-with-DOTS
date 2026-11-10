using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using UnityEditor;

namespace Factory.Tests
{
    public sealed class BeltTopologyVisualTests : FactoryWorldFixture
    {
        private static readonly int2 East = new int2(1, 0);

        [Test]
        public void ImportedMultiMaterialBelts_HideAllConnectedRailSubmeshes_AndRestoreOnDisconnect()
        {
            // Bake the actual FBX models: mock single-entity rails miss the extra
            // render entities created by Entities Graphics for multiple materials.
            var candidate = FactoryDatabaseCsvImporter.ReadCandidate();
            var root = new GameObject("Belt rendering regression catalog");
            var authoring = root.AddComponent<BuildingPrefabCatalogAuthoring>();
            authoring.database = candidate;
            authoring.inputPortVisualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Ports/InputPortView.prefab");
            authoring.outputPortVisualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Ports/OutputPortView.prefab");
            var hybrid = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Unity.Entities.Hybrid");
            var store = (IDisposable)Activator.CreateInstance(hybrid.GetType("Unity.Entities.BlobAssetStore"), new object[] { 128 });
            try
            {
                var settingsType = hybrid.GetType("Unity.Entities.BakingSettings");
                var settings = Activator.CreateInstance(settingsType);
                settingsType.GetProperty("BlobAssetStore").SetValue(settings, store);
                hybrid.GetType("Unity.Entities.BakingUtility").GetMethod("BakeGameObjects", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { TestWorld, new[] { root }, settings });
                using var catalogs = EntityManager.CreateEntityQuery(typeof(BuildingVisualPrefabEntry));
                using var entries = EntityManager.GetBuffer<BuildingVisualPrefabEntry>(catalogs.GetSingletonEntity()).ToNativeArray(Allocator.Temp);
                Entity grid = CreateTransportGrid();
                EntityManager.AddBuffer<BeltVisualDirtyCell>(grid);
                for (int tier = 1; tier <= 4; tier++)
                {
                    var definition = candidate.buildings.Single(b => b.key == "belt_mk" + tier);
                    Entity template = entries.ToArray().Single(e => e.BuildingDefinition.Value == definition.id).Prefab;
                    Entity source = CreateVisualBelt(new GridCell(2, 0, tier * 3), East, BuildingPortType.Output);
                    Entity target = CreateVisualBelt(new GridCell(3, 0, tier * 3), East, BuildingPortType.Input);
                    Entity sourceModel = EntityManager.Instantiate(template), targetModel = EntityManager.Instantiate(template);
                    EntityManager.SetComponentData(source, new BuildingVisualReference { Value = sourceModel });
                    EntityManager.SetComponentData(target, new BuildingVisualReference { Value = targetModel });
                    var sourceParts = EntityManager.GetComponentData<BeltVisualParts>(sourceModel);
                    var targetParts = EntityManager.GetComponentData<BeltVisualParts>(targetModel);
                    Assert.That(EntityManager.GetBuffer<BeltEdgeRenderEntity>(sourceParts.EastEdge).Length, Is.EqualTo(3),
                        "The actual rail's colored guard, steel panel and bolts use three render entities.");
                    foreach (var renderer in EntityManager.GetBuffer<BeltEdgeRenderEntity>(sourceParts.EastEdge))
                    {
                        Assert.That(EntityManager.HasComponent<MaterialMeshInfo>(renderer.Value), Is.True);
                        Assert.That(EntityManager.HasComponent<Prefab>(renderer.Value), Is.False, "Renderer references must remap to the instantiated belt.");
                    }
                    EntityManager.GetBuffer<BeltVisualDirtyCell>(grid).Add(new BeltVisualDirtyCell { Value = new GridCell(2, 0, tier * 3) });
                    EntityManager.GetBuffer<BeltVisualDirtyCell>(grid).Add(new BeltVisualDirtyCell { Value = new GridCell(3, 0, tier * 3) });
                    var occupancy = GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
                    UpdateSystem(occupancy);
                    var visuals = GetOrCreateManagedSystem<BeltTopologyVisualSystem>();
                    UpdateSystem(visuals);
                    AssertRailHidden(sourceParts.EastEdge, true);
                    AssertRailHidden(targetParts.WestEdge, true);
                    AssertRailHidden(sourceParts.NorthEdge, false);
                    var original = EntityManager.GetComponentData<BeltVisualParts>(template);
                    AssertRailHidden(original.EastEdge, false);

                    EntityManager.DestroyEntity(target);
                    EntityManager.GetBuffer<BeltVisualDirtyCell>(grid).Add(new BeltVisualDirtyCell { Value = new GridCell(2, 0, tier * 3) });
                    UpdateSystem(occupancy); UpdateSystem(visuals);
                    AssertRailHidden(sourceParts.EastEdge, false);
                }
            }
            finally
            {
                TestWorld.Dispose();
                store.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(candidate);
            }
        }

        private void AssertRailHidden(Entity edge, bool hidden)
        {
            Assert.That(EntityManager.HasComponent<DisableRendering>(edge), Is.EqualTo(hidden));
            foreach (var renderer in EntityManager.GetBuffer<BeltEdgeRenderEntity>(edge))
                Assert.That(EntityManager.HasComponent<DisableRendering>(renderer.Value), Is.EqualTo(hidden),
                    "Visibility must include every material submesh of the named edge.");
        }

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
