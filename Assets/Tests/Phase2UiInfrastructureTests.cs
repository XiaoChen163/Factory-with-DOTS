using System;
using NUnit.Framework;
using Unity.Entities;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Factory.Tests
{
    public sealed class Phase2UiInfrastructureTests : FactoryWorldFixture
    {
        private UiDataHub hub;

        public override void TearDownWorld()
        {
            UiRuntimeServices.Detach(TestWorld, hub);
            hub = null;
            base.TearDownWorld();
        }

        [Test]
        public void WindowManager_KeepsLeftAndRightWindowsOpenTogether()
        {
            VisualElement left = new();
            VisualElement right = new();
            VisualElement bottom = new();
            VisualElement overlay = new();
            VisualElement building = new();
            VisualElement backpack = new();
            using UiWindowManager manager = new(left, right, bottom, overlay);
            manager.Register(UiWindowId.Building, UiDockRegion.Left, building);
            manager.Register(UiWindowId.Backpack, UiDockRegion.Right, backpack);

            manager.Open(UiWindowId.Building);
            manager.Open(UiWindowId.Backpack);

            Assert.That(manager.IsOpen(UiWindowId.Building), Is.True);
            Assert.That(manager.IsOpen(UiWindowId.Backpack), Is.True);
            Assert.That(building.parent, Is.SameAs(left));
            Assert.That(backpack.parent, Is.SameAs(right));
            Assert.That(building.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(backpack.style.display.value, Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void ClosingWindow_DisposesItsDataObservation()
        {
            hub = new UiDataHub();
            VisualElement left = new();
            using UiWindowManager manager = new(
                left, new VisualElement(), new VisualElement(), new VisualElement());
            manager.Register(
                UiWindowId.Building, UiDockRegion.Left, new VisualElement());
            BuildingRuntimeId id = new(42);

            manager.Open(
                UiWindowId.Building,
                () => hub.Buildings.Subscribe(id, _ => { }));
            Assert.That(hub.Observations.BuildingCount, Is.EqualTo(1));

            manager.Close(UiWindowId.Building);
            Assert.That(hub.Observations.BuildingCount, Is.Zero);
        }

        [Test]
        public void ExportSystem_ReadsInventoryOnlyWhileObservedAndRevisionChanged()
        {
            PlayerId playerId = new() { Value = 17 };
            Entity player = EntityManager.CreateEntity();
            EntityManager.AddComponentData(player, new PlayerIdentity { Value = playerId });
            EntityManager.AddComponentData(player, new PlayerInventory
            {
                SlotCount = 2,
                Revision = 3
            });
            DynamicBuffer<InventorySlot> slots =
                EntityManager.AddBuffer<InventorySlot>(player);
            slots.Add(new InventorySlot
            {
                ItemType = new ItemId { Value = 1 },
                Count = 5
            });
            slots.Add(default);

            hub = new UiDataHub();
            UiRuntimeServices.Attach(TestWorld, hub);
            UiSnapshotExportSystem system =
                GetOrCreateManagedSystem<UiSnapshotExportSystem>();

            UpdateSystem(system);
            Assert.That(system.InventoryBufferReadsLastUpdate, Is.Zero);

            InventorySnapshot received = null;
            IDisposable subscription = hub.Inventories.Subscribe(
                playerId, value => received = value);
            UpdateSystem(system);
            Assert.That(system.InventoryBufferReadsLastUpdate, Is.EqualTo(1));
            Assert.That(received, Is.Not.Null);
            Assert.That(received.Slots[0].Count, Is.EqualTo(5));

            ItemSlotSnapshot[] firstSlots = received.Slots;
            UpdateSystem(system);
            Assert.That(system.InventoryBufferReadsLastUpdate, Is.Zero);
            Assert.That(received.Slots, Is.SameAs(firstSlots));

            PlayerInventory inventory =
                EntityManager.GetComponentData<PlayerInventory>(player);
            inventory.Revision++;
            EntityManager.SetComponentData(player, inventory);
            UpdateSystem(system);
            Assert.That(system.InventoryBufferReadsLastUpdate, Is.EqualTo(1));
            Assert.That(received.Slots, Is.Not.SameAs(firstSlots));

            subscription.Dispose();
            UpdateSystem(system);
            Assert.That(system.InventoryBufferReadsLastUpdate, Is.Zero);
        }

        [Test]
        public void MultipleSubscribers_ShareOneObservationUntilLastDispose()
        {
            hub = new UiDataHub();
            PlayerId id = new() { Value = 1 };
            IDisposable first = hub.Inventories.Subscribe(id, _ => { });
            IDisposable second = hub.Inventories.Subscribe(id, _ => { });
            Assert.That(hub.Observations.PlayerCount, Is.EqualTo(1));

            first.Dispose();
            Assert.That(hub.Observations.PlayerCount, Is.EqualTo(1));
            second.Dispose();
            Assert.That(hub.Observations.PlayerCount, Is.Zero);
        }

        [Test]
        public void EcsScene_HasExactlyOneConfiguredGameUiRoot()
        {
            Scene scene = EditorSceneManager.OpenScene(
                "Assets/Scenes/Ecs.unity", OpenSceneMode.Single);
            int documentCount = 0;
            GameUiController controller = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                documentCount += root.GetComponentsInChildren<UIDocument>(true).Length;
                if (root.name == "GameUiRoot")
                    controller = root.GetComponent<GameUiController>();
            }

            Assert.That(documentCount, Is.EqualTo(1));
            Assert.That(controller, Is.Not.Null);
            UIDocument document = controller.GetComponent<UIDocument>();
            Assert.That(document.panelSettings, Is.Not.Null);
            Assert.That(document.visualTreeAsset, Is.Not.Null);
            Assert.That(controller.GetComponent<PlayerInputModeController>(), Is.Not.Null);
            Assert.That(controller.PresentationCatalog, Is.Not.Null);
        }
    }

}
