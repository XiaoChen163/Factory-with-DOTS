using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Factory.Tests
{
    public sealed class BuildTransactionTests :
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
                ConfigureWorldGrid(grid, new int2(128, 16),
                    new WorldGridConfig { CellSize = 1f, LayerHeight = 1f });
                EntityManager.AddBuffer<GridBuildCommand>(grid).Add(
                    new GridBuildCommand
                    {
                        Type = GridBuildCommandType.PlaceBeltPath,
                        BuildingDefinition = new BuildingDefinitionId { Value = 1 },
                        StartCell = new GridCell(int2.zero),
                        EndCell = new GridCell(3, 0, 0),
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
                        BuildingDefinition = new BuildingDefinitionId { Value = 1 },
                        Prefab = visualPrefab
                    });
                EntityManager.AddComponentData(
                    catalog,
                    new FactoryDatabase { Value = database });

                Entity blocker = EntityManager.CreateEntity();
                EntityManager.AddComponentData(blocker, new GridPlacement
                {
                    AnchorCell = new GridCell(2, 0, 0),
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
                new FactoryBuildingDefinitionRow
                {
                    id = 1,
                    key = "belt",
                    kind = BuildingKind.Belt,
                    footprintWidth = 1,
                    footprintHeight = 1,
                    isRectangle = true,
                    visualPrefab = visualObject,
                    beltTier = 1,
                    cellsPerSecond = 1f,
                    ports = System.Array.Empty<FactoryBuildingPortTableRow>()
                }
            };
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
