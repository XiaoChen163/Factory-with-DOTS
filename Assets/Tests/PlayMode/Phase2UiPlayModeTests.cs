using System.Collections;
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
    }
}
