using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Factory.Tests
{
    public sealed class MultiLevelStage0BaselineTests
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
            GridDefinition grid = new GridDefinition
            {
                Origin = new float3(-3.5f, 4f, 8.25f),
                CellSize = 2.5f,
                Size = new int2(64, 64)
            };
            GridCell cell = new GridCell(x, 0, z);

            float3 world = EcsGridUtility.CellToWorldCenter(cell, 9f, grid);

            Assert.That(EcsGridUtility.WorldToCell(world, grid), Is.EqualTo(cell));
            Assert.That(world.y, Is.EqualTo(9f));
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
                AnchorCell = new int2(10, 20),
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
                    AnchorCell = new int2(5, 7),
                    FootprintSize = new int2(1, 1),
                    QuarterTurns = quarterTurns
                };
                Assert.That(
                    EcsGridUtility.GetBuildingCell(placement, int2.zero),
                    Is.EqualTo(placement.AnchorCell));
            }
        }

        [Test]
        public void RectangularGrid_ContainsIncludesOriginAndExcludesEveryBoundary()
        {
            GridDefinition grid = new GridDefinition { Size = new int2(4, 3) };

            Assert.That(EcsGridUtility.Contains(grid, new int2(0, 0)), Is.True);
            Assert.That(EcsGridUtility.Contains(grid, new int2(3, 2)), Is.True);
            Assert.That(EcsGridUtility.Contains(grid, new int2(-1, 0)), Is.False);
            Assert.That(EcsGridUtility.Contains(grid, new int2(0, -1)), Is.False);
            Assert.That(EcsGridUtility.Contains(grid, new int2(4, 0)), Is.False);
            Assert.That(EcsGridUtility.Contains(grid, new int2(0, 3)), Is.False);
        }

        [Test]
        public void BeltPath_HorizontalFirstFreezesXPriorityCornerAndDirections()
        {
            List<BeltPathCell> path = EcsGridUtility.BuildBeltPath(
                new int2(1, 2),
                new int2(4, 4),
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
                new int2(1, 2),
                new int2(4, 4),
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
                        Cell = int2.zero,
                        Direction = new int2(1, 0)
                    },
                    new BeltTopology
                    {
                        Cell = new int2(1, 0),
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

    public sealed class MultiLevelStage1AddressTests
    {
        [Test]
        public void GridCell_LevelParticipatesInEqualityAndHashing()
        {
            GridCell ground = new GridCell(7, 0, -3);
            GridCell upper = new GridCell(7, 1, -3);
            HashSet<GridCell> cells = new HashSet<GridCell>
            {
                ground,
                upper
            };

            Assert.That(ground, Is.Not.EqualTo(upper));
            Assert.That(cells.Count, Is.EqualTo(2));
        }

        [Test]
        public void WorldGridConfig_ConvertsAllThreeAddressAxes()
        {
            WorldGridConfig grid = new WorldGridConfig
            {
                Origin = new float3(-4f, 10f, 6f),
                CellSize = 2f,
                LayerHeight = 3f
            };
            GridCell cell = new GridCell(5, -2, -7);

            float3 world = EcsGridUtility.CellToWorldCenter(cell, grid);

            Assert.That(world, Is.EqualTo(new float3(7f, 4f, -7f)));
            Assert.That(EcsGridUtility.WorldToCell(world, grid),
                Is.EqualTo(cell));
        }

        [Test]
        public void LegacyRectangularGrid_AcceptsOnlyLevelZero()
        {
            GridDefinition grid = new GridDefinition
            {
                Size = new int2(8, 8)
            };

            Assert.That(EcsGridUtility.Contains(
                grid,
                new GridCell(3, 0, 4)), Is.True);
            Assert.That(EcsGridUtility.Contains(
                grid,
                new GridCell(3, 1, 4)), Is.False);
        }

        [Test]
        public void BeltPath_CrossLevelEndpointsAreRejected()
        {
            List<BeltPathCell> path = EcsGridUtility.BuildBeltPath(
                new GridCell(0, 0, 0),
                new GridCell(3, 1, 0),
                true,
                new int2(1, 0));

            Assert.That(path, Is.Empty);
        }

        [Test]
        public void TransportResolver_DoesNotConnectSameHorizontalCellsAcrossLevels()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity item = scenario.CreateItem();
            scenario.AddBelt(
                new GridCell(0, 0, 0),
                new int2(1, 0),
                item,
                1f);
            scenario.AddBelt(
                new GridCell(1, 1, 0),
                new int2(1, 0));

            TransportTickResult result = scenario.ResolveTickLinear();

            Assert.That(result.ReadyRequestCount, Is.Zero);
            Assert.That(result.AcceptedTransferCount, Is.Zero);
        }

        [Test]
        public void BuildingRuntimeIds_AreMonotonicAndIndependentOfCell()
        {
            BuildingRuntimeIdAllocator allocator =
                new BuildingRuntimeIdAllocator
                {
                    NextValue = 0x8000000000000000UL
                };

            ulong first = allocator.Allocate();
            ulong second = allocator.Allocate();

            Assert.That(second, Is.EqualTo(first + 1));
        }
    }

    public sealed class MultiLevelStage0BuildTransactionTests :
        FactoryWorldFixture
    {
        [Test]
        public void BeltPath_WhenAnyCellIsOccupied_RollsBackEntirePath()
        {
            BlobAssetReference<FactoryDatabaseBlob> database = default;
            GameObject visualObject = null;
            FactoryDatabaseAsset asset = null;
            try
            {
                database = CreateBeltDatabase(out visualObject, out asset);
                Entity grid = EntityManager.CreateEntity();
                EntityManager.AddComponentData(grid, new GridDefinition
                {
                    Size = new int2(128, 16),
                    CellSize = 1f,
                    Revision = 1
                });
                EntityManager.AddBuffer<GridBuildCommand>(grid).Add(
                    new GridBuildCommand
                    {
                        Type = GridBuildCommandType.PlaceBeltPath,
                        BuildingLevel = new BuildingLevelId { Value = 1 },
                        StartCell = int2.zero,
                        EndCell = new int2(3, 0),
                        HorizontalFirst = 1
                    });
                EntityManager.AddBuffer<GridBuildResult>(grid);

                Entity visualPrefab = EntityManager.CreateEntity(
                    typeof(Prefab),
                    typeof(LocalTransform));
                EntityManager.SetComponentData(
                    visualPrefab,
                    LocalTransform.Identity);
                Entity catalog = EntityManager.CreateEntity();
                EntityManager.AddComponentData(
                    catalog,
                    new BuildingPrefabCatalog());
                EntityManager.AddBuffer<BuildingVisualPrefabEntry>(catalog).Add(
                    new BuildingVisualPrefabEntry
                    {
                        BuildingLevel = new BuildingLevelId { Value = 1 },
                        Prefab = visualPrefab
                    });
                EntityManager.AddComponentData(
                    catalog,
                    new FactoryDatabase { Value = database });

                Entity blocker = EntityManager.CreateEntity();
                EntityManager.AddComponentData(blocker, new GridPlacement
                {
                    AnchorCell = new int2(2, 0),
                    FootprintSize = new int2(1, 1),
                    Kind = BuildingKind.Storage
                });
                EntityManager.AddBuffer<OccupiedCellOffset>(blocker).Add(
                    new OccupiedCellOffset { Value = int2.zero });
                EntityManager.AddBuffer<BuildingPort>(blocker);

                // Far-away placements must remain outside the command batch's
                // materialized working set.
                for (int i = 0; i < 64; i++)
                {
                    Entity far = EntityManager.CreateEntity();
                    EntityManager.AddComponentData(far, new GridPlacement
                    {
                        AnchorCell = new GridCell(32 + i, 0, 7),
                        FootprintSize = new int2(1, 1),
                        Kind = BuildingKind.Storage
                    });
                    EntityManager.AddBuffer<OccupiedCellOffset>(far).Add(
                        new OccupiedCellOffset { Value = int2.zero });
                    EntityManager.AddBuffer<BuildingPort>(far);
                }

                GridOccupancyIndexSystem occupancy =
                    GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
                UpdateSystem(occupancy);
                GridBuildCommandSystem build =
                    GetOrCreateManagedSystem<GridBuildCommandSystem>();
                UpdateSystem(build);

                DynamicBuffer<GridBuildResult> results =
                    EntityManager.GetBuffer<GridBuildResult>(grid);
                Assert.That(results.Length, Is.EqualTo(1));
                Assert.That(results[0].Success, Is.Zero);
                Assert.That(results[0].FailureReason,
                    Is.EqualTo(GridBuildFailureReason.CellOccupied));
                Assert.That(EntityManager.Exists(blocker), Is.True);

                EntityQuery placements = EntityManager.CreateEntityQuery(
                    ComponentType.ReadOnly<GridPlacement>());
                Assert.That(placements.CalculateEntityCount(), Is.EqualTo(65));
                placements.Dispose();
                Assert.That(build.LastBatchPlacementScanCount, Is.EqualTo(1));
                Assert.That(build.LastBatchTemporaryRecordCount, Is.EqualTo(4));
                Assert.That(build.LastBatchPathValidationCellCount, Is.EqualTo(3));
            }
            finally
            {
                if (database.IsCreated)
                    database.Dispose();
                if (visualObject != null)
                    Object.DestroyImmediate(visualObject);
                if (asset != null)
                    Object.DestroyImmediate(asset);
            }
        }

        private static BlobAssetReference<FactoryDatabaseBlob>
            CreateBeltDatabase(
                out GameObject visualObject,
                out FactoryDatabaseAsset asset)
        {
            visualObject = new GameObject("Stage0 Belt Visual");
            asset = ScriptableObject.CreateInstance<FactoryDatabaseAsset>();
            asset.items = new[]
            {
                new FactoryItemTableRow
                {
                    id = 1,
                    key = "item",
                    nameKey = "item",
                    maxStack = 100,
                    category = FactoryItemCategory.Ore
                }
            };
            asset.machineTypes = new[]
            {
                new FactoryMachineTypeTableRow { id = 1, key = "machine" }
            };
            asset.buildings = new[]
            {
                new FactoryBuildingTableRow
                {
                    id = 1,
                    key = "belt",
                    nameKey = "belt",
                    behavior = FactoryBuildingBehavior.Belt,
                    kind = BuildingKind.Belt,
                    footprintWidth = 1,
                    footprintHeight = 1,
                    ports = System.Array.Empty<FactoryBuildingPortTableRow>()
                }
            };
            asset.buildingLevels = new[]
            {
                new FactoryBuildingLevelTableRow
                {
                    id = 1,
                    key = "belt_mk1",
                    buildingKey = "belt",
                    buildingId = 1,
                    level = 1,
                    nameKey = "belt_mk1",
                    visualPrefab = visualObject
                }
            };
            asset.beltLevels = new[]
            {
                new FactoryBeltLevelTableRow
                {
                    buildingLevelKey = "belt_mk1",
                    buildingLevelId = 1,
                    cellsPerSecond = 1f
                }
            };
            asset.processorLevels =
                System.Array.Empty<FactoryProcessorLevelTableRow>();
            asset.storageLevels =
                System.Array.Empty<FactoryStorageLevelTableRow>();
            asset.recipes = new[]
            {
                new FactoryRecipeTableRow
                {
                    id = 1,
                    key = "noop",
                    machineTypeKey = "machine",
                    machineTypeId = 1,
                    durationSeconds = 1f,
                    inputs = new[]
                    {
                        new FactoryRecipeIngredientTableRow
                        {
                            itemKey = "item", itemId = 1, count = 1
                        }
                    },
                    outputs = new[]
                    {
                        new FactoryRecipeIngredientTableRow
                        {
                            itemKey = "item", itemId = 1, count = 1
                        }
                    }
                }
            };

            if (!FactoryDatabaseBakingUtility.TryBuild(
                    asset,
                    null,
                    out BlobAssetReference<FactoryDatabaseBlob> result))
            {
                throw new System.InvalidOperationException(
                    "Failed to build the Stage 0 belt database.");
            }

            return result;
        }
    }
}
