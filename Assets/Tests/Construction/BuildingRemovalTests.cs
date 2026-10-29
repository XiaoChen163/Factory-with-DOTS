using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

namespace Factory.Tests
{
    public sealed class BuildingRemovalTests : FactoryWorldFixture
    {
        private static readonly int2 East = new int2(1, 0);

        [Test]
        public void RemovingBelt_ReturnsItemToPoolAndHidesRendering()
        {
            BlobAssetReference<FactoryDatabaseBlob> database = default;
            try
            {
                database = FactoryTestDatabase.CreateDatabase();
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
                        AnchorCell = new GridCell(int2.zero),
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
                        StartCell = new GridCell(int2.zero)
                    });
                GridBuildCommandSystem build =
                    GetOrCreateManagedSystem<GridBuildCommandSystem>();
                UpdateSystem(build);

                Assert.That(build.DiagnosticBatchCount, Is.EqualTo(1));
                Assert.That(build.LastBatchPlacementScanCount, Is.EqualTo(1));
                Assert.That(build.LastBatchTemporaryRecordCount, Is.EqualTo(1));
                Assert.That(build.LastBatchPathValidationCellCount, Is.Zero);

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
        public void RemovingBeltLine_ReturnsEveryCarriedItemToInitiatingPlayer()
        {
            BlobAssetReference<FactoryDatabaseBlob> database = default;
            try
            {
                database = FactoryTestDatabase.CreateDatabase();
                Entity grid = CreateBuildGrid();
                Entity catalog = EntityManager.CreateEntity();
                EntityManager.AddComponentData(catalog, new BuildingPrefabCatalog());
                EntityManager.AddBuffer<BuildingVisualPrefabEntry>(catalog);
                EntityManager.AddComponentData(
                    catalog,
                    new FactoryDatabase { Value = database });

                PlayerId playerId = new PlayerId { Value = 7 };
                Entity player = EntityManager.CreateEntity();
                EntityManager.AddComponentData(
                    player,
                    new PlayerIdentity { Value = playerId });
                EntityManager.AddComponentData(
                    player,
                    new PlayerInventory { SlotCount = 2 });
                DynamicBuffer<InventorySlot> inventory =
                    EntityManager.AddBuffer<InventorySlot>(player);
                inventory.ResizeUninitialized(2);
                inventory[0] = default;
                inventory[1] = default;

                Entity first = CreateRemovableBelt(
                    new GridCell(2, 0, 2),
                    CreateItem(1));
                Entity second = CreateRemovableBelt(
                    new GridCell(3, 0, 2),
                    CreateItem(1));

                GridOccupancyIndexSystem occupancy =
                    GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
                UpdateSystem(occupancy);

                EntityManager.GetBuffer<GridBuildCommand>(grid).Add(
                    new GridBuildCommand
                    {
                        Player = playerId,
                        Type = GridBuildCommandType.RemoveBeltLine,
                        StartCell = new GridCell(2, 0, 2)
                    });
                UpdateSystem(
                    GetOrCreateManagedSystem<GridBuildCommandSystem>());

                Assert.That(EntityManager.Exists(first), Is.False);
                Assert.That(EntityManager.Exists(second), Is.False);
                inventory = EntityManager.GetBuffer<InventorySlot>(player);
                Assert.That(inventory[0].ItemType.Value, Is.EqualTo(1));
                Assert.That(inventory[0].Count, Is.EqualTo(1));
                Assert.That(inventory[1].ItemType.Value, Is.EqualTo(1));
                Assert.That(inventory[1].Count, Is.EqualTo(1));
                Assert.That(
                    EntityManager.GetComponentData<PlayerInventory>(player).Revision,
                    Is.EqualTo(2),
                    "Each removed belt updates the shared inventory revision.");
            }
            finally
            {
                if (database.IsCreated)
                    database.Dispose();
            }
        }

