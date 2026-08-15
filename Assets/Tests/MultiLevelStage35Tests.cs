using System.IO;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Factory.Tests
{
    public sealed class MultiLevelStage35Tests : FactoryWorldFixture
    {
        private BlobAssetReference<FactoryDatabaseBlob> database;
        private Entity grid;
        private SurfaceRegistrySystem registry;
        private GridBuildCommandSystem build;

        public override void SetUpWorld()
        {
            base.SetUpWorld();
            grid = EntityManager.CreateEntity();
            EntityManager.AddComponentData(grid, new GridDefinition
            {
                Size = new int2(32, 32),
                CellSize = 1f,
                Revision = 1
            });
            EntityManager.AddComponentData(grid, new WorldGridConfig
            {
                CellSize = 1f,
                LayerHeight = 1f
            });
            EntityManager.AddComponentData(grid, new InitialSurfaceSettings
            {
                Mode = InitialSurfaceMode.Empty
            });
            EntityManager.AddComponentData(grid, new SurfaceTopologyRevision { Value = 1 });
            EntityManager.AddComponentData(grid, new BuildingOccupancyRevision { Value = 1 });
            EntityManager.AddBuffer<GridBuildCommand>(grid);
            EntityManager.AddBuffer<GridBuildResult>(grid);

            database = CreateFoundationDatabase();
            Entity catalog = EntityManager.CreateEntity();
            EntityManager.AddComponentData(catalog, new BuildingPrefabCatalog());
            EntityManager.AddComponentData(catalog, new FactoryDatabase { Value = database });
            EntityManager.AddBuffer<BuildingVisualPrefabEntry>(catalog);
            EntityManager.AddBuffer<FoundationLevelMaterial>(catalog).Add(
                new FoundationLevelMaterial
                {
                    BuildingLevel = new BuildingLevelId { Value = 1 },
                    VisualMaterialId = 3
                });

            registry = GetOrCreateManagedSystem<SurfaceRegistrySystem>();
            UpdateSystem(registry);
            UpdateSystem(GetOrCreateManagedSystem<GridOccupancyIndexSystem>());
            build = GetOrCreateManagedSystem<GridBuildCommandSystem>();
        }

        public override void TearDownWorld()
        {
            base.TearDownWorld();
            if (database.IsCreated)
                database.Dispose();
        }

        [Test]
        public void EmptyMode_DoesNotCreateLegacyRectangle()
        {
            Assert.That(registry.SurfaceCount, Is.Zero);
            Assert.That(EntityManager.HasComponent<LegacySurfaceInitialized>(grid), Is.False);
        }

        [Test]
        public void FoundationArea_FillsInclusiveRectangleAtArbitraryCoordinates()
        {
            Submit(new GridCell(-2, 0, 4), new GridCell(0, 0, 5));

            GridBuildResult result = EntityManager.GetBuffer<GridBuildResult>(grid)[0];
            Assert.That(result.Success, Is.EqualTo(1));
            Assert.That(result.AffectedCount, Is.EqualTo(6));
            Assert.That(registry.SurfaceCount, Is.EqualTo(6));
            Assert.That(registry.TryGetSurface(new GridCell(-1, 0, 5), out var surface));
            Assert.That(surface.VisualMaterialId, Is.EqualTo(3),
                "The material must come from the level mapping, not the command payload.");
        }

        [Test]
        public void FoundationArea_OverlapRejectsWholeRectangleAtomically()
        {
            Submit(new GridCell(0, 0, 0), new GridCell(0, 0, 0));
            Submit(new GridCell(0, 0, 0), new GridCell(2, 0, 0));

            GridBuildResult result = EntityManager.GetBuffer<GridBuildResult>(grid)[0];
            Assert.That(result.Success, Is.Zero);
            Assert.That(result.AffectedCount, Is.Zero);
            Assert.That(result.FailureReason,
                Is.EqualTo(GridBuildFailureReason.FoundationAlreadyExists));
            Assert.That(registry.SurfaceCount, Is.EqualTo(1));
            Assert.That(registry.HasFoundationVoxel(new GridCell(1, 0, 0)), Is.False);
        }

        [Test]
        public void FoundationArea_CanStackOnExistingFoundationGrid()
        {
            Submit(new GridCell(0, 0, 0), new GridCell(1, 0, 0));
            Submit(new GridCell(0, 1, 0), new GridCell(1, 1, 0));

            GridBuildResult result = EntityManager.GetBuffer<GridBuildResult>(grid)[0];
            Assert.That(result.Success, Is.EqualTo(1));
            Assert.That(result.AffectedCount, Is.EqualTo(2));
            Assert.That(registry.HasFoundationVoxel(new GridCell(0, 1, 0)));
            Assert.That(registry.HasFoundationVoxel(new GridCell(1, 1, 0)));
        }

        [Test]
        public void SingleFoundation_CanBePlacedWithoutLowerSupport()
        {
            EntityManager.GetBuffer<GridBuildCommand>(grid).Add(
                new GridBuildCommand
                {
                    RequestId = 1,
                    Type = GridBuildCommandType.PlaceFoundation,
                    StartCell = new GridCell(3, 4, 5),
                    VisualMaterialId = 3
                });
            UpdateSystem(build);

            GridBuildResult result =
                EntityManager.GetBuffer<GridBuildResult>(grid)[0];
            Assert.That(result.Success, Is.EqualTo(1));
            Assert.That(result.FailureReason,
                Is.EqualTo(GridBuildFailureReason.None));
            Assert.That(registry.HasFoundationVoxel(new GridCell(3, 4, 5)));
            Assert.That(registry.HasFoundationVoxel(new GridCell(3, 3, 5)),
                Is.False);
        }

        [Test]
        public void FoundationArea_AllowsCellsWithoutLowerFoundationSupport()
        {
            Submit(new GridCell(0, 0, 0), new GridCell(0, 0, 0));
            Submit(new GridCell(0, 1, 0), new GridCell(1, 1, 0));

            GridBuildResult result = EntityManager.GetBuffer<GridBuildResult>(grid)[0];
            Assert.That(result.Success, Is.EqualTo(1));
            Assert.That(result.AffectedCount, Is.EqualTo(2));
            Assert.That(result.FailureReason,
                Is.EqualTo(GridBuildFailureReason.None));
            Assert.That(registry.HasFoundationVoxel(new GridCell(0, 1, 0)));
            Assert.That(registry.HasFoundationVoxel(new GridCell(1, 1, 0)));
        }

        [Test]
        public void RemovingFoundation_ThatSupportsUpperFoundationIsAllowed()
        {
            Submit(new GridCell(0, 0, 0), new GridCell(0, 0, 0));
            Submit(new GridCell(0, 1, 0), new GridCell(0, 1, 0));
            EntityManager.GetBuffer<GridBuildCommand>(grid).Add(
                new GridBuildCommand
                {
                    RequestId = 3,
                    Type = GridBuildCommandType.RemoveFoundation,
                    StartCell = new GridCell(0, 0, 0)
                });
            UpdateSystem(build);

            GridBuildResult result =
                EntityManager.GetBuffer<GridBuildResult>(grid)[0];
            Assert.That(result.Success, Is.EqualTo(1));
            Assert.That(result.FailureReason,
                Is.EqualTo(GridBuildFailureReason.None));
            Assert.That(registry.HasFoundationVoxel(new GridCell(0, 0, 0)),
                Is.False);
            Assert.That(registry.HasFoundationVoxel(new GridCell(0, 1, 0)),
                Is.True);
        }

        [Test]
        public void FoundationArea_AllowsUnsupportedLevelButRequiresOnePlane()
        {
            Submit(new GridCell(0, 1, 0), new GridCell(0, 1, 0));
            Assert.That(EntityManager.GetBuffer<GridBuildResult>(grid)[0].Success,
                Is.EqualTo(1));

            Submit(new GridCell(0, 1, 0), new GridCell(0, 2, 0));
            Assert.That(EntityManager.GetBuffer<GridBuildResult>(grid)[0].FailureReason,
                Is.EqualTo(GridBuildFailureReason.FoundationUnsupported));

            Submit(new GridCell(0, 0, 0),
                new GridCell(GridBuildCommandSystem.MaxFoundationAreaCells, 0, 0));
            Assert.That(EntityManager.GetBuffer<GridBuildResult>(grid)[0].FailureReason,
                Is.EqualTo(GridBuildFailureReason.FoundationAreaTooLarge));
            Assert.That(registry.SurfaceCount, Is.EqualTo(1));
        }

        [Test]
        public void FoundationContent_UsesStableIdsLocalizedNameAndPureVisualPrefab()
        {
            string buildings = File.ReadAllText(
                "Assets/Data/FactoryTables/buildings/buildings.csv");
            string levels = File.ReadAllText(
                "Assets/Data/FactoryTables/buildings/levels/building_levels.csv");
            string prefab = File.ReadAllText(
                "Assets/Prefabs/Buildings/Foundation.prefab");
            Assert.That(buildings, Does.StartWith("id,key,"));
            Assert.That(buildings, Does.Contain("7,foundation,"));
            Assert.That(levels, Does.StartWith("id,key,"));
            Assert.That(levels, Does.Contain("10,foundation_mk1,"));
            Assert.That(prefab, Does.Contain("MeshFilter:"));
            Assert.That(prefab, Does.Contain("MeshRenderer:"));
            Assert.That(prefab, Does.Not.Contain("Collider:"));
            Assert.That(prefab, Does.Not.Contain("MonoBehaviour:"));
            Assert.That(File.Exists(
                "Assets/Art/Icons/Buildings/Foundation.png"), Is.True);

            FactoryDatabaseAsset asset =
                ScriptableObject.CreateInstance<FactoryDatabaseAsset>();
            FactoryPresentationCatalog catalog =
                ScriptableObject.CreateInstance<FactoryPresentationCatalog>();
            try
            {
                asset.buildingLevels = new[]
                {
                    new FactoryBuildingLevelTableRow
                    {
                        id = 10,
                        key = "foundation_mk1",
                        nameKey = "building.foundation.mk1"
                    }
                };
                typeof(FactoryPresentationCatalog).GetField(
                    "database",
                    BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(
                    catalog,
                    asset);
                catalog.BuildIndex();
                Assert.That(catalog.GetBuildingName(
                    new BuildingLevelId { Value = 10 }), Is.EqualTo("地基"));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void FormalScene_HasNoLegacyFactoryFloor()
        {
            string scene = File.ReadAllText("Assets/Scenes/Ecs.unity");
            Assert.That(scene, Does.Not.Contain("m_Name: Factory Floor"));
            Assert.That(scene, Does.Not.Contain("m_LocalScale: {x: 32, y: 1, z: 32}"));
        }

        private void Submit(GridCell start, GridCell end)
        {
            EntityManager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand
            {
                RequestId = 1,
                Type = GridBuildCommandType.PlaceFoundationArea,
                Kind = BuildingKind.Foundation,
                BuildingLevel = new BuildingLevelId { Value = 1 },
                StartCell = start,
                EndCell = end,
                VisualMaterialId = 99
            });
            UpdateSystem(build);
        }

        private static BlobAssetReference<FactoryDatabaseBlob>
            CreateFoundationDatabase()
        {
            using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref FactoryDatabaseBlob root = ref
                builder.ConstructRoot<FactoryDatabaseBlob>();
            BlobBuilderArray<FactoryBuildingBlob> buildings =
                builder.Allocate(ref root.BuildingsById, 2);
            buildings[1] = new FactoryBuildingBlob
            {
                Id = new BuildingTypeId { Value = 1 },
                Kind = BuildingKind.Foundation,
                FootprintWidth = 1,
                FootprintHeight = 1
            };
            BlobBuilderArray<FactoryBuildingLevelBlob> levels =
                builder.Allocate(ref root.BuildingLevelsById, 2);
            levels[1] = new FactoryBuildingLevelBlob
            {
                Id = new BuildingLevelId { Value = 1 },
                BuildingId = new BuildingTypeId { Value = 1 },
                Level = 1
            };
            BlobBuilderArray<BuildingLevelId> menu =
                builder.Allocate(ref root.BuildingLevelMenu, 1);
            menu[0] = new BuildingLevelId { Value = 1 };
            builder.Allocate(ref root.ItemsById, 0);
            builder.Allocate(ref root.MachineTypesById, 0);
            builder.Allocate(ref root.BeltLevelsById, 0);
            builder.Allocate(ref root.ProcessorLevelsById, 0);
            builder.Allocate(ref root.StorageLevelsById, 0);
            builder.Allocate(ref root.BuildingPorts, 0);
            builder.Allocate(ref root.Recipes, 0);
            builder.Allocate(ref root.Inputs, 0);
            builder.Allocate(ref root.Outputs, 0);
            builder.Allocate(ref root.RecipeRangesByMachine, 0);
            return builder.CreateBlobAssetReference<FactoryDatabaseBlob>(
                Allocator.Persistent);
        }
    }
}
