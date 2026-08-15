using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;

namespace Factory.Tests
{
    public sealed class MultiLevelStage4Tests : FactoryWorldFixture
    {
        [TestCase(-1024)]
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(1024)]
        public void LayerHeight_RoundTripsPositiveAndNegativeLevels(int level)
        {
            WorldGridConfig config = new WorldGridConfig
            {
                Origin = new float3(-3f, 0f, 7f),
                CellSize = 2f,
                LayerHeight = 1.25f
            };

            float worldY = EcsGridUtility.LevelToWorldY(level, config);

            Assert.That(EcsGridUtility.WorldYToLevel(worldY, config),
                Is.EqualTo(level));
            Assert.That(EcsGridUtility.CellToWorldCenter(
                    new GridCell(2, level, -4),
                    config).y,
                Is.EqualTo(worldY));
        }

        [TestCase(-1, -1)]
        [TestCase(0, 0)]
        [TestCase(7, 0)]
        [TestCase(8, 1)]
        [TestCase(-8, -1)]
        [TestCase(-9, -2)]
        public void PhysicsLevelBand_UsesFloorDivision(
            int level,
            int expectedBand)
        {
            FoundationPhysicsChunkKey key =
                SurfaceChunkUtility.GetPhysicsChunkKey(
                    new GridCell(0, level, 0));
            Assert.That(key.LevelBand, Is.EqualTo(expectedBand));
        }

        [Test]
        public void Occupancy_AllowsSameHorizontalCellOnDifferentLevels()
        {
            Entity grid = CreateGrid();
            SurfaceRegistrySystem registry =
                GetOrCreateManagedSystem<SurfaceRegistrySystem>();
            UpdateSystem(registry);
            registry.AddFoundation(
                grid,
                new GridCell(2, 0, 3),
                0,
                SurfacePermission.All,
                true);
            registry.AddFoundation(
                grid,
                new GridCell(2, 1, 3),
                0,
                SurfacePermission.All,
                true);

            Entity lower = CreatePlacement(new GridCell(2, 0, 3));
            Entity upper = CreatePlacement(new GridCell(2, 1, 3));
            GridOccupancyIndexSystem occupancy =
                GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
            UpdateSystem(occupancy);

            Assert.That(occupancy.ConflictCount, Is.Zero);
            Assert.That(occupancy.TryGetOccupant(
                new GridCell(2, 0, 3), out Entity lowerOwner));
            Assert.That(occupancy.TryGetOccupant(
                new GridCell(2, 1, 3), out Entity upperOwner));
            Assert.That(lowerOwner, Is.EqualTo(lower));
            Assert.That(upperOwner, Is.EqualTo(upper));
        }

        [Test]
        public void VerticalFoundationChange_DirtiesAdjacentRenderLayers()
        {
            Entity grid = CreateGrid();
            SurfaceRegistrySystem registry =
                GetOrCreateManagedSystem<SurfaceRegistrySystem>();
            UpdateSystem(registry);
            EntityManager.GetBuffer<SurfaceRenderDirtyChunk>(grid).Clear();
            EntityManager.GetBuffer<SurfacePhysicsDirtyChunk>(grid).Clear();

            registry.AddFoundation(
                grid,
                new GridCell(4, 1, 5),
                0,
                SurfacePermission.All,
                true);

            DynamicBuffer<SurfaceRenderDirtyChunk> renderDirty =
                EntityManager.GetBuffer<SurfaceRenderDirtyChunk>(grid);
            Assert.That(Contains(renderDirty, new SurfaceChunkKey(0, 0, 0)));
            Assert.That(Contains(renderDirty, new SurfaceChunkKey(0, 1, 0)));
            Assert.That(Contains(renderDirty, new SurfaceChunkKey(0, 2, 0)));
            DynamicBuffer<SurfacePhysicsDirtyChunk> physicsDirty =
                EntityManager.GetBuffer<SurfacePhysicsDirtyChunk>(grid);
            Assert.That(physicsDirty.Length, Is.EqualTo(1));
            Assert.That(physicsDirty[0].Value,
                Is.EqualTo(new FoundationPhysicsChunkKey(0, 0, 0)));
        }

        [Test]
        public void PhysicsBand_MergesVerticallyStackedFoundationsIntoOneBody()
        {
            Entity grid = CreateGrid();
            SurfaceRegistrySystem registry =
                GetOrCreateManagedSystem<SurfaceRegistrySystem>();
            UpdateSystem(registry);
            registry.AddFoundation(grid, new GridCell(0, 0, 0), 0,
                SurfacePermission.All, true);
            registry.AddFoundation(grid, new GridCell(0, 1, 0), 0,
                SurfacePermission.All, true);

            FoundationPhysicsChunkSystem physics =
                GetOrCreateManagedSystem<FoundationPhysicsChunkSystem>();
            UpdateSystem(physics);

            EntityQuery query = EntityManager.CreateEntityQuery(
                typeof(FoundationPhysicsChunk),
                typeof(PhysicsCollider));
            Assert.That(query.CalculateEntityCount(), Is.EqualTo(1));
            FoundationPhysicsChunk chunk =
                query.GetSingleton<FoundationPhysicsChunk>();
            Assert.That(chunk.Key,
                Is.EqualTo(new FoundationPhysicsChunkKey(0, 0, 0)));
            Assert.That(physics.ActiveColliderBlobCount, Is.EqualTo(1));
            query.Dispose();
        }

