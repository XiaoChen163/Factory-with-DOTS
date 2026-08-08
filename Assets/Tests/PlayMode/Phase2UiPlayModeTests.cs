using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

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
    }
}
