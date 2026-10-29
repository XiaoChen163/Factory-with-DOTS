using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Factory.Tests
{
    public sealed class BeltTransferSystemTests : FactoryWorldFixture
    {
        private static readonly int2 East = new int2(1, 0);

        [Test]
        public void BuildingInput_ConsumesReadyBeltItemAndPublishesReceipt()
        {
            CreateGrid();
            Entity item = CreateItem();
            Entity belt = CreateBelt(
                new GridCell(0, 0, 0),
                East,
                item,
                1f);
            Entity owner = CreatePortOwner(new GridPlacement
            {
                AnchorCell = new GridCell(0, 0, 0),
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

            Assert.That(
                EntityManager.GetComponentData<BeltState>(belt).CurrentItem,
                Is.EqualTo(Entity.Null));
            Assert.That(EntityManager.Exists(item), Is.False);
            DynamicBuffer<ItemTransferReceiptNext> receipts =
                EntityManager.GetBuffer<ItemTransferReceiptNext>(owner);
            Assert.That(receipts.Length, Is.EqualTo(1));
            Assert.That(
                receipts[0].Value.Kind,
                Is.EqualTo(ItemTransferReceiptKind.InputAccepted));
        }

        [Test]
        public void BuildingOutput_InstantiatesPrefabWithPreloadedItemComponent()
        {
            CreateGrid();
            CreateItemCatalog();
            Entity belt = CreateBelt(new GridCell(0, 0, 0), East);
            Entity owner = CreateOutputOwner(new GridCell(0, 0, 0));

            UpdateTransferTick();

            Entity item =
                EntityManager.GetComponentData<BeltState>(belt).CurrentItem;
            Assert.That(item, Is.Not.EqualTo(Entity.Null));
            Assert.That(EntityManager.HasComponent<Item>(item), Is.True);
            Assert.That(
                EntityManager.GetComponentData<Item>(item).ItemType,
                Is.EqualTo(new ItemId { Value = 1 }));
            Assert.That(EntityManager.HasComponent<Prefab>(item), Is.False);
            Assert.That(
                EntityManager.GetBuffer<ItemTransferReceiptNext>(owner)
                    .Length,
                Is.EqualTo(1));
        }

        [Test]
        public void BuildingOutput_StackedAvailabilityTransfersOnlyOneItemPerTick()
        {
            CreateGrid();
            CreateItemCatalog();
            Entity belt = CreateBelt(new GridCell(0, 0, 0), East);
            Entity owner = CreateOutputOwner(new GridCell(0, 0, 0), 64);

            UpdateTransferTick();

            Assert.That(
                EntityManager.GetComponentData<BeltState>(belt).CurrentItem,
                Is.Not.EqualTo(Entity.Null));
            DynamicBuffer<ItemTransferReceiptNext> receipts =
                EntityManager.GetBuffer<ItemTransferReceiptNext>(owner);
            Assert.That(receipts.Length, Is.EqualTo(1));
            Assert.That(receipts[0].Value.Count, Is.EqualTo(1));
        }

        [Test]
        public void BuildingOutput_WhenItemPrefabMissingItemComponent_StillInjectsItem()
        {
            CreateGrid();
            CreateItemCatalog(
                includeItemComponent: false,
                includeLocalTransform: true);
            Entity belt = CreateBelt(new GridCell(0, 0, 0), East);
            Entity owner = CreateOutputOwner(new GridCell(0, 0, 0));

            UpdateTransferTick();

            Entity item =
                EntityManager.GetComponentData<BeltState>(belt).CurrentItem;
            Assert.That(item, Is.Not.EqualTo(Entity.Null));
            Assert.That(EntityManager.HasComponent<Item>(item), Is.True);
            Assert.That(
                EntityManager.GetComponentData<Item>(item).ItemType,
                Is.EqualTo(new ItemId { Value = 1 }));
            Assert.That(
                EntityManager.HasComponent<LocalTransform>(item),
                Is.True);
            Assert.That(
                EntityManager.GetComponentData<LocalTransform>(item).Position,
                Is.EqualTo(new float3(0.5f, 0.535f, 0.5f)));
            Assert.That(
                EntityManager.GetBuffer<ItemTransferReceiptNext>(owner)
                    .Length,
                Is.EqualTo(1));
            Assert.That(
                EntityManager.GetBuffer<ItemTransferReceiptNext>(owner)[0]
                    .Value.Kind,
                Is.EqualTo(ItemTransferReceiptKind.OutputTransferred));
        }

        [Test]
        public void BuildingOutput_WhenItemPrefabMissingLocalTransform_AddsTransform()
        {
            CreateGrid();
            CreateItemCatalog(
                includeItemComponent: false,
                includeLocalTransform: false);
            Entity belt = CreateBelt(new GridCell(0, 0, 0), East);
            Entity owner = CreateOutputOwner(new GridCell(0, 0, 0));

            UpdateTransferTick();

            Entity item =
                EntityManager.GetComponentData<BeltState>(belt).CurrentItem;
            Assert.That(item, Is.Not.EqualTo(Entity.Null));
            Assert.That(EntityManager.HasComponent<Item>(item), Is.True);
            Assert.That(
                EntityManager.HasComponent<LocalTransform>(item),
                Is.True);
            Assert.That(
                EntityManager.GetComponentData<LocalTransform>(item).Position,
                Is.EqualTo(new float3(0.5f, 0.535f, 0.5f)));
            Assert.That(
                EntityManager.GetBuffer<ItemTransferReceiptNext>(owner)
                    .Length,
                Is.EqualTo(1));
        }

        [Test]
        public void PortOwnerOrder_RebuildsOnlyAfterOccupancyRevisionChanges()
        {
            Entity grid = CreateGrid();
            CreateItemCatalog();
            CreatePortOwner(new GridPlacement
            {
                AnchorCell = new GridCell(5, 0, 5),
                FootprintSize = new int2(1, 1),
                Kind = BuildingKind.Storage
            });
            UpdateTransferTick();

            Entity belt = CreateBelt(new GridCell(0, 0, 0), East);
            CreateOutputOwner(new GridCell(0, 0, 0));
            EntityManager.AddComponentData(grid, new TransportTopologyRevision { Value = 1 });
            UpdateTransferTick();
            Assert.That(
                EntityManager.GetComponentData<BeltState>(belt).CurrentItem,
                Is.EqualTo(Entity.Null));

            BuildingOccupancyRevision definition =
                EntityManager.GetComponentData<BuildingOccupancyRevision>(grid);
            definition.Value++;
            EntityManager.SetComponentData(grid, definition);
            UpdateTransferTick();
            Assert.That(
                EntityManager.GetComponentData<BeltState>(belt).CurrentItem,
                Is.Not.EqualTo(Entity.Null));
        }

        [Test]
        public void Belt_IsSplitIntoTopologyAndStateComponents()
        {
            Entity belt = CreateBelt(new GridCell(int2.zero), East);

            Assert.That(
                EntityManager.HasComponent<BeltTopology>(belt),
                Is.True);
            Assert.That(
                EntityManager.HasComponent<BeltState>(belt),
                Is.True);
            Assert.That(
                EntityManager.GetComponentData<BeltTopology>(belt).Cell,
                Is.EqualTo(GridCell.LevelZero(int2.zero)));
            Assert.That(
                EntityManager.GetComponentData<BeltTopology>(belt).Direction,
                Is.EqualTo(East));
            Assert.That(
                EntityManager.GetComponentData<BeltState>(belt).CurrentItem,
                Is.EqualTo(Entity.Null));
        }

        [Test]
        public void TransferTick_MovesReadyItemIntoEmptyDownstreamBelt()
        {
            CreateGrid(withOccupancyRevision: false);
            Entity item = CreateItem(1);
            Entity source = CreateBelt(new GridCell(0, 0, 0), East, item, 1f);
            Entity target = CreateBelt(new GridCell(1, 0, 0), East);

            UpdateTransferTick();

            Assert.That(
                EntityManager.GetComponentData<BeltState>(source)
                    .CurrentItem,
                Is.EqualTo(Entity.Null));
            Assert.That(
                EntityManager.GetComponentData<BeltState>(target)
                    .CurrentItem,
                Is.EqualTo(item));
        }

        [Test]
        public void MultipleTransferTicks_AdvanceChainWithoutLosingItems()
        {
            CreateGrid(withOccupancyRevision: false);
            Entity itemA = CreateItem(1);
            Entity itemB = CreateItem(2);
            Entity belt0 = CreateBelt(new GridCell(0, 0, 0), East, itemA, 1f);
            Entity belt1 = CreateBelt(new GridCell(1, 0, 0), East, itemB, 1f);
            Entity belt2 = CreateBelt(new GridCell(2, 0, 0), East);
            Entity belt3 = CreateBelt(new GridCell(3, 0, 0), East);

            for (int tick = 0; tick < 3; tick++)
            {
                UpdateTransferTick();
            }

            Assert.That(
                EntityManager.GetComponentData<BeltState>(belt0)
                    .CurrentItem,
                Is.EqualTo(Entity.Null));
            Assert.That(
                EntityManager.GetComponentData<BeltState>(belt1)
                    .CurrentItem,
                Is.EqualTo(Entity.Null));
            Assert.That(
                EntityManager.GetComponentData<BeltState>(belt2)
                    .CurrentItem,
                Is.EqualTo(itemA));
            Assert.That(
                EntityManager.GetComponentData<BeltState>(belt3)
                    .CurrentItem,
                Is.EqualTo(itemB));
            Assert.That(EntityManager.Exists(itemA), Is.True);
            Assert.That(EntityManager.Exists(itemB), Is.True);
        }

        [Test]
        public void TransferTick_WritesStatsFromArbitrationJob()
        {
            CreateGrid(withOccupancyRevision: false);
            CreateItem();
            CreateBelt(new GridCell(0, 0, 0), East);
            CreateBelt(new GridCell(1, 0, 0), East);

            UpdateTransferTick();

            EntityQuery statsQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<Stage3SimulationStats>());
            Stage3SimulationStats stats =
                statsQuery.GetSingleton<Stage3SimulationStats>();
            statsQuery.Dispose();
            Assert.That(stats.BeltCount, Is.EqualTo(2));
            Assert.That(stats.TickCount, Is.EqualTo(1));
        }

        [Test]
        public void BuildingOutput_ItemExistsOnlyAfterTransferEcbPlayback()
        {
            CreateGrid(withOccupancyRevision: false);
            CreateItemCatalog(includeLocalTransform: true);
            Entity belt = CreateBelt(new GridCell(0, 0, 0), East);
            Entity owner = CreateOutputOwner(new GridCell(0, 0, 0));

            UpdateSystem(GetOrCreateManagedSystem<BeltTransferSystem>());

            Entity item =
                EntityManager.GetComponentData<BeltState>(belt).CurrentItem;
            Assert.That(item, Is.Not.EqualTo(Entity.Null));
            Assert.That(
                item.Index,
                Is.EqualTo(-1),
                "Item is still a deferred ECB entity before playback.");

            UpdateSystem(
                GetOrCreateManagedSystem<TransferCommandBufferSystem>());
            Entity realized =
                EntityManager.GetComponentData<BeltState>(belt).CurrentItem;
            Assert.That(realized, Is.Not.EqualTo(Entity.Null));
            Assert.That(
                realized.Index,
                Is.Not.EqualTo(-1),
                "Item must be a realized entity after ECB playback.");
            Assert.That(EntityManager.Exists(realized), Is.True);
            Assert.That(
                EntityManager.HasComponent<Item>(realized),
                Is.True);
            Assert.That(
                EntityManager.GetBuffer<ItemTransferReceiptNext>(owner)
                    .Length,
                Is.EqualTo(1));
        }

        private Entity CreateGrid(bool withOccupancyRevision = true)
        {
            Entity grid = EntityManager.CreateEntity();
            ConfigureWorldGrid(grid, new int2(16, 16), new WorldGridConfig { CellSize = 1f, LayerHeight = 1f });
            if (withOccupancyRevision)
                EntityManager.AddComponentData(grid, new BuildingOccupancyRevision { Value = 1 });
            return grid;
        }

        private void CreateItemCatalog(
            bool includeItemComponent = true,
            bool includeLocalTransform = false)
        {
            Entity prefab = EntityManager.CreateEntity(typeof(Prefab));
            if (includeItemComponent)
            {
                EntityManager.AddComponentData(prefab, new Item
                {
                    ItemType = new ItemId { Value = 1 }
                });
            }
            if (includeLocalTransform)
            {
                EntityManager.AddComponentData(
                    prefab,
                    LocalTransform.FromPosition(float3.zero));
            }
            Entity catalog = EntityManager.CreateEntity(
                typeof(BuildingPrefabCatalog));
            EntityManager.AddBuffer<ItemPrefabEntry>(catalog).Add(
                new ItemPrefabEntry
                {
                    ItemType = new ItemId { Value = 1 },
                    Prefab = prefab
                });
        }

        private Entity CreateOutputOwner(GridCell targetCell, int availableCount = 1)
        {
            Entity owner = CreatePortOwner(new GridPlacement
            {
                AnchorCell = targetCell,
                FootprintSize = new int2(1, 1),
                Kind = BuildingKind.Storage
            });
            EntityManager.GetBuffer<BuildingPort>(owner).Add(
                new BuildingPort
                {
                    CellOffset = int2.zero,
                    Direction = East,
                    Type = BuildingPortType.Output,
                    Index = 0
                });
            EntityManager.GetBuffer<ItemOutputPortCurrent>(owner).Add(
                new ItemOutputPortCurrent
                {
                    Value = new ItemOutputPortSnapshot
                    {
                        ItemType = new ItemId { Value = 1 },
                        AvailableCount = availableCount,
                        PortIndex = 0,
                        Enabled = 1
                    }
                });
            return owner;
        }
    }
}
