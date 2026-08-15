using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Unity.Entities;
using Unity.Mathematics;

namespace Factory.Tests
{
    public sealed class Phase2UiPlayModeTests
    {
        [UnityTest]
        public IEnumerator EcsScene_GameUiRootInitializesAndTogglesEmptyWindows()
        {
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            yield return null;

            GameUiController[] controllers =
                Object.FindObjectsByType<GameUiController>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
            Assert.That(controllers, Has.Length.EqualTo(1));
            GameUiController controller = controllers[0];
            Assert.That(controller.DataHub, Is.Not.Null);
            Assert.That(controller.Windows, Is.Not.Null);

            controller.ToggleBackpack(new PlayerId { Value = 1 });
            controller.OpenBuilding(new BuildingRuntimeId(123));
            Assert.That(controller.Windows.IsOpen(UiWindowId.Backpack), Is.True);
            Assert.That(controller.Windows.IsOpen(UiWindowId.Building), Is.True);

            controller.Windows.Close(UiWindowId.Building);
            controller.Windows.Close(UiWindowId.Backpack);
            Assert.That(controller.DataHub.Observations.PlayerCount, Is.Zero);
            Assert.That(controller.DataHub.Observations.BuildingCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Phase3_InputModes_KeepBuildAndRegularWindowsExclusive()
        {
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            yield return null;

            GameUiController controller = Object.FindFirstObjectByType<GameUiController>();
            PlayerInputModeController input =
                controller.GetComponent<PlayerInputModeController>();

            controller.HandleBackpackToggle();
            Assert.That(controller.Windows.IsOpen(UiWindowId.Backpack), Is.True);

            controller.HandleBuildCatalogToggle();
            Assert.That(input.IsBuildMode, Is.True);
            Assert.That(input.IsModalOpen, Is.True);
            Assert.That(controller.Windows.IsOpen(UiWindowId.BuildCatalog), Is.True);
            Assert.That(controller.Windows.IsOpen(UiWindowId.Backpack), Is.False);

            controller.HandleBackpackToggle();
            Assert.That(controller.Windows.IsOpen(UiWindowId.Backpack), Is.False,
                "Tab must be ignored while building.");

            controller.CloseBuildCatalog();
            Assert.That(input.IsBuildMode, Is.True,
                "Closing the picker must keep batch build mode active.");
            Assert.That(input.IsBuildCatalogToggleActionEnabled, Is.True,
                "Q must remain available through the Build action map.");
            controller.HandleBuildCatalogToggle();
            Assert.That(controller.Windows.IsOpen(UiWindowId.BuildCatalog), Is.True);

            controller.HandleCancel();
            Assert.That(input.IsBuildMode, Is.False);
            Assert.That(input.IsModalOpen, Is.False);
            Assert.That(controller.Windows.IsOpen(UiWindowId.BuildCatalog), Is.False);
        }

        [UnityTest]
        public IEnumerator Phase3_EscapeClosesBackpackAndBuildingTogether()
        {
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            yield return null;

            GameUiController controller = Object.FindFirstObjectByType<GameUiController>();
            controller.ToggleBackpack(new PlayerId { Value = 1 });
            controller.OpenBuilding(new BuildingRuntimeId(123));
            controller.HandleCancel();

            Assert.That(controller.Windows.IsOpen(UiWindowId.Backpack), Is.False);
            Assert.That(controller.Windows.IsOpen(UiWindowId.Building), Is.False);
            Assert.That(controller.DataHub.Observations.PlayerCount, Is.Zero);
            Assert.That(controller.DataHub.Observations.BuildingCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DemolitionMode_TogglesHintAndKeepsOtherModesClosed()
        {
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            yield return null;

            GameUiController controller =
                Object.FindFirstObjectByType<GameUiController>();
            PlayerInputModeController input =
                controller.GetComponent<PlayerInputModeController>();
            Label hint = controller.GetComponent<UIDocument>()
                .rootVisualElement.Q<Label>("demolition-mode-hint");

            controller.HandleDemolitionToggle();
            Assert.That(input.IsDemolitionMode, Is.True);
            Assert.That(input.IsBuildMode, Is.False);
            Assert.That(hint.style.display.value, Is.EqualTo(DisplayStyle.Flex));

            controller.HandleBuildCatalogToggle();
            controller.HandleBackpackToggle();
            Assert.That(input.IsDemolitionMode, Is.True);
            Assert.That(controller.Windows.IsOpen(UiWindowId.BuildCatalog), Is.False);
            Assert.That(controller.Windows.IsOpen(UiWindowId.Backpack), Is.False);

            controller.HandleCancel();
            Assert.That(input.IsDemolitionMode, Is.False);
            Assert.That(hint.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator Phase6_BuildShortcut_BindsSelectionAndReentersBatchBuildMode()
        {
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            GameUiController controller =
                Object.FindFirstObjectByType<GameUiController>();
            EcsGridInteractionController interaction =
                Object.FindFirstObjectByType<EcsGridInteractionController>();
            PlayerInputModeController input =
                controller.GetComponent<PlayerInputModeController>();
            InputActionAsset runtimeActions =
                typeof(PlayerInputModeController).GetField(
                    "runtimeActions",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(input) as InputActionAsset;
            InputAction firstShortcut = runtimeActions?
                .FindActionMap("UI", true)
                .FindAction("BuildShortcut1", true);
            Assert.That(firstShortcut.bindings.Any(
                binding => binding.effectivePath == "<Keyboard>/1"), Is.True);
            Keyboard keyboard = Keyboard.current;
            bool addedKeyboard = keyboard == null;
            if (addedKeyboard)
                keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                Assert.That(firstShortcut.controls.Any(
                    control => control.path.EndsWith("/1")), Is.True,
                    "The main-row number binding must resolve to a real keyboard control.");
                Assert.That(firstShortcut.enabled, Is.True);
            }
            finally
            {
                if (addedKeyboard)
                    InputSystem.RemoveDevice(keyboard);
            }

            controller.HandleBuildCatalogToggle();
            controller.CloseBuildCatalog();
            for (int i = 0;
                 i < 120 && !interaction.SelectedBuildingLevel.IsValid;
                 i++)
            {
                yield return null;
            }

            BuildingLevelId selected = interaction.SelectedBuildingLevel;
            Assert.That(selected.IsValid, Is.True);
            controller.HandleBuildShortcut(2);
            Assert.That(controller.GetBuildShortcut(2), Is.EqualTo(selected));

            controller.HandleBuildCatalogToggle();
            for (int i = 0;
                 i < 120 && (controller.LatestBuildCatalog.Levels == null ||
                             controller.LatestBuildCatalog.Levels.Length < 2);
                 i++)
            {
                yield return null;
            }
            BuildingLevelId alternate =
                controller.LatestBuildCatalog.Levels.First(
                    value => value != selected);
            controller.SelectCatalogBuildingLevel(alternate);
            controller.HandleBuildShortcut(3);
            controller.CloseBuildCatalog();
            Assert.That(controller.GetBuildShortcut(3), Is.EqualTo(alternate));

            controller.HandleBuildShortcut(3);
            Assert.That(interaction.SelectedBuildingLevel, Is.EqualTo(alternate));
            Assert.That(controller.GetBuildShortcut(2), Is.EqualTo(selected),
                "Switching shortcuts must not overwrite another occupied slot.");
            controller.HandleBuildShortcut(2);
            Assert.That(interaction.SelectedBuildingLevel, Is.EqualTo(selected));
            Assert.That(controller.GetBuildShortcut(3), Is.EqualTo(alternate));

            controller.HandleCancel();
            Assert.That(input.IsBuildMode, Is.False);
            controller.HandleBuildShortcut(2);

            Assert.That(input.IsBuildMode, Is.True);
            Assert.That(interaction.SelectedBuildingLevel, Is.EqualTo(selected));
            VisualElement bar = controller.GetComponent<UIDocument>()
                .rootVisualElement.Q("build-shortcut-bar");
            Assert.That(bar, Is.Not.Null);
            Assert.That(bar.childCount, Is.EqualTo(9));
        }

        [UnityTest]
        public IEnumerator Phase5_EcsSceneUsesRetainedUiAndInputSystemBuildActions()
        {
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            yield return null;

            Assert.That(Object.FindObjectsByType<Stage3PrototypeHud>(
                FindObjectsInactive.Include, FindObjectsSortMode.None), Is.Empty,
                "The prototype IMGUI HUD must not exist in the production scene.");

            EcsGridInteractionController interaction =
                Object.FindFirstObjectByType<EcsGridInteractionController>();
            Assert.That(interaction, Is.Not.Null);
            Assert.That(typeof(EcsGridInteractionController).GetMethod(
                "OnGUI", BindingFlags.Instance | BindingFlags.NonPublic), Is.Null,
                "Build selection must be owned by the retained-mode UI.");

            GameUiController controller =
                Object.FindFirstObjectByType<GameUiController>();
            PlayerInputModeController input =
                controller.GetComponent<PlayerInputModeController>();
            Assert.That(input.IsGameplayActionMapEnabled, Is.True);
            Assert.That(input.IsBuildActionMapEnabled, Is.False);

            controller.HandleBuildCatalogToggle();
            Assert.That(input.IsBuildMode, Is.True);
            Assert.That(input.IsGameplayActionMapEnabled, Is.False);
            Assert.That(input.IsBuildActionMapEnabled, Is.False,
                "Modal build catalog owns input until a selection is made.");

            controller.CloseBuildCatalog();
            Assert.That(input.IsBuildActionMapEnabled, Is.True,
                "Build actions must become active after the catalog closes.");
            controller.HandleCancel();
            Assert.That(input.IsBuildMode, Is.False);
            Assert.That(input.IsGameplayActionMapEnabled, Is.True);
        }

        [UnityTest]
        public IEnumerator Phase5_BuildCatalogSelectionSurvivesSnapshotRefresh()
        {
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            GameUiController controller =
                Object.FindFirstObjectByType<GameUiController>();
            EcsGridInteractionController interaction =
                Object.FindFirstObjectByType<EcsGridInteractionController>();
            controller.HandleBuildCatalogToggle();

            for (int i = 0;
                 i < 120 && (controller.LatestBuildCatalog.Levels == null ||
                             controller.LatestBuildCatalog.Levels.Length < 2);
                 i++)
                yield return null;

            BuildingLevelId[] levels = controller.LatestBuildCatalog.Levels;
            Assert.That(levels, Is.Not.Null.And.Length.GreaterThanOrEqualTo(2));
            BuildingLevelId requested = levels[0] == interaction.SelectedBuildingLevel
                ? levels[1]
                : levels[0];

            controller.SelectCatalogBuildingLevel(requested);
            yield return null;
            yield return null;
            Assert.That(controller.SelectedCatalogBuildingLevel,
                Is.EqualTo(requested),
                "Repeated catalog snapshots must not reset the pending selection.");

            controller.ConfirmCatalogBuildingSelection();
            Assert.That(interaction.SelectedBuildingLevel, Is.EqualTo(requested));
            Assert.That(controller.Windows.IsOpen(UiWindowId.BuildCatalog), Is.False);
            Assert.That(controller.GetComponent<PlayerInputModeController>().IsBuildMode,
                Is.True, "Selecting a building must keep batch build mode active.");
        }

        [UnityTest]
        public IEnumerator Phase5_RTogglesBeltPathPriorityAfterStart()
        {
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            EcsGridInteractionController interaction =
                Object.FindFirstObjectByType<EcsGridInteractionController>();
            for (int i = 0;
                 i < 120 && !interaction.SelectedBuildingLevel.IsValid;
                 i++)
                yield return null;

            Assert.That(interaction.SelectedKind, Is.EqualTo(BuildingKind.Belt));
            typeof(EcsGridInteractionController).GetField(
                "beltPathStarted", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(interaction, true);
            Assert.That(interaction.IsBeltPathStarted, Is.True);
            bool previous = interaction.UsesHorizontalFirst;

            interaction.RotateSelectionOrToggleBeltPathOrder();

            Assert.That(interaction.UsesHorizontalFirst, Is.Not.EqualTo(previous),
                "R must change horizontal/vertical priority after a belt start.");
        }

        [UnityTest]
        public IEnumerator SimulatedBuild_BypassesPlayerModeAndPlacesBeltPath()
        {
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            EcsGridInteractionController interaction =
                Object.FindFirstObjectByType<EcsGridInteractionController>();
            PlayerInputModeController input =
                Object.FindFirstObjectByType<PlayerInputModeController>();
            Assert.That(input.IsBuildMode, Is.False);

            bool selected = false;
            for (int i = 0; i < 240 && !selected; i++)
            {
                selected = interaction.TrySelectBuildingLevel(
                    new BuildingLevelId { Value = 10 });
                yield return null;
            }
            Assert.That(selected, Is.True,
                "Foundation must become available after SubScene loading.");

            interaction.SimulateHover(new int2(0, 0));
            GameObject preview = null;
            for (int i = 0; i < 240 && preview == null; i++)
            {
                yield return null;
                preview = GameObject.Find("ECS Building Placement Preview");
            }
            Assert.That(preview, Is.Not.Null,
                "Simulated hover must render without player build mode.");

            interaction.SimulatePrimaryClick(new int2(0, 0));
            interaction.SimulatePrimaryClick(new int2(1, 0));
            World world = World.DefaultGameObjectInjectionWorld;
            SurfaceRegistrySystem registry =
                world.GetExistingSystemManaged<SurfaceRegistrySystem>();
            int surfaceCountBeforeBuild = registry?.SurfaceCount ?? 0;
            for (int i = 0;
                 i < 120 && (registry == null ||
                             registry.SurfaceCount < surfaceCountBeforeBuild + 2);
                 i++)
            {
                yield return null;
                registry = world.GetExistingSystemManaged<SurfaceRegistrySystem>();
            }
            Assert.That(registry?.SurfaceCount,
                Is.EqualTo(surfaceCountBeforeBuild + 2));

            Assert.That(interaction.TrySelectBuildingLevel(
                new BuildingLevelId { Value = 4 }), Is.True,
                "The Mk4 belt must remain available after foundation placement.");

            interaction.SimulatePrimaryClick(new int2(0, 0));
            yield return null;
            Assert.That(interaction.IsBeltPathStarted, Is.True,
                "Player-mode checks must not clear a simulated belt path.");

            interaction.SimulateHover(new int2(1, 0));
            yield return null;
            interaction.SimulatePrimaryClick(new int2(1, 0));

            EntityQuery belts = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<BeltTopology>());
            for (int i = 0;
                 i < 120 && belts.CalculateEntityCount() < 2;
                 i++)
            {
                yield return null;
            }

            Assert.That(belts.CalculateEntityCount(), Is.EqualTo(2));
            belts.Dispose();
            interaction.StopSimulatedHover();
        }

        [UnityTest]
        public IEnumerator IncrementalFoundationBatches_ReuseValidChunkRenderMesh()
        {
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            EcsGridInteractionController interaction =
                Object.FindFirstObjectByType<EcsGridInteractionController>();
            bool selected = false;
            for (int i = 0; i < 240 && !selected; i++)
            {
                selected = interaction.TrySelectBuildingLevel(
                    new BuildingLevelId { Value = 10 });
                yield return null;
            }
            Assert.That(selected, Is.True);

            World world = World.DefaultGameObjectInjectionWorld;
            SurfaceRegistrySystem registry =
                world.GetExistingSystemManaged<SurfaceRegistrySystem>();
            int initialSurfaceCount = registry?.SurfaceCount ?? 0;
            EntityQuery initialChunks = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<FoundationRenderChunk>());
            int initialChunkCount = initialChunks.CalculateEntityCount();
            initialChunks.Dispose();
            for (int x = 0; x < 8; x++)
            {
                int2 cell = new int2(100 + x, 100);
                interaction.SimulatePrimaryClick(cell);
                interaction.SimulatePrimaryClick(cell);
                for (int frame = 0;
                     frame < 120 &&
                     (registry == null ||
                      registry.SurfaceCount < initialSurfaceCount + x + 1);
                     frame++)
                {
                    yield return null;
                    registry = world.GetExistingSystemManaged<SurfaceRegistrySystem>();
                }
                Assert.That(registry?.SurfaceCount,
                    Is.EqualTo(initialSurfaceCount + x + 1));
                yield return null;
                yield return null;
            }

            EntityQuery chunks = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<FoundationRenderChunk>());
            Assert.That(chunks.CalculateEntityCount(),
                Is.EqualTo(initialChunkCount + 1),
                "Rebuilding one chunk must reuse one render entity.");
            chunks.Dispose();
        }
    }
}
