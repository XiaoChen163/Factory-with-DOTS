using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Factory.Tests
{
    public sealed class GridGeometryTests
    {
        [TestCase(0.0f, 0.0f, 0, 0)]
        [TestCase(0.999f, 0.999f, 0, 0)]
        [TestCase(1.0f, 1.0f, 1, 1)]
        [TestCase(-0.001f, -0.001f, -1, -1)]
        [TestCase(-1.0f, -1.0f, -1, -1)]
        public void WorldToCell_DefaultGridUsesFloorSemantics(
            float x,
            float z,
            int expectedX,
            int expectedZ)
        {
            Assert.That(
                EcsGridUtility.WorldToCell(new float3(x, 17f, z)),
                Is.EqualTo(new GridCell(expectedX, 0, expectedZ)));
        }

        [TestCase(0, 0)]
        [TestCase(7, 11)]
        [TestCase(-1, -1)]
        [TestCase(-17, 31)]
        public void WorldAndCellConversion_RoundTripsCellCenters(int x, int z)
        {
            WorldGridConfig grid = new WorldGridConfig
            {
                Origin = new float3(-3.5f, 4f, 8.25f),
                CellSize = 2.5f,
                LayerHeight = 1f
            };
            GridCell cell = new GridCell(x, 0, z);

            float3 world = EcsGridUtility.CellToWorldCenter(cell, grid);

            Assert.That(EcsGridUtility.WorldToCell(world, grid), Is.EqualTo(cell));
            Assert.That(world.y, Is.EqualTo(4f));
        }

        [TestCase(0, 2, 3)]
        [TestCase(1, 3, 2)]
        [TestCase(2, 2, 3)]
        [TestCase(3, 3, 2)]
        public void MultiCellFootprint_FourRotationsPreserveEveryCell(
            byte quarterTurns,
            int expectedWidth,
            int expectedHeight)
        {
            GridPlacement placement = new GridPlacement
            {
                AnchorCell = new GridCell(10, 0, 20),
                FootprintSize = new int2(2, 3),
                QuarterTurns = quarterTurns,
                Kind = BuildingKind.Storage
            };
            HashSet<GridCell> cells = new HashSet<GridCell>();
            for (int z = 0; z < 3; z++)
            for (int x = 0; x < 2; x++)
            {
                cells.Add(EcsGridUtility.GetBuildingCell(
                    placement,
                    new int2(x, z)));
            }

            Assert.That(cells.Count, Is.EqualTo(6));
            Assert.That(Max(cells, true) - Min(cells, true) + 1,
                Is.EqualTo(expectedWidth));
            Assert.That(Max(cells, false) - Min(cells, false) + 1,
                Is.EqualTo(expectedHeight));
        }

        [Test]
        public void SingleCellFootprint_IsInvariantUnderRotation()
        {
            for (byte quarterTurns = 0; quarterTurns < 4; quarterTurns++)
            {
                GridPlacement placement = new GridPlacement
                {
                    AnchorCell = new GridCell(5, 0, 7),
                    FootprintSize = new int2(1, 1),
                    QuarterTurns = quarterTurns
                };
                Assert.That(
                    EcsGridUtility.GetBuildingCell(placement, int2.zero),
                    Is.EqualTo(placement.AnchorCell));
            }
        }

        [Test]
        public void BeltPath_HorizontalFirstFreezesXPriorityCornerAndDirections()
        {
            List<BeltPathCell> path = EcsGridUtility.BuildBeltPath(
                new GridCell(1, 0, 2),
                new GridCell(4, 0, 4),
                true,
                new int2(-1, 0));

            AssertPath(path, new[]
            {
                new int2(1, 2), new int2(2, 2), new int2(3, 2),
                new int2(4, 2), new int2(4, 3), new int2(4, 4)
            });
            Assert.That(path[3].Direction, Is.EqualTo(new int2(0, 1)));
            Assert.That(path[5].Direction, Is.EqualTo(new int2(0, 1)));
        }

        [Test]
        public void BeltPath_VerticalFirstFreezesZPriorityCornerAndDirections()
        {
            List<BeltPathCell> path = EcsGridUtility.BuildBeltPath(
                new GridCell(1, 0, 2),
                new GridCell(4, 0, 4),
                false,
                new int2(-1, 0));

            AssertPath(path, new[]
            {
                new int2(1, 2), new int2(1, 3), new int2(1, 4),
                new int2(2, 4), new int2(3, 4), new int2(4, 4)
            });
            Assert.That(path[2].Direction, Is.EqualTo(new int2(1, 0)));
            Assert.That(path[5].Direction, Is.EqualTo(new int2(1, 0)));
        }

        [Test]
        public void TransportTopologyDiagnostics_RecordRebuildNodesAndDuration()
        {
            using FactoryLinearTransferResolver resolver =
                new FactoryLinearTransferResolver();
            using NativeArray<Entity> entities =
                new NativeArray<Entity>(new[]
                {
                    new Entity { Index = 1, Version = 1 },
                    new Entity { Index = 2, Version = 1 }
                }, Allocator.Temp);
            using NativeArray<BeltTopology> belts =
                new NativeArray<BeltTopology>(new[]
                {
                    new BeltTopology
                    {
                        Cell = new GridCell(int2.zero),
                        Direction = new int2(1, 0)
                    },
                    new BeltTopology
                    {
                        Cell = new GridCell(1, 0, 0),
                        Direction = new int2(1, 0)
                    }
                }, Allocator.Temp);
            using NativeArray<Entity> emptyEntities =
                new NativeArray<Entity>(0, Allocator.Temp);
            using NativeArray<Merger> mergers =
                new NativeArray<Merger>(0, Allocator.Temp);
            using NativeArray<Splitter> splitters =
                new NativeArray<Splitter>(0, Allocator.Temp);
            resolver.EnsureTopology(
                1, entities, belts, emptyEntities, mergers,
                emptyEntities, splitters);

            Assert.That(resolver.TopologyRebuildCount, Is.EqualTo(1));
            Assert.That(resolver.NodeCount, Is.EqualTo(2));
            Assert.That(resolver.LastTopologyRebuildNodeCount, Is.EqualTo(2));
            Assert.That(resolver.LastTopologyRebuildMilliseconds,
                Is.GreaterThanOrEqualTo(0.0));
            Assert.That(resolver.TotalTopologyRebuildMilliseconds,
                Is.GreaterThanOrEqualTo(resolver.LastTopologyRebuildMilliseconds));
        }

        private static void AssertPath(
            IReadOnlyList<BeltPathCell> actual,
            IReadOnlyList<int2> expected)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.That(actual[i].Cell,
                    Is.EqualTo(GridCell.LevelZero(expected[i])),
                    "Path cell " + i);
            }
        }

        private static int Min(IEnumerable<GridCell> cells, bool xAxis)
        {
            int result = int.MaxValue;
            foreach (GridCell cell in cells)
                result = math.min(result, xAxis ? cell.X : cell.Z);
            return result;
        }

        private static int Max(IEnumerable<GridCell> cells, bool xAxis)
        {
            int result = int.MinValue;
            foreach (GridCell cell in cells)
                result = math.max(result, xAxis ? cell.X : cell.Z);
            return result;
        }
    }
}
