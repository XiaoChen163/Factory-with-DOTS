using System;
using System.Linq;
using NUnit.Framework;
using Unity.Entities;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Factory.Tests
{
    public sealed class UiInfrastructureTests : FactoryWorldFixture
    {
        private UiDataHub hub;

        public override void TearDownWorld()
        {
            UiRuntimeServices.Detach(TestWorld, hub);
            hub = null;
            base.TearDownWorld();
        }

        [Test]
        public void ProcessorSnapshot_ExportsBoundOutletNumbersAndMachineRelativeRecipe()
        {
            var asset = AssetDatabase.LoadAssetAtPath<FactoryDatabaseAsset>(FactoryDatabaseCsvImporter.DatabaseAssetPath);
            Assert.That(FactoryDatabaseBakingUtility.TryBuild(asset, null, out var database), Is.True);
            try
            {
                Entity singleton = EntityManager.CreateEntity();
                EntityManager.AddComponentData(singleton, new FactoryDatabase { Value = database });
                var definition = asset.buildings.Single(b => b.key == "test_machine_m");
                Entity owner = EntityManager.CreateEntity();
                EntityManager.AddComponentData(owner, new ItemContainerIdentity { RuntimeId = 123 });
                EntityManager.AddComponentData(owner, new BuildingIdentity
                    { BuildingDefinition = new BuildingDefinitionId { Value = definition.id } });
                EntityManager.AddComponentData(owner, new GridPlacement { Kind = BuildingKind.Processor });
                var machineType = new MachineTypeId { Value = definition.machineTypeId };
                EntityManager.AddComponentData(owner, new ItemProcessor { MachineType = machineType });
                EntityManager.AddComponentData(owner, new ItemProcessState());
                EntityManager.AddBuffer<ProcessorItemSlot>(owner);
                var ports = EntityManager.AddBuffer<BuildingPort>(owner);
                // The UI must read the explicit binding, not derive a number from the slot/port index.
                ports.Add(new BuildingPort { Type = BuildingPortType.Output, Index = 4, Number = 17, OutputSlot = 1 });
                ports.Add(new BuildingPort { Type = BuildingPortType.Output, Index = 2, Number = 6, OutputSlot = 2 });
                ref FactoryDatabaseBlob blob = ref database.Value;
                FactoryRecipeRangeBlob range = FactoryDatabaseUtility.GetRecipeRange(ref blob, machineType);
                var slots = EntityManager.GetBuffer<ProcessorItemSlot>(owner);
                var process = new ItemProcessState { SelectedRecipeIndex = -1, ActiveRecipeIndex = -1 };
                Assert.That(ItemProcessUtility.TrySelectRecipe(1, range, ref blob, slots, ref process), Is.True);
                EntityManager.SetComponentData(owner, process);
                hub = new UiDataHub();
                UiRuntimeServices.Attach(TestWorld, hub);
                BuildingSnapshot received = null;
                using IDisposable subscription = hub.Buildings.Subscribe(new BuildingRuntimeId(123), value => received = value);
                UpdateSystem(GetOrCreateManagedSystem<UiSnapshotExportSystem>());
                Assert.That(received.Processor.SelectedRecipeId, Is.EqualTo(blob.Recipes[range.Start + 1].Id));
                Assert.That(received.Processor.Outputs.Select(s => s.OutputPortNumber), Is.EqualTo(new ushort[] { 17, 6 }));
                Assert.That(received.Processor.Outputs.Select(s => s.SlotIndex), Is.EqualTo(new ushort[] { 0, 1 }));
                Assert.That(received.Processor.Inputs.All(s => s.OutputPortNumber == 0), Is.True);
            }
            finally { database.Dispose(); }
        }

        [Test]
        public void OutputSlotLabel_ShowsConfiguredNumber_AndClearsWhenReused()
        {
            VisualElement root = new();
            var view = new ItemSlotGridView(root, null);
            view.Render(new[] { new ItemSlotSnapshot(0, default, 0, 64, default, UiSlotAccess.Extract, 17) });
            Label label = root.Q<Label>("output-port-number");
            Assert.That(label.text, Is.EqualTo("出口 17"));
            Assert.That(label.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            view.Render(new[] { new ItemSlotSnapshot(0, default, 0, 64, default, UiSlotAccess.Insert) });
            Assert.That(label.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void OutputPortRay_RespectsRotationLayerAndTenMeterReach(int rotation)
        {
            var grid = new WorldGridConfig { CellSize = 1f, LayerHeight = 4f };
            var placement = new GridPlacement { AnchorCell = new GridCell(4, 2, 5),
                FootprintSize = new Unity.Mathematics.int2(3, 3), QuarterTurns = (byte)rotation };
            var port = new BuildingPort { Type = BuildingPortType.Output, Index = 0, Number = 17, OutputSlot = 1,
                CellOffset = new Unity.Mathematics.int2(3, 1), Direction = new Unity.Mathematics.int2(1, 0) };
            Unity.Mathematics.int2 direction = EcsGridUtility.Rotate(port.Direction, (byte)rotation);
            Vector3 outward = new(direction.x, 0f, direction.y);
            Vector3 center = EcsGridUtility.CellToWorldCenter(EcsGridUtility.GetBuildingCell(placement, port.CellOffset), grid);
            center += -outward * 0.5f + Vector3.up * 0.375f;
            Assert.That(EcsGridInteractionController.TryHitOutputPort(new Ray(center + outward * 9f, -outward),
                placement, port, grid, 10f, out _, out Vector3 hit), Is.True);
            Assert.That(Vector3.Distance(hit, center), Is.LessThan(0.001f));
            Assert.That(EcsGridInteractionController.TryHitOutputPort(new Ray(center + outward * 11f, -outward),
                placement, port, grid, 50f, out _, out _), Is.False);
            Assert.That(EcsGridInteractionController.TryHitOutputPort(new Ray(center + outward * 9f + Vector3.up, -outward),
                placement, port, grid, 10f, out _, out _), Is.False);
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