        [Test]
        public void RemovingStorage_ReturnsStoredItemsToInitiatingPlayer()
        {
            BlobAssetReference<FactoryDatabaseBlob> database = default;
            try
            {
                database = FactoryTestDatabase.CreateDatabase();
                Entity grid = CreateBuildGrid();
                Entity catalog = EntityManager.CreateEntity();
                EntityManager.AddComponentData(catalog, new BuildingPrefabCatalog());
                EntityManager.AddBuffer<BuildingVisualPrefabEntry>(catalog);
                EntityManager.AddComponentData(
                    catalog,
                    new FactoryDatabase { Value = database });

                PlayerId playerId = new PlayerId { Value = 8 };
                Entity player = EntityManager.CreateEntity();
                EntityManager.AddComponentData(
                    player,
                    new PlayerIdentity { Value = playerId });
                EntityManager.AddComponentData(
                    player,
                    new PlayerInventory { SlotCount = 1 });
                EntityManager.AddBuffer<InventorySlot>(player).Add(default);

                Entity storage = EntityManager.CreateEntity();
                EntityManager.AddComponentData(
                    storage,
                    new GridPlacement
                    {
                        AnchorCell = new GridCell(4, 0, 4),
                        FootprintSize = new int2(1, 1),
                        Kind = BuildingKind.Storage
                    });
                EntityManager.AddBuffer<OccupiedCellOffset>(storage).Add(
                    new OccupiedCellOffset { Value = int2.zero });
                EntityManager.AddBuffer<BuildingPort>(storage);
                DynamicBuffer<InventorySlot> stored =
                    EntityManager.AddBuffer<InventorySlot>(storage);
                stored.Add(new InventorySlot
                {
                    ItemType = new ItemId { Value = 1 },
                    Count = 3
                });

                UpdateSystem(
                    GetOrCreateManagedSystem<GridOccupancyIndexSystem>());
                EntityManager.GetBuffer<GridBuildCommand>(grid).Add(
                    new GridBuildCommand
                    {
                        Player = playerId,
                        Type = GridBuildCommandType.Remove,
                        StartCell = new GridCell(4, 0, 4)
                    });
                UpdateSystem(
                    GetOrCreateManagedSystem<GridBuildCommandSystem>());

                Assert.That(EntityManager.Exists(storage), Is.False);
                DynamicBuffer<InventorySlot> inventory =
                    EntityManager.GetBuffer<InventorySlot>(player);
                int recovered = 0;
                for (int i = 0; i < inventory.Length; i++)
                {
                    if (inventory[i].ItemType.Value == 1)
                        recovered += inventory[i].Count;
                }
                Assert.That(recovered, Is.EqualTo(3));
                Assert.That(
                    EntityManager.GetComponentData<PlayerInventory>(player).Revision,
                    Is.EqualTo(1));
            }
            finally
            {
                if (database.IsCreated)
                    database.Dispose();
            }
        }

        private Entity CreateBuildGrid()
        {
            Entity grid = CreateTransportGrid();
            EntityManager.AddBuffer<GridBuildCommand>(grid);
            EntityManager.AddBuffer<GridBuildResult>(grid);
            return grid;
        }

        private Entity CreateRemovableBelt(GridCell cell, Entity item)
        {
            Entity belt = EntityManager.CreateEntity();
            EntityManager.AddComponentData(
                belt,
                new BeltState { CurrentItem = item });
            EntityManager.AddComponentData(
                belt,
                new GridPlacement
                {
                    AnchorCell = cell,
                    FootprintSize = new int2(1, 1),
                    Kind = BuildingKind.Belt
                });
            EntityManager.AddBuffer<OccupiedCellOffset>(belt).Add(
                new OccupiedCellOffset { Value = int2.zero });
            EntityManager.AddBuffer<BuildingPort>(belt).Add(
                new BuildingPort
                {
                    CellOffset = East,
                    Direction = East,
                    Type = BuildingPortType.Output
                });
            return belt;
        }
    }
}
