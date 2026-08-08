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
    }
}
