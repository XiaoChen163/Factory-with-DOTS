using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Factory.Tests
{
    public sealed class ItemPortBufferTests : FactoryWorldFixture
    {
        private static readonly int2 East = new int2(1, 0);

        [Test]
        public void PortBufferSwap_TogglesGenerationWithoutCopyingBuffers()
        {
            Entity owner = CreatePortOwner(new GridPlacement
            {
                AnchorCell = new GridCell(int2.zero),
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
            CreateTransportGrid();
            Entity item = CreateItem(1);
            Entity belt = CreateBelt(
                new GridCell(0, 0, 0),
                East,
                item,
                1f);
            Entity owner = CreatePortOwner(new GridPlacement
            {
                AnchorCell = new GridCell(int2.zero),
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
        public void StorageAdapter_GenerationOneWritesCurrentBuffersWithoutSafetyError()
        {
            BlobAssetReference<FactoryDatabaseBlob> database = default;
            try
            {
                database = FactoryTestDatabase.CreateDatabase();
                Entity databaseEntity = EntityManager.CreateEntity();
                EntityManager.AddComponentData(
                    databaseEntity,
                    new FactoryDatabase { Value = database });

                Entity owner = CreatePortOwner(new GridPlacement
                {
                    AnchorCell = new GridCell(int2.zero),
                    FootprintSize = new int2(1, 1),
                    Kind = BuildingKind.Storage
                });
                EntityManager.AddComponentData(
                    owner,
                    new StorageState { Capacity = 10, SlotCount = 2 });
                DynamicBuffer<InventorySlot> storageSlots =
                    EntityManager.AddBuffer<InventorySlot>(owner);
                storageSlots.ResizeUninitialized(2);
                storageSlots[0] = default;
                storageSlots[1] = default;
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
    }
}
