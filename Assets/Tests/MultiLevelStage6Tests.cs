using System.IO;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;
using UnityEditor;
using Unity.Mathematics;
using UnityEngine;

namespace Factory.Tests
{
    public sealed class MultiLevelStage6Tests : FactoryWorldFixture
    {
        private static readonly int2 East = new int2(1, 0);

        [Test]
        public void RampImportedMesh_VisualRotationMapsHighEdgeToUphillDirection()
        {
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(
                "Assets/Art/Mesh/Ramp_45_1x1x1.obj");
            Assert.That(mesh, Is.Not.Null);
            float negativeXMaxY = float.MinValue;
            float positiveXMaxY = float.MinValue;
            foreach (Vector3 vertex in mesh.vertices)
            {
                if (vertex.x < 0f)
                    negativeXMaxY = math.max(negativeXMaxY, vertex.y);
                else if (vertex.x > 0f)
                    positiveXMaxY = math.max(positiveXMaxY, vertex.y);
            }

            Assert.That(negativeXMaxY, Is.GreaterThan(positiveXMaxY),
                "Unity's imported ramp mesh has its high edge on local -X.");

            int2[] directions =
            {
                new int2(1, 0),
                new int2(0, 1),
                new int2(-1, 0),
                new int2(0, -1)
            };
            foreach (int2 direction in directions)
            {
                float3 worldHighAxis = math.rotate(
                    RampUtility.GetFoundationVisualRotation(direction),
                    new float3(-1f, 0f, 0f));
                Assert.That(worldHighAxis.x, Is.EqualTo(direction.x).Within(0.001f));
                Assert.That(worldHighAxis.z, Is.EqualTo(direction.y).Within(0.001f));
            }
        }

        [Test]
        public void RampCatalog_ContainsOnlyRequestedSlopeBuildOptionsAndIcons()
        {
            string buildings = File.ReadAllText(
                "Assets/Data/FactoryTables/buildings/buildings.csv");
            string levels = File.ReadAllText(
                "Assets/Data/FactoryTables/buildings/levels/building_levels.csv");
            Assert.That(buildings, Does.Contain("8,ramp_foundation_1_1,"));
            Assert.That(buildings, Does.Contain("9,ramp_foundation_1_2,"));
            Assert.That(buildings, Does.Contain("10,ramp_foundation_1_4,"));
            Assert.That(levels, Does.Contain("ramp_foundation,51,8"));
            Assert.That(levels, Does.Contain("ramp_foundation_half,52,4"));
            Assert.That(levels, Does.Contain("ramp_foundation_quarter,53,2"));
            Assert.That(levels, Does.Not.Contain("ramp_foundation_1_8"));
            Assert.That(File.Exists(
                "Assets/Art/Icons/Buildings/RampFoundation.png"), Is.True);
            Assert.That(File.Exists(
                "Assets/Art/Icons/Buildings/RampFoundationHalf.png"), Is.True);
            Assert.That(File.Exists(
                "Assets/Art/Icons/Buildings/RampFoundationQuarter.png"), Is.True);
        }

        [TestCase(2)]
        [TestCase(4)]
        [TestCase(8)]
        public void SupportedRampSlopes_UseExactHeightUnits(int rise)
        {
            Assert.That(RampUtility.IsAllowedRise(rise), Is.True);
            Assert.That(RampUtility.IsAllowedRise(-rise), Is.True);
        }

        [Test]
        public void FractionalRampEndpoints_ChainWithoutFloatRounding()
        {
            RampConnector first = Connector(new GridCell(1, 0, 0), 0, 2);
            RampConnector second = Connector(new GridCell(2, 0, 0), 2, 4);

            Assert.That(
                RampUtility.GetHighEndpoint(first),
                Is.EqualTo(RampUtility.GetLowEndpoint(second)));
            Assert.That(first.HighHeight.IsWholeLevel, Is.False);
        }

