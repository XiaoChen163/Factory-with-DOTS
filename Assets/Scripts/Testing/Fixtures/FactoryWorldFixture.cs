using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace Factory.Tests
{
    /// <summary>
    /// Creates an isolated ECS World for system-level EditMode tests.
    /// Production DefaultWorld state is never reused by a test.
    /// </summary>
    public abstract class FactoryWorldFixture
    {
        protected World TestWorld { get; private set; }
        protected EntityManager EntityManager => TestWorld.EntityManager;

        [SetUp]
        public virtual void SetUpWorld()
        {
            TestWorld = new World($"{GetType().Name} World");
        }

        [TearDown]
        public virtual void TearDownWorld()
        {
            if (TestWorld != null && TestWorld.IsCreated)
            {
                EntityManager.CompleteAllTrackedJobs();
                TestWorld.Dispose();
            }

            TestWorld = null;
        }

        protected Entity CreateItem(ushort itemType = 1)
        {
            Entity entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, new Item
            {
                ItemType = new ItemId { Value = itemType }
            });
            return entity;
        }

        protected Entity CreateBelt(
            int2 cell,
            int2 direction,
            Entity item = default,
            float progress = 0f)
        {
            Entity entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, new Belt
            {
                Cell = cell,
                Direction = direction,
                NextCell = cell + direction,
                CurrentItem = item,
                Progress = progress,
                CellsPerSecond = 1f
            });
            return entity;
        }

        protected Entity CreatePortOwner(in GridPlacement placement)
        {
            Entity owner = EntityManager.CreateEntity();
            EntityManager.AddComponentData(owner, placement);
            EntityManager.AddBuffer<BuildingPort>(owner);
            EntityManager.AddBuffer<ItemInputPortCurrent>(owner);
            EntityManager.AddBuffer<ItemInputPortNext>(owner);
            EntityManager.AddBuffer<ItemOutputPortCurrent>(owner);
            EntityManager.AddBuffer<ItemOutputPortNext>(owner);
            EntityManager.AddBuffer<ItemTransferReceiptCurrent>(owner);
            EntityManager.AddBuffer<ItemTransferReceiptNext>(owner);
            return owner;
        }

        protected T GetOrCreateManagedSystem<T>() where T : SystemBase
        {
            return TestWorld.GetOrCreateSystemManaged<T>();
        }

        protected void UpdateSystem(SystemBase system)
        {
            system.Update();
            EntityManager.CompleteAllTrackedJobs();
        }
    }
}
