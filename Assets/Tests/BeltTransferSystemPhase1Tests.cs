using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Factory.Tests
{
    public sealed class BeltTransferSystemPhase1Tests : FactoryWorldFixture
    {
        private static readonly int2 East = new int2(1, 0);

        [Test]
        public void BuildingInput_ConsumesReadyBeltItemAndPublishesReceipt()
        {
            CreateGrid();
            Entity item = CreateItem();
            Entity belt = CreateBelt(
                new int2(0, 0),
                East,
                item,
                1f);
            Entity owner = CreatePortOwner(new GridPlacement
            {
                AnchorCell = new int2(0, 0),
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
            Entity belt = CreateBelt(new int2(0, 0), East);
            Entity owner = CreateOutputOwner(new int2(0, 0));

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
        public void BuildingOutput_WhenItemPrefabMissingItemComponent_StillInjectsItem()
        {
            CreateGrid();
            CreateItemCatalog(
                includeItemComponent: false,
                includeLocalTransform: true);
            Entity belt = CreateBelt(new int2(0, 0), East);
            Entity owner = CreateOutputOwner(new int2(0, 0));

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
            Entity belt = CreateBelt(new int2(0, 0), East);
            Entity owner = CreateOutputOwner(new int2(0, 0));

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
        public void PortOwnerOrder_RebuildsOnlyAfterGridRevisionChanges()
        {
            Entity grid = CreateGrid();
            CreateItemCatalog();
            CreatePortOwner(new GridPlacement
            {
                AnchorCell = new int2(5, 5),
                FootprintSize = new int2(1, 1),
                Kind = BuildingKind.Storage
            });
            UpdateTransferTick();

            Entity belt = CreateBelt(new int2(0, 0), East);
            CreateOutputOwner(new int2(0, 0));
            UpdateTransferTick();
            Assert.That(
                EntityManager.GetComponentData<BeltState>(belt).CurrentItem,
                Is.EqualTo(Entity.Null));

            GridDefinition definition =
                EntityManager.GetComponentData<GridDefinition>(grid);
            definition.Revision++;
            EntityManager.SetComponentData(grid, definition);
            UpdateTransferTick();
            Assert.That(
                EntityManager.GetComponentData<BeltState>(belt).CurrentItem,
                Is.Not.EqualTo(Entity.Null));
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

        private Entity CreateOutputOwner(int2 targetCell)
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
                        AvailableCount = 1,
                        PortIndex = 0,
                        Enabled = 1
                    }
                });
            return owner;
        }
    }
}
