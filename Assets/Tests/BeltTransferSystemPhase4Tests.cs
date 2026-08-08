using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

namespace Factory.Tests
{
    /// <summary>
    /// Phase 4 tests: pooled Item lifecycle with enableable components, fixed
    /// tick visual snapshots, and item-centered presentation interpolation.
    /// </summary>
    public sealed class BeltTransferSystemPhase4Tests : FactoryWorldFixture
    {
        private static readonly int2 East = new int2(1, 0);
        private static readonly ItemId IronOre =
            new ItemId { Value = 1 };

        [Test]
        public void BuildingInput_ReturnsItemToPoolInsteadOfDestroying()
        {
            CreateGrid();
            Entity pool = CreatePool(IronOre);
            Entity item = CreateItemWithVisualState(IronOre);
            Entity belt = CreateBelt(
                new int2(0, 0),
                East,
                item,
                1f);
            Entity owner = CreateInputOwner(new int2(0, 0));

            UpdateTransferTick();

            Assert.That(
                EntityManager.GetComponentData<BeltState>(belt)
                    .CurrentItem,
                Is.EqualTo(Entity.Null));
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
        }

        [Test]
        public void BuildingInput_StillDestroysWhenNoPoolExists()
        {
            CreateGrid();
            Entity item = CreateItemWithVisualState(IronOre);
            Entity belt = CreateBelt(
                new int2(0, 0),
                East,
                item,
                1f);
            Entity owner = CreateInputOwner(new int2(0, 0));

            UpdateTransferTick();

            Assert.That(EntityManager.Exists(item), Is.False);
            Assert.That(
                EntityManager.GetComponentData<BeltState>(belt)
                    .CurrentItem,
                Is.EqualTo(Entity.Null));
        }

        [Test]
        public void BuildingOutput_ReusesPooledItemWithoutInstantiate()
        {
            CreateGrid();
            CreateItemCatalog();
            Entity pooledItem = CreateItemWithVisualState(IronOre);
            EntityManager.AddComponentData(
                pooledItem,
                LocalTransform.FromPosition(float3.zero));
            EntityManager.AddComponent<DisableRendering>(pooledItem);
            EntityManager.SetComponentEnabled<Item>(pooledItem, false);
            Entity pool = CreatePool(
                IronOre,
                pooledItem);
            Entity belt = CreateBelt(new int2(0, 0), East);
            Entity owner = CreateOutputOwner(new int2(0, 0));

            UpdateTransferTick();

            Entity item =
                EntityManager.GetComponentData<BeltState>(belt)
                    .CurrentItem;
            Assert.That(item, Is.EqualTo(pooledItem));
            Assert.That(
                EntityManager.IsComponentEnabled<Item>(item),
                Is.True);
            Assert.That(
                EntityManager.HasComponent<DisableRendering>(item),
                Is.False);
            Assert.That(
                EntityManager.GetComponentData<LocalTransform>(item)
                    .Position,
                Is.EqualTo(new float3(0.5f, 0.535f, 0.5f)));
            Assert.That(
                EntityManager.GetComponentData<ItemVisualState>(item)
                    .ToPosition,
                Is.EqualTo(new float3(0.5f, 0.535f, 0.5f)));

            ItemPool poolState =
                EntityManager.GetComponentData<ItemPool>(pool);
            Assert.That(poolState.FreeCursor, Is.EqualTo(1));
            DynamicBuffer<ItemPoolEntry> entries =
                EntityManager.GetBuffer<ItemPoolEntry>(pool);
            Assert.That(entries[0].Entity, Is.EqualTo(Entity.Null));
        }

        [Test]
        public void VisualCaptureSystem_SamplesLatestTransportPosition()
        {
            Entity item = CreateItemWithVisualState(IronOre);
            CreateBelt(new int2(1, 0), East, item, 0.5f);

            SystemHandle capture =
                TestWorld.GetOrCreateSystem<ItemVisualStateCaptureSystem>();
            capture.Update(TestWorld.Unmanaged);
            EntityManager.CompleteAllTrackedJobs();

            ItemVisualState visualState =
                EntityManager.GetComponentData<ItemVisualState>(item);
            Assert.That(
                visualState.FromPosition,
                Is.EqualTo(float3.zero));
            Assert.That(
                visualState.ToPosition,
                Is.EqualTo(new float3(1.5f, 0.535f, 0.5f)));
            Assert.That(visualState.Progress, Is.EqualTo(0.5f));
        }

