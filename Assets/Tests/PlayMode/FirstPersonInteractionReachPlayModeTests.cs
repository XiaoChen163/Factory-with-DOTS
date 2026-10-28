using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Factory.Tests
{
    public sealed class FirstPersonInteractionReachPlayModeTests
    {
        private EcsGridInteractionController interaction;
        private Camera camera;
        private World world;
        private Entity grid;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            var player = Object.FindFirstObjectByType<FirstPersonPlayerController>();
            for (int frame = 0; frame < 240 && !player.IsSpawnFoundationReady; frame++) yield return null;
            Assert.That(player.IsSpawnFoundationReady, Is.True);
            player.enabled = false;
            interaction = Object.FindFirstObjectByType<EcsGridInteractionController>();
            camera = interaction.GetComponent<Camera>();
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 1000f;
            world = World.DefaultGameObjectInjectionWorld;
            using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldGridConfig>());
            grid = query.GetSingletonEntity();
        }

        [UnityTest]
        public IEnumerator ConstructionPlanes_RespectFiftyMetersFromCamera_InEveryPickingMode()
        {
            for (int frame = 0; frame < 120 && !interaction.TrySelectBuildingLevel(new BuildingLevelId { Value = 10 }); frame++)
                yield return null;
            Assert.That(interaction.SelectedKind, Is.EqualTo(BuildingKind.Foundation));
            for (int mode = 0; mode < 3; mode++)
            {
                interaction.CancelPlacementGesture();
                interaction.StopSimulatedHover();
                if (mode == 1) interaction.SelectGridLevel(0);
                else interaction.UseFirstHitLayer();
                if (mode == 2)
                {
                    interaction.SimulatePrimaryClick(new GridCell(1000, 0, 1000));
                    Assert.That(interaction.IsFoundationAreaStarted, Is.True);
                }
                AimDownFrom(49.9f);
                Assert.That(RaycastGrid(true), Is.True, "Near plane, picking mode " + mode);
                AimDownFrom(50f);
                Assert.That(RaycastGrid(true), Is.True, "Boundary plane, picking mode " + mode);
                AimDownFrom(50.1f);
                Assert.That(RaycastGrid(true), Is.False, "Far plane, picking mode " + mode);
            }
        }

        [UnityTest]
        public IEnumerator DotsSurfaceRay_RejectsFarTarget_DespiteLongCameraFarClip()
        {
            SurfaceRegistrySystem surfaces = world.GetExistingSystemManaged<SurfaceRegistrySystem>();
            surfaces.AddFoundation(grid, new GridCell(1000, 0, 1000), 0, SurfacePermission.All, true);
            yield return new WaitForSeconds(0.1f);
            interaction.UseFirstHitLayer();
            AimDownFrom(49.9f);
            Assert.That(RaycastGrid(false), Is.True);
            AimDownFrom(50.1f);
            Assert.That(RaycastGrid(false), Is.False);
        }

        private void AimDownFrom(float height)
        {
            camera.transform.position = new Vector3(1000.5f, height, 1000.5f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        }

        private bool RaycastGrid(bool foundationPlacement)
        {
            // Exercise the shared picking path used by placement, demolition and building selection.
            object[] arguments = { null, default(WorldGridConfig), Entity.Null, default(GridCell),
                Vector3.zero, false, default(GridSurfaceHit), foundationPlacement, false };
            return (bool)typeof(EcsGridInteractionController).GetMethod("TryRaycastGrid",
                BindingFlags.Instance | BindingFlags.NonPublic).Invoke(interaction, arguments);
        }
    }
}
