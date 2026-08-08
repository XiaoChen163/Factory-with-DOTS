using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Factory.Tests
{
    /// <summary>
    /// Phase 3 system-level tests: the belt component split, deferred ECB
    /// item creation, stats produced by the Burst arbitration job, and
    /// multi-tick pipeline correctness through the full transfer path.
    /// </summary>
    public sealed class BeltTransferSystemPhase3Tests : FactoryWorldFixture
    {
        private static readonly int2 East = new int2(1, 0);

        [Test]
        public void Belt_IsSplitIntoTopologyAndStateComponents()
        {
            Entity belt = CreateBelt(int2.zero, East);

            Assert.That(
                EntityManager.HasComponent<BeltTopology>(belt),
                Is.True);
            Assert.That(
                EntityManager.HasComponent<BeltState>(belt),
                Is.True);
            Assert.That(
                EntityManager.GetComponentData<BeltTopology>(belt).Cell,
                Is.EqualTo(int2.zero));
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
            CreateGrid();
            Entity item = CreateItem(1);
            Entity source = CreateBelt(new int2(0, 0), East, item, 1f);
            Entity target = CreateBelt(new int2(1, 0), East);

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
            CreateGrid();
            Entity itemA = CreateItem(1);
            Entity itemB = CreateItem(2);
            Entity belt0 = CreateBelt(new int2(0, 0), East, itemA, 1f);
            Entity belt1 = CreateBelt(new int2(1, 0), East, itemB, 1f);
            Entity belt2 = CreateBelt(new int2(2, 0), East);
            Entity belt3 = CreateBelt(new int2(3, 0), East);

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
            CreateGrid();
            CreateItem();
            CreateBelt(new int2(0, 0), East);
            CreateBelt(new int2(1, 0), East);

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
            CreateGrid();
            CreateItemCatalog();
            Entity belt = CreateBelt(new int2(0, 0), East);
            Entity owner = CreateOutputOwner(new int2(0, 0));

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

        private void CreateItemCatalog()
        {
            Entity prefab = EntityManager.CreateEntity(typeof(Prefab));
            EntityManager.AddComponentData(prefab, new Item
            {
                ItemType = new ItemId { Value = 1 }
            });
            EntityManager.AddComponentData(
                prefab,
                LocalTransform.FromPosition(float3.zero));
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