        [Test]
        public void RampLine_SnapsToOneAxisAndAccumulatesFractionalHeight()
        {
            List<GridCell> cells = new List<GridCell>();
            List<int2> directions = new List<int2>();
            List<int> heights = new List<int>();

            EcsGridInteractionController.BuildRampLine(
                new GridCell(3, 2, 4),
                new GridCell(6, 9, 5),
                true,
                East,
                4,
                cells,
                directions,
                heights);

            Assert.That(cells, Has.Count.EqualTo(4));
            Assert.That(cells[0], Is.EqualTo(new GridCell(3, 2, 4)));
            Assert.That(cells[1], Is.EqualTo(new GridCell(4, 2, 4)));
            Assert.That(cells[2], Is.EqualTo(new GridCell(5, 3, 4)));
            Assert.That(cells[3], Is.EqualTo(new GridCell(6, 3, 4)));
            Assert.That(heights, Is.EqualTo(new[] { 16, 20, 24, 28 }));
            Assert.That(directions, Has.All.EqualTo(East));
        }

        [Test]
        public void QuarterSlopeChain_ConnectsPlanarBeltsAtBothEnds()
        {
            Entity grid = EntityManager.CreateEntity();
            EntityManager.AddComponentData(grid,
                new TransportTopologyRevision { Value = 1 });
            DynamicBuffer<TransportExplicitEdge> edges =
                EntityManager.AddBuffer<TransportExplicitEdge>(grid);
            Entity source = CreateBelt(new GridCell(0, 0, 0), East);
            Entity previous = source;

            for (int i = 0; i < 4; i++)
            {
                RampConnector connector = Connector(
                    new GridCell(i + 1, 0, 0), i * 2, (i + 1) * 2);
                Entity foundation = EntityManager.CreateEntity();
                EntityManager.AddComponentData(foundation, connector);
                Entity belt = CreateBelt(new GridCell(i + 1, 0, 0), East);
                BeltTopology topology = EntityManager.GetComponentData<BeltTopology>(belt);
                topology.ConnectionMode = RampUtility.GetConnectionMode(
                    connector.Cell,
                    connector.LowHeight,
                    connector.HighHeight);
                EntityManager.SetComponentData(belt, topology);
                EntityManager.AddComponentData(belt, new RampBelt
                {
                    Connector = foundation,
                    TravelDirection = East,
                    EntryHeight = connector.LowHeight,
                    ExitHeight = connector.HighHeight
                });
                previous = belt;
            }

            Entity target = CreateBelt(new GridCell(5, 1, 0), East);
            UpdateSystem(GetOrCreateManagedSystem<RampTopologyConnectionSystem>());

            edges = EntityManager.GetBuffer<TransportExplicitEdge>(grid);
            Assert.That(edges.Length, Is.EqualTo(1));
            Assert.That(edges[0].Source, Is.EqualTo(previous));
            Assert.That(edges[0].Target, Is.EqualTo(target));
            for (int i = 0; i < edges.Length; i++)
                Assert.That(edges[i].Generator, Is.EqualTo(1));
        }

        [Test]
        public void RampBeltDirection_MustFollowOrReverseSlopeAxis()
        {
            RampConnector connector = Connector(new GridCell(0, 0, 0), 0, 4);
            Assert.That(RampUtility.IsTravelDirectionAllowed(connector, East),
                Is.True);
            Assert.That(RampUtility.IsTravelDirectionAllowed(connector, -East),
                Is.True);
            Assert.That(RampUtility.IsTravelDirectionAllowed(
                connector, new int2(0, 1)), Is.False);
        }

