using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Factory.Tests
{
    public sealed class Phase5OptimizationTests : FactoryWorldFixture
    {
        private static readonly int2 East = new int2(1, 0);

        [Test]
        public void PortBufferSwap_TogglesGenerationWithoutCopyingBuffers()
        {
            Entity owner = CreatePortOwner(new GridPlacement
            {
                AnchorCell = int2.zero,
                FootprintSize = new int2(1, 1),
                Kind = BuildingKind.Storage
            });
            EntityManager.GetBuffer<ItemInputPortCurrent>(owner).Add(
                new ItemInputPortCurrent
                {
                    Value = new ItemInputPortSnapshot
                    {
                        PortIndex = 0,
                        AppliedTransferCount = 42
                    }
                });
            EntityManager.GetBuffer<ItemInputPortNext>(owner).Add(
                new ItemInputPortNext
                {
                    Value = new ItemInputPortSnapshot
                    {
                        PortIndex = 1,
                        AppliedTransferCount = 7
                    }
                });

            SystemHandle swap = TestWorld.GetOrCreateSystem<
                ItemPortBufferSwapSystem>();
            swap.Update(TestWorld.Unmanaged);
            EntityManager.CompleteAllTrackedJobs();

            ItemPortBufferGeneration generation =
                EntityManager.GetComponentData<ItemPortBufferGeneration>(
                    owner);
            Assert.That(generation.Value, Is.EqualTo(1));

            DynamicBuffer<ItemInputPortCurrent> current =
                EntityManager.GetBuffer<ItemInputPortCurrent>(owner);
            DynamicBuffer<ItemInputPortNext> next =
                EntityManager.GetBuffer<ItemInputPortNext>(owner);
            Assert.That(current.Length, Is.EqualTo(1));
            Assert.That(current[0].Value.PortIndex, Is.Zero);
            Assert.That(current[0].Value.AppliedTransferCount, Is.EqualTo(42));
            Assert.That(next.Length, Is.EqualTo(1));
            Assert.That(next[0].Value.PortIndex, Is.EqualTo(1));
            Assert.That(next[0].Value.AppliedTransferCount, Is.EqualTo(7));
        }

        [Test]
        public void BuildingInput_ReservedCountPersistsInPortSnapshot()
        {
            CreateGrid();
            Entity item = CreateItem(1);
            Entity belt = CreateBelt(
                new int2(0, 0),
                East,
                item,
                1f);
            Entity owner = CreatePortOwner(new GridPlacement
            {
                AnchorCell = int2.zero,
                FootprintSize = new int2(1, 1),
                Kind = BuildingKind.Storage
            });
            EntityManager.GetBuffer<BuildingPort>(owner).Add(
                new BuildingPort
                {
                    CellOffset = int2.zero,
                    Direction = East,
                    Type = BuildingPortType.Input,
                    Index = 0
                });
            EntityManager.GetBuffer<ItemInputPortCurrent>(owner).Add(
                new ItemInputPortCurrent
                {
                    Value = new ItemInputPortSnapshot
                    {
                        AcceptedItemType = new ItemId { Value = 1 },
                        FreeCapacity = 1,
                        PortIndex = 0,
                        Enabled = 1,
                        FilterMode = ItemPortFilterMode.ExactItemType
                    }
                });

            UpdateTransferTick();

            DynamicBuffer<ItemInputPortCurrent> ports =
                EntityManager.GetBuffer<ItemInputPortCurrent>(owner);
            Assert.That(ports.Length, Is.EqualTo(1));
            Assert.That(
                ports[0].Value.ReservedTransferCount,
                Is.EqualTo(1));
            Assert.That(
                ports[0].Value.AppliedTransferCount,
                Is.Zero);
            Assert.That(
                EntityManager.GetComponentData<BeltState>(belt).CurrentItem,
                Is.EqualTo(Entity.Null));
        }

        [Test]
        public void PendingOccupancyAdd_IsAppliedIncrementally()
        {
            Entity grid = CreateGrid();
            Entity building = EntityManager.CreateEntity();
            EntityManager.AddComponentData(
                building,
                new PendingOccupancyAdd());
            EntityManager.AddComponentData(
                building,
                new GridPlacement
                {
                    AnchorCell = new int2(2, 3),
                    FootprintSize = new int2(1, 1),
                    Kind = BuildingKind.Belt
                });
            EntityManager.AddBuffer<OccupiedCellOffset>(building).Add(
                new OccupiedCellOffset { Value = int2.zero });

            GridOccupancyIndexSystem occupancy =
                GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
            UpdateSystem(occupancy);

            Assert.That(occupancy.IsReady, Is.True);
            Assert.That(
                occupancy.TryGetOccupant(
                    new int2(2, 3),
                    out Entity occupant),
                Is.True);
            Assert.That(occupant, Is.EqualTo(building));
            Assert.That(
                EntityManager.HasComponent<PendingOccupancyAdd>(building),
                Is.False);
            Assert.That(
                EntityManager.GetComponentData<GridDefinition>(grid)
                    .Revision,
                Is.EqualTo(1));
        }

        [Test]
        public void BeltVisualDirtyBuffer_IsClearedAfterLocalRefresh()
        {
            Entity grid = CreateGrid();
            EntityManager.AddBuffer<BeltVisualDirtyCell>(grid).Add(
                new BeltVisualDirtyCell
                {
                    Value = new int2(5, 5)
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
            Entity grid = CreateGrid();
            EntityManager.AddBuffer<BeltVisualDirtyCell>(grid).Add(
                new BeltVisualDirtyCell
                {
                    Value = new int2(1, 0)
                });

            CreateVisualBelt(
                new int2(0, 0),
                East,
                BuildingPortType.Output);
            Entity target = CreateVisualBelt(
                new int2(1, 0),
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
                occupancy.TryGetOccupant(new int2(0, 0), out _),
                Is.True);
            Assert.That(
                occupancy.TryGetOccupant(new int2(1, 0), out _),
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
        public void RemovingBelt_ReturnsItemToPoolAndHidesRendering()
        {
            BlobAssetReference<FactoryDatabaseBlob> database = default;
            try
            {
                database = CreateDatabase();
                Entity grid = CreateBuildGrid();

                Entity catalog = EntityManager.CreateEntity();
                EntityManager.AddComponentData(
                    catalog,
                    new BuildingPrefabCatalog());
                EntityManager.AddBuffer<BuildingVisualPrefabEntry>(
                    catalog);
                EntityManager.AddComponentData(
                    catalog,
                    new FactoryDatabase
                    {
                        Value = database
                    });

                Entity pool = EntityManager.CreateEntity(
                    typeof(ItemPool),
                    typeof(ItemPoolEntry));
                EntityManager.SetComponentData(
                    pool,
                    new ItemPool
                    {
                        ItemType = new ItemId { Value = 1 },
                        FreeCursor = 0
                    });

                Entity item = CreateItem(1);
                Entity belt = EntityManager.CreateEntity();
                EntityManager.AddComponentData(
                    belt,
                    new BeltState
                    {
                        CurrentItem = item
                    });
                EntityManager.AddComponentData(
                    belt,
                    new GridPlacement
                    {
                        AnchorCell = int2.zero,
                        FootprintSize = new int2(1, 1),
                        QuarterTurns = 0,
                        Kind = BuildingKind.Belt
                    });
                EntityManager.AddBuffer<OccupiedCellOffset>(belt).Add(
                    new OccupiedCellOffset
                    {
                        Value = int2.zero
                    });
                EntityManager.AddBuffer<BuildingPort>(belt);

                GridOccupancyIndexSystem occupancy =
                    GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
                UpdateSystem(occupancy);

                EntityManager.GetBuffer<GridBuildCommand>(grid).Add(
                    new GridBuildCommand
                    {
                        Type = GridBuildCommandType.Remove,
                        StartCell = int2.zero
                    });
                UpdateSystem(
                    GetOrCreateManagedSystem<GridBuildCommandSystem>());

                Assert.That(EntityManager.Exists(item), Is.True);
                Assert.That(
                    EntityManager.IsComponentEnabled<Item>(item),
                    Is.False);
                Assert.That(
                    EntityManager.HasComponent<DisableRendering>(item),
                    Is.True);
                DynamicBuffer<ItemPoolEntry> entries =
                    EntityManager.GetBuffer<ItemPoolEntry>(pool);
                Assert.That(entries.Length, Is.EqualTo(1));
                Assert.That(entries[0].Entity, Is.EqualTo(item));
                Assert.That(EntityManager.Exists(belt), Is.False);
            }
            finally
            {
                if (database.IsCreated)
                {
                    database.Dispose();
                }
            }
        }

        [Test]
        public void StorageAdapter_GenerationOneWritesCurrentBuffersWithoutSafetyError()
        {
            BlobAssetReference<FactoryDatabaseBlob> database = default;
            try
            {
                database = CreateDatabase();
                Entity databaseEntity = EntityManager.CreateEntity();
                EntityManager.AddComponentData(
                    databaseEntity,
                    new FactoryDatabase { Value = database });

                Entity owner = CreatePortOwner(new GridPlacement
                {
                    AnchorCell = int2.zero,
                    FootprintSize = new int2(1, 1),
                    Kind = BuildingKind.Storage
                });
                EntityManager.AddComponentData(
                    owner,
                    new StorageState { Capacity = 10 });
                EntityManager.AddBuffer<StoredItemCount>(owner);
                EntityManager.GetBuffer<BuildingPort>(owner).Add(
                    new BuildingPort
                    {
                        CellOffset = int2.zero,
                        Direction = int2.zero,
                        Type = BuildingPortType.Input,
                        Index = 0
                    });
                EntityManager.SetComponentData(
                    owner,
                    new ItemPortBufferGeneration { Value = 1 });
                EntityManager.GetBuffer<ItemInputPortNext>(owner).Add(
                    new ItemInputPortNext
                    {
                        Value = new ItemInputPortSnapshot
                        {
                            PortIndex = 0,
                            Enabled = 1,
                            FilterMode = ItemPortFilterMode.Any
                        }
                    });

                SystemHandle adapter = TestWorld.GetOrCreateSystem<
                    ItemPortAdapterSystem>();
                adapter.Update(TestWorld.Unmanaged);
                EntityManager.CompleteAllTrackedJobs();

                DynamicBuffer<ItemInputPortCurrent> current =
                    EntityManager.GetBuffer<ItemInputPortCurrent>(owner);
                Assert.That(current.Length, Is.EqualTo(1));
                Assert.That(current[0].Value.PortIndex, Is.Zero);
            }
            finally
            {
                if (database.IsCreated)
                {
                    database.Dispose();
                }
            }
        }

        private static BlobAssetReference<FactoryDatabaseBlob> CreateDatabase()
        {
            FactoryDatabaseAsset asset =
                ScriptableObject.CreateInstance<FactoryDatabaseAsset>();
            GameObject visualPrefab = new GameObject("Storage Visual");
            asset.items = new[]
            {
                new FactoryItemTableRow
                {
                    id = 1,
                    key = "iron_ore",
                    nameKey = "iron_ore",
                    maxStack = 1,
                    category = FactoryItemCategory.Ore
                }
            };
            asset.machineTypes = new[]
            {
                new FactoryMachineTypeTableRow
                {
                    id = 1,
                    key = "furnace"
                }
            };
            asset.buildings = new[]
            {
                new FactoryBuildingTableRow
                {
                    id = 1,
                    key = "storage",
                    nameKey = "storage",
                    behavior = FactoryBuildingBehavior.Storage,
                    kind = BuildingKind.Storage,
                    footprintWidth = 1,
                    footprintHeight = 1,
                    ports = new[]
                    {
                        new FactoryBuildingPortTableRow
                        {
                            type = BuildingPortType.Input,
                            index = 0,
                            cellOffset = Vector2Int.zero,
                            direction = Vector2Int.right
                        }
                    }
                }
            };
            asset.buildingLevels = new[]
            {
                new FactoryBuildingLevelTableRow
                {
                    id = 1,
                    key = "storage_mk1",
                    buildingKey = "storage",
                    buildingId = 1,
                    level = 1,
                    nameKey = "storage_mk1",
                    visualPrefab = visualPrefab,
                    menuOrder = 0
                }
            };
            asset.beltLevels = System.Array.Empty<
                FactoryBeltLevelTableRow>();
            asset.processorLevels = System.Array.Empty<
                FactoryProcessorLevelTableRow>();
            asset.storageLevels = new[]
            {
                new FactoryStorageLevelTableRow
                {
                    buildingLevelKey = "storage_mk1",
                    buildingLevelId = 1,
                    capacity = 10
                }
            };
            asset.recipes = new[]
            {
                new FactoryRecipeTableRow
                {
                    id = 1,
                    key = "smelt",
                    machineTypeKey = "furnace",
                    machineTypeId = 1,
                    durationSeconds = 1f,
                    inputs = new[]
                    {
                        new FactoryRecipeIngredientTableRow
                        {
                            itemKey = "iron_ore",
                            itemId = 1,
                            count = 1
                        }
                    },
                    outputs = new[]
                    {
                        new FactoryRecipeIngredientTableRow
                        {
                            itemKey = "iron_ingot",
                            itemId = 1,
                            count = 1
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
                    "Failed to build minimal factory database.");
            }

            Object.DestroyImmediate(visualPrefab);
            Object.DestroyImmediate(asset);
            return result;
        }

        private Entity CreateGrid()
        {
            Entity grid = EntityManager.CreateEntity();
            EntityManager.AddComponentData(grid, new GridDefinition
            {
                Size = new int2(16, 16),
                CellSize = 1f,
                Revision = 1
            });
            return grid;
        }

        private Entity CreateBuildGrid()
        {
            Entity grid = CreateGrid();
            EntityManager.AddBuffer<GridBuildCommand>(grid);
            EntityManager.AddBuffer<GridBuildResult>(grid);
            return grid;
        }

        private Entity CreateVisualBelt(
            int2 cell,
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