        [Test]
        public void CompoundRaycast_SelectsFirstActualFoundationSurface()
        {
            BlobAssetReference<Collider> collider =
                FoundationCompoundColliderBuilder.Build(
                    new[]
                    {
                        new FoundationBox(new int3(0, -1, 0), new int3(1)),
                        new FoundationBox(new int3(0, 0, 0), new int3(1))
                    },
                    FoundationCollisionCategories.FoundationFilter,
                    Unity.Physics.Material.Default);
            try
            {
                RaycastInput input = new RaycastInput
                {
                    Start = new float3(0.5f, 3f, 0.5f),
                    End = new float3(0.5f, -2f, 0.5f),
                    Filter = FoundationCollisionCategories.FoundationQueryFilter
                };
                Assert.That(collider.Value.CastRay(input, out RaycastHit hit));
                GridCell cell = FoundationQueryUtility.ResolveVoxelCell(
                    hit.Position,
                    hit.SurfaceNormal,
                    new WorldGridConfig
                    {
                        CellSize = 1f,
                        LayerHeight = 1f
                    });
                Assert.That(cell, Is.EqualTo(new GridCell(0, 1, 0)));
                Assert.That(hit.SurfaceNormal.y, Is.GreaterThan(0.99f));
            }
            finally
            {
                if (collider.IsCreated)
                    collider.Dispose();
            }
        }

        [Test]
        public void BuildCommands_PlaceSameHorizontalCellOnTwoLevels()
        {
            BlobAssetReference<FactoryDatabaseBlob> database =
                CreateSplitterDatabase();
            try
            {
                Entity grid = CreateGrid();
                SurfaceRegistrySystem registry =
                    GetOrCreateManagedSystem<SurfaceRegistrySystem>();
                UpdateSystem(registry);
                registry.AddFoundation(grid, new GridCell(1, 0, 1), 0,
                    SurfacePermission.All, true);
                registry.AddFoundation(grid, new GridCell(1, 1, 1), 0,
                    SurfacePermission.All, true);

                Entity prefab = EntityManager.CreateEntity(
                    typeof(Prefab),
                    typeof(LocalTransform));
                Entity catalog = EntityManager.CreateEntity();
                EntityManager.AddComponentData(catalog,
                    new BuildingPrefabCatalog());
                EntityManager.AddComponentData(catalog,
                    new FactoryDatabase { Value = database });
                EntityManager.AddBuffer<BuildingVisualPrefabEntry>(catalog).Add(
                    new BuildingVisualPrefabEntry
                    {
                        BuildingLevel = new BuildingLevelId { Value = 1 },
                        Prefab = prefab
                    });

                GridOccupancyIndexSystem occupancy =
                    GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
                UpdateSystem(occupancy);
                GridBuildCommandSystem build =
                    GetOrCreateManagedSystem<GridBuildCommandSystem>();
                PlaceBuilding(grid, build, new GridCell(1, 0, 1));
                UpdateSystem(occupancy);
                PlaceBuilding(grid, build, new GridCell(1, 1, 1));
                UpdateSystem(occupancy);

                Assert.That(occupancy.ConflictCount, Is.Zero);
                Assert.That(occupancy.TryGetOccupant(
                    new GridCell(1, 0, 1), out Entity lower));
                Assert.That(occupancy.TryGetOccupant(
                    new GridCell(1, 1, 1), out Entity upper));
                Assert.That(lower, Is.Not.EqualTo(upper));
                Assert.That(EntityManager.GetComponentData<LocalTransform>(lower)
                    .Position.y, Is.EqualTo(0f));
                Assert.That(EntityManager.GetComponentData<LocalTransform>(upper)
                    .Position.y, Is.EqualTo(1f));
            }
            finally
            {
                if (database.IsCreated)
                    database.Dispose();
            }
        }

        [Test]
        public void DirtyPlacementTransform_UsesAnchorLevelHeight()
        {
            CreateGrid(new WorldGridConfig
            {
                Origin = new float3(10f, 0f, 20f),
                CellSize = 2f,
                LayerHeight = 3f
            });
            Entity building = EntityManager.CreateEntity();
            EntityManager.AddComponentData(building, new GridPlacement
            {
                AnchorCell = new GridCell(2, 2, 3),
                FootprintSize = new int2(1, 1),
                Kind = BuildingKind.Storage
            });
            EntityManager.AddComponentData(
                building,
                LocalTransform.FromPosition(float3.zero));
            EntityManager.AddComponent<GridTransformDirty>(building);

            SystemHandle system =
                TestWorld.GetOrCreateSystem<GridPlacementTransformSystem>();
            system.Update(TestWorld.Unmanaged);
            EntityManager.CompleteAllTrackedJobs();

            Assert.That(EntityManager.GetComponentData<LocalTransform>(building)
                .Position, Is.EqualTo(new float3(15f, 6f, 27f)));
        }