        [Test]
        public void PresentationSystem_InterpolatesAlongVisualProgress()
        {
            Entity item = CreateItemWithVisualState(IronOre);
            EntityManager.AddComponentData(
                item,
                LocalTransform.FromPosition(float3.zero));
            EntityManager.SetComponentData(item, new ItemVisualState
            {
                FromPosition = float3.zero,
                ToPosition = new float3(1f, 0f, 0f),
                Progress = 0.5f
            });

            SystemHandle presentation =
                TestWorld.GetOrCreateSystem<
                    ItemTransformPresentationSystem>();
            presentation.Update(TestWorld.Unmanaged);
            EntityManager.CompleteAllTrackedJobs();

            Assert.That(
                EntityManager.GetComponentData<LocalTransform>(item)
                    .Position,
                Is.EqualTo(new float3(0.5f, 0f, 0f)));

            EntityManager.SetComponentData(item, new ItemVisualState
            {
                FromPosition = float3.zero,
                ToPosition = new float3(1f, 0f, 0f),
                Progress = 1f
            });
            presentation.Update(TestWorld.Unmanaged);
            EntityManager.CompleteAllTrackedJobs();

            Assert.That(
                EntityManager.GetComponentData<LocalTransform>(item)
                    .Position,
                Is.EqualTo(new float3(1f, 0f, 0f)));
        }

        [Test]
        public void PresentationSystem_PreservesPostTransformScale()
        {
            Entity item = CreateItemWithVisualState(IronOre);
            EntityManager.AddComponentData(
                item,
                LocalTransform.FromPosition(float3.zero));
            EntityManager.AddComponentData(
                item,
                new PostTransformMatrix
                {
                    Value = float4x4.Scale(
                        0.34f,
                        0.22f,
                        0.54f)
                });
            EntityManager.AddComponentData(
                item,
                new LocalToWorld
                {
                    Value = float4x4.identity
                });
            EntityManager.SetComponentData(item, new ItemVisualState
            {
                FromPosition = float3.zero,
                ToPosition = new float3(1f, 0f, 0f),
                Progress = 0.5f
            });

            SystemHandle presentation =
                TestWorld.GetOrCreateSystem<
                    ItemTransformPresentationSystem>();
            presentation.Update(TestWorld.Unmanaged);
            EntityManager.CompleteAllTrackedJobs();

            LocalToWorld localToWorld =
                EntityManager.GetComponentData<LocalToWorld>(item);
            Assert.That(
                math.abs(localToWorld.Value.c0.x),
                Is.EqualTo(0.34f).Within(0.0001f));
            Assert.That(
                localToWorld.Value.c3.x,
                Is.EqualTo(0.5f).Within(0.0001f));
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
                ItemType = IronOre
            });
            EntityManager.AddComponentData(
                prefab,
                new ItemVisualState());
            EntityManager.AddComponentData(
                prefab,
                LocalTransform.FromPosition(float3.zero));
            Entity catalog = EntityManager.CreateEntity(
                typeof(BuildingPrefabCatalog));
            EntityManager.AddBuffer<ItemPrefabEntry>(catalog).Add(
                new ItemPrefabEntry
                {
                    ItemType = IronOre,
                    Prefab = prefab
                });
        }

        private Entity CreateItemWithVisualState(ItemId itemType)
        {
            Entity item = CreateItem(itemType.Value);
            EntityManager.AddComponentData(item, new ItemVisualState());
            return item;
        }

        private Entity CreatePool(
            ItemId itemType,
            params Entity[] inactiveItems)
        {
            Entity pool = EntityManager.CreateEntity(
                typeof(ItemPool),
                typeof(ItemPoolEntry));
            EntityManager.SetComponentData(pool, new ItemPool
            {
                ItemType = itemType,
                FreeCursor = 0
            });
            DynamicBuffer<ItemPoolEntry> entries =
                EntityManager.GetBuffer<ItemPoolEntry>(pool);
            for (int i = 0; i < inactiveItems.Length; i++)
            {
                entries.Add(new ItemPoolEntry
                {
                    Entity = inactiveItems[i]
                });
            }

            return pool;
        }

        private Entity CreateInputOwner(int2 sourceCell)
        {
            Entity owner = CreatePortOwner(new GridPlacement
            {
                AnchorCell = sourceCell,
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
                        AcceptedItemType = IronOre,
                        FreeCapacity = 1,
                        PortIndex = 0,
                        Enabled = 1,
                        FilterMode = ItemPortFilterMode.ExactItemType
                    }
                });
            return owner;
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
                        ItemType = IronOre,
                        AvailableCount = 1,
                        PortIndex = 0,
                        Enabled = 1
                    }
                });
            return owner;
        }
    }
}
