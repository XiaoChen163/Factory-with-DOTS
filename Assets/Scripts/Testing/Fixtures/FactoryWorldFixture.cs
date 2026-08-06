using NUnit.Framework;
using Unity.Core;
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
            EntityManager.AddComponentData(entity, new BeltTopology
            {
                Cell = cell,
                Direction = direction,
                CellsPerSecond = 1f
            });
            EntityManager.AddComponentData(entity, new BeltState
            {
                CurrentItem = item,
                Progress = progress
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

        /// <summary>
        /// Runs one full transport tick: BeltTransferSystem schedules the
        /// Burst arbitration job and TransferCommandBufferSystem plays back
        /// the item create/destroy commands it produced. BeltProgressSystem
        /// runs first with a fixed one-second delta so items become ready one
        /// cell per tick in logic tests.
        /// </summary>
        protected void UpdateTransferTick()
        {
            TestWorld.PushTime(new TimeData(
                TestWorld.Time.ElapsedTime + 1f,
                1f));
            SystemHandle progress =
                TestWorld.GetOrCreateSystem<BeltProgressSystem>();
            progress.Update(TestWorld.Unmanaged);
            UpdateSystem(GetOrCreateManagedSystem<BeltTransferSystem>());
            UpdateSystem(
                GetOrCreateManagedSystem<TransferCommandBufferSystem>());
            TestWorld.PopTime();
        }
    }
}