        [Test]
        public void ItemVisualCapture_UsesTransportCellLevel()
        {
            CreateGrid(new WorldGridConfig
            {
                CellSize = 1f,
                LayerHeight = 2f
            });
            Entity item = CreateItem();
            EntityManager.AddComponentData(item, new ItemVisualState());
            CreateBelt(new GridCell(3, 2, 4), new int2(1, 0), item, 1f);

            SystemHandle capture =
                TestWorld.GetOrCreateSystem<ItemVisualStateCaptureSystem>();
            capture.Update(TestWorld.Unmanaged);
            EntityManager.CompleteAllTrackedJobs();

            ItemVisualState state =
                EntityManager.GetComponentData<ItemVisualState>(item);
            Assert.That(state.ToPosition,
                Is.EqualTo(new float3(3.5f, 4.535f, 4.5f)));
        }

        [Test]
        public void LayerVisibility_HidesChunksOutsideSelectedLevel()
        {
            Entity grid = CreateGrid();
            EntityManager.AddComponentData(grid, new GridLayerViewState
            {
                SelectedLevel = 1,
                PickingMode = GridLayerPickingMode.SelectedLevel,
                VisibilityMode = GridLayerVisibilityMode.SelectedOnly,
                VisibleLevelRadius = 0,
                Revision = 2
            });
            Entity lower = EntityManager.CreateEntity();
            EntityManager.AddComponentData(lower, new FoundationRenderChunk
            {
                Key = new SurfaceChunkKey(0, 0, 0)
            });
            Entity upper = EntityManager.CreateEntity();
            EntityManager.AddComponentData(upper, new FoundationRenderChunk
            {
                Key = new SurfaceChunkKey(0, 1, 0)
            });

            UpdateSystem(GetOrCreateManagedSystem<SurfaceChunkVisibilitySystem>());

            Assert.That(EntityManager.HasComponent<DisableRendering>(lower));
            Assert.That(EntityManager.HasComponent<DisableRendering>(upper),
                Is.False);
        }

        private Entity CreateGrid(WorldGridConfig? configured = null)
        {
            WorldGridConfig config = configured ?? new WorldGridConfig
            {
                CellSize = 1f,
                LayerHeight = 1f
            };
            Entity grid = EntityManager.CreateEntity();
            EntityManager.AddComponentData(grid, new GridDefinition
            {
                Size = new int2(16, 16),
                Origin = config.Origin,
                CellSize = config.CellSize,
                Revision = 1
            });
            EntityManager.AddComponentData(grid, config);
            EntityManager.AddComponentData(grid, new InitialSurfaceSettings
            {
                Mode = InitialSurfaceMode.Empty
            });
            EntityManager.AddComponentData(grid,
                new SurfaceTopologyRevision { Value = 1 });
            EntityManager.AddComponentData(grid,
                new BuildingOccupancyRevision { Value = 1 });
            EntityManager.AddBuffer<GridBuildCommand>(grid);
            EntityManager.AddBuffer<GridBuildResult>(grid);
            return grid;
        }

        private Entity CreatePlacement(GridCell cell)
        {
            Entity entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, new GridPlacement
            {
                AnchorCell = cell,
                FootprintSize = new int2(1, 1),
                Kind = BuildingKind.Storage
            });
            EntityManager.AddBuffer<OccupiedCellOffset>(entity).Add(
                new OccupiedCellOffset { Value = int2.zero });
            return entity;
        }

        private void PlaceBuilding(
            Entity grid,
            GridBuildCommandSystem build,
            GridCell cell)
        {
            EntityManager.GetBuffer<GridBuildCommand>(grid).Add(
                new GridBuildCommand
                {
                    RequestId = (uint)(cell.Level + 1),
                    Type = GridBuildCommandType.Place,
                    Kind = BuildingKind.Splitter,
                    BuildingLevel = new BuildingLevelId { Value = 1 },
                    StartCell = cell,
                    EndCell = cell
                });
            UpdateSystem(build);
            Assert.That(EntityManager.GetBuffer<GridBuildResult>(grid)[0].Success,
                Is.EqualTo(1));
        }

        private static BlobAssetReference<FactoryDatabaseBlob>
            CreateSplitterDatabase()
        {
            using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref FactoryDatabaseBlob root = ref
                builder.ConstructRoot<FactoryDatabaseBlob>();
            BlobBuilderArray<FactoryBuildingBlob> buildings =
                builder.Allocate(ref root.BuildingsById, 2);
            buildings[1] = new FactoryBuildingBlob
            {
                Id = new BuildingTypeId { Value = 1 },
                Kind = BuildingKind.Splitter,
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

        private static bool Contains(
            DynamicBuffer<SurfaceRenderDirtyChunk> buffer,
            SurfaceChunkKey key)
        {
            for (int i = 0; i < buffer.Length; i++)
                if (buffer[i].Value == key)
                    return true;
            return false;
        }
    }
}