        [Test]
        public void RampBeltPath_RequiresCompleteCollinearUniformSlope()
        {
            RampRegistrySystem registry =
                GetOrCreateManagedSystem<RampRegistrySystem>();
            registry.Register(Entity.Null,
                Connector(new GridCell(0, 0, 0), 0, 2));
            registry.Register(Entity.Null,
                Connector(new GridCell(1, 0, 0), 2, 4));
            registry.Register(Entity.Null,
                Connector(new GridCell(2, 0, 0), 4, 6));
            List<RampRegistrySystem.Record> path =
                new List<RampRegistrySystem.Record>();

            Assert.That(registry.TryBuildBeltPath(
                new GridCell(0, 0, 0),
                new GridCell(2, 0, 0),
                East,
                path,
                out GridBuildFailureReason failure), Is.True);
            Assert.That(failure, Is.EqualTo(GridBuildFailureReason.None));
            Assert.That(path, Has.Count.EqualTo(3));

            registry.Register(Entity.Null,
                Connector(new GridCell(10, 0, 0), 0, 2));
            registry.Register(Entity.Null,
                Connector(new GridCell(12, 0, 0), 4, 6));
            Assert.That(registry.TryBuildBeltPath(
                new GridCell(10, 0, 0),
                new GridCell(12, 0, 0), East, path, out failure), Is.False);
            Assert.That(failure,
                Is.EqualTo(GridBuildFailureReason.RampPathIncomplete));

            registry.Register(Entity.Null,
                Connector(new GridCell(20, 0, 0), 0, 2));
            registry.Register(Entity.Null,
                Connector(new GridCell(21, 0, 0), 2, 6));
            Assert.That(registry.TryBuildBeltPath(
                new GridCell(20, 0, 0),
                new GridCell(21, 0, 0), East, path, out failure), Is.False);
            Assert.That(failure,
                Is.EqualTo(GridBuildFailureReason.RampPathSlopeMismatch));

            registry.Register(Entity.Null,
                Connector(new GridCell(1, 0, 1), 2, 4));
            Assert.That(registry.TryBuildBeltPath(
                new GridCell(0, 0, 0),
                new GridCell(1, 0, 1), East, path, out failure), Is.False);
            Assert.That(failure,
                Is.EqualTo(GridBuildFailureReason.RampPathNotCollinear));
        }

        [Test]
        public void RampConnectionMode_UsesExplicitEdgeOnlyAtLevelBoundary()
        {
            Assert.That(RampUtility.GetConnectionMode(
                    new GridCell(0, 0, 0),
                    new GridHeight(0),
                    new GridHeight(2)),
                Is.EqualTo(TransportConnectionMode.Planar));
            Assert.That(RampUtility.GetConnectionMode(
                    new GridCell(0, 0, 0),
                    new GridHeight(6),
                    new GridHeight(8)),
                Is.EqualTo(TransportConnectionMode.PlanarInputOnly));
            Assert.That(RampUtility.GetConnectionMode(
                    new GridCell(0, 0, 0),
                    new GridHeight(8),
                    new GridHeight(6)),
                Is.EqualTo(TransportConnectionMode.PlanarOutputOnly));
        }

        [Test]
        public void RampBelt_IsExcludedFromPlanarOccupancyIndex()
        {
            Entity grid = EntityManager.CreateEntity();
            EntityManager.AddComponentData(grid, new GridDefinition
            {
                Size = new int2(4, 4),
                CellSize = 1f,
                Revision = 1
            });
            Entity rampBelt = EntityManager.CreateEntity();
            EntityManager.AddComponentData(rampBelt, new GridPlacement
            {
                AnchorCell = new GridCell(20, 9, 9),
                FootprintSize = new int2(1, 1),
                Kind = BuildingKind.Belt
            });
            EntityManager.AddBuffer<OccupiedCellOffset>(rampBelt).Add(
                new OccupiedCellOffset { Value = int2.zero });
            EntityManager.AddComponentData(rampBelt, new RampBelt());
            EntityManager.AddComponent<PendingOccupancyAdd>(rampBelt);

            GridOccupancyIndexSystem occupancy =
                GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
            UpdateSystem(occupancy);

            Assert.That(occupancy.IsReady, Is.True);
            Assert.That(occupancy.ConflictCount, Is.Zero);
            Assert.That(occupancy.OccupiedCellCount, Is.Zero);
            Assert.That(EntityManager.HasComponent<PendingOccupancyAdd>(rampBelt),
                Is.True,
                "Ramp belts must not enter the planar pending-add channel.");
        }

        private static RampConnector Connector(
            GridCell cell,
            int lowUnits,
            int highUnits) => new RampConnector
        {
            Cell = cell,
            LowHeight = new GridHeight(lowUnits),
            HighHeight = new GridHeight(highUnits),
            UphillDirection = East
        };
    }
}
