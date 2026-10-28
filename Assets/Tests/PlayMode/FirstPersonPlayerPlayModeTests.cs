using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Factory.Tests
{
    public sealed class FirstPersonPlayerPlayModeTests
    {
        private Keyboard keyboard;
        private FirstPersonPlayerController player;
        private InputSettings.BackgroundBehavior previousBackground;
        private InputSettings.EditorInputBehaviorInPlayMode previousEditorInput;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousBackground = InputSystem.settings.backgroundBehavior;
            previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            keyboard = InputSystem.AddDevice<Keyboard>();
            player = Object.FindFirstObjectByType<FirstPersonPlayerController>();
            Assert.That(player, Is.Not.Null);
            typeof(FirstPersonPlayerController).GetField("focused", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, true);
            for (int frame = 0; frame < 240 && !player.IsSpawnFoundationReady; frame++) yield return null;
            World spawnWorld = World.DefaultGameObjectInjectionWorld;
            Assert.That(player.IsSpawnFoundationReady, Is.True,
                "Spawn foundations and their colliders must finish loading. Surface count: " +
                spawnWorld.GetExistingSystemManaged<SurfaceRegistrySystem>().SurfaceCount + ", collider count: " +
                spawnWorld.GetExistingSystemManaged<FoundationPhysicsChunkSystem>().ActiveColliderBlobCount);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior = previousBackground;
            InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
            yield return null;
        }

        private IEnumerator Keys(params Key[] keys)
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
            yield return null;
            yield return new WaitForSeconds(0.025f);
            Assert.That(player.IsControlling || Object.FindFirstObjectByType<PlayerInputModeController>().RequiresPointer, Is.True,
                "The synthetic test keyboard must be routed to the active first-person player.");
        }

        private IEnumerator ToggleFlight()
        {
            yield return Keys(Key.Space);
            yield return Keys();
            yield return Keys(Key.Space);
            yield return Keys();
        }

        [UnityTest]
        public IEnumerator Spawn_CreatesTenByTenEcsFoundationOnce_WithoutFactoryFloor()
        {
            Assert.That(GameObject.Find("Factory Floor"), Is.Null);
            World world = World.DefaultGameObjectInjectionWorld;
            SurfaceRegistrySystem surfaces = world.GetExistingSystemManaged<SurfaceRegistrySystem>();
            using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldGridConfig>());
            GridCell foot = EcsGridUtility.WorldToCell((Unity.Mathematics.float3)player.transform.position,
                query.GetSingleton<WorldGridConfig>());
            for (int z = foot.Z - 5; z < foot.Z + 5; z++)
            for (int x = foot.X - 5; x < foot.X + 5; x++)
                Assert.That(surfaces.HasFoundationVoxel(new GridCell(x, 0, z)), Is.True);
            Assert.That(surfaces.HasFoundationVoxel(new GridCell(foot.X - 6, 0, foot.Z)), Is.False);
            Assert.That(surfaces.HasFoundationVoxel(new GridCell(foot.X + 5, 0, foot.Z)), Is.False);
            int count = surfaces.SurfaceCount;
            yield return new WaitForSeconds(0.15f);
            Assert.That(surfaces.SurfaceCount, Is.EqualTo(count));
        }

        [UnityTest]
        public IEnumerator Escape_LeavesBuildModeCaptured_ThenReleasesPointerInNormalMode()
        {
#if UNITY_EDITOR
            var gameViewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var gameView = UnityEditor.EditorWindow.GetWindow(gameViewType);
            gameView.Focus();
            yield return null;
            typeof(FirstPersonPlayerController).GetField("focused", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(player, true);
#endif
            PlayerInputModeController input = Object.FindFirstObjectByType<PlayerInputModeController>();
            GameUiController ui = Object.FindFirstObjectByType<GameUiController>();
            ui.HandleBuildCatalogToggle();
            ui.CloseBuildCatalog();
            Assert.That(input.IsBuildMode, Is.True);
#if UNITY_EDITOR
            gameView.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
#endif
            yield return Keys(Key.Escape);
            yield return Keys();
            Assert.That(input.IsBuildMode, Is.False);
            Assert.That(input.IsPointerReleased, Is.False);
            Assert.That(player.IsControlling, Is.True);
#if UNITY_EDITOR
            Assert.That(gameViewType.GetMethod("AllowCursorLockAndHide", BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null);
#endif
            // Hidden batch Editors retain the native Focus restriction after receiving a Game view event.
            // Keep that restriction intact; OS cursor locking is checked only in an interactive Editor.
            if (!Application.isBatchMode)
                Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.Locked));
            Assert.That(Cursor.visible, Is.False);
#if UNITY_EDITOR
            gameView.SendEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Escape });
#endif
            yield return Keys(Key.Escape);
            yield return Keys();
            Assert.That(input.IsPointerReleased, Is.True);
            Assert.That(player.IsControlling, Is.False);
            Assert.That(Cursor.lockState, Is.EqualTo(CursorLockMode.None));
            Assert.That(Cursor.visible, Is.True);
        }

        [UnityTest]
        public IEnumerator SingleSpace_JumpsAndReturnsToGround_WithoutEnteringFlight()
        {
            yield return new WaitForSeconds(0.2f);
            Assert.That(player.GetComponent<EcsCharacterMotor>().IsGrounded, Is.True);
            yield return Keys(Key.Space);
            yield return new WaitForSeconds(0.15f);
            Assert.That(player.IsFlying, Is.False);
            Assert.That(player.transform.position.y, Is.GreaterThan(0.4f));
            yield return Keys();
            yield return new WaitForSeconds(1f);
            Assert.That(player.GetComponent<EcsCharacterMotor>().IsGrounded, Is.True);
            Assert.That(player.transform.position.y, Is.EqualTo(0f).Within(0.08f));
        }

        [UnityTest]
        public IEnumerator Scene_HasRobotAnimatorAndEyeCamera()
        {
            Assert.That(player.GetComponent<UnityEngine.CharacterController>(), Is.Null);
            Assert.That(player.GetComponent<EcsCharacterMotor>().IsCreated, Is.True);
            Animator animator = player.GetComponentInChildren<Animator>();
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.avatar.isHuman, Is.True);
            Assert.That(animator.runtimeAnimatorController, Is.Not.Null);
            Assert.That(animator.applyRootMotion, Is.False);
            Assert.That(animator.GetComponentInChildren<SkinnedMeshRenderer>(), Is.Not.Null);
            Assert.That(player.ViewCamera.transform.parent, Is.EqualTo(player.transform));
            Assert.That(player.ViewCamera.transform.localPosition.y, Is.EqualTo(1.62f).Within(0.01f));
            yield return new WaitForSeconds(0.2f);
            Assert.That(player.GetComponent<EcsCharacterMotor>().IsGrounded, Is.True);
            Assert.That(player.transform.position.y, Is.EqualTo(0f).Within(0.08f));
        }

        [UnityTest]
        public IEnumerator Flight_ShiftLandingExitsFlight_ForEitherShiftKey()
        {
            foreach (Key shift in new[] { Key.LeftShift, Key.RightShift })
            {
                yield return ToggleFlight();
                Assert.That(player.IsFlying, Is.True);
                yield return Keys(Key.Space);
                yield return new WaitForSeconds(0.2f);
                yield return Keys();
                Assert.That(player.transform.position.y, Is.GreaterThan(1f));
                yield return Keys(shift);
                for (int frame = 0; frame < 180 && player.IsFlying; frame++) yield return null;
                Assert.That(player.IsFlying, Is.False, shift + " must exit flight after landing.");
                Assert.That(player.GetComponent<EcsCharacterMotor>().IsGrounded, Is.True);
                Assert.That(player.VerticalVelocity, Is.LessThanOrEqualTo(0f));
                yield return new WaitForSeconds(0.1f);
                Assert.That(player.transform.position.y, Is.EqualTo(0f).Within(0.08f));
                yield return Keys();
            }
            // After auto-landing, moving above the platform must produce a normal gravitational fall.
            player.GetComponent<EcsCharacterMotor>().Teleport(player.transform.position + Vector3.up * 2f);
            float height = player.transform.position.y;
            yield return new WaitForSeconds(0.15f);
            Assert.That(player.transform.position.y, Is.LessThan(height - 0.1f));
            Assert.That(player.IsFlying, Is.False);
        }

        [UnityTest]
        public IEnumerator Flight_HoveringOrOpposingVerticalInputs_DoesNotExitFlightNearGround()
        {
            yield return ToggleFlight();
            Assert.That(player.IsFlying, Is.True);
            Vector3 position = player.transform.position;
            position.y = 0.02f;
            player.GetComponent<EcsCharacterMotor>().Teleport(position);
            yield return Keys();
            Assert.That(player.transform.position.y, Is.EqualTo(0.02f).Within(0.03f));
            Assert.That(player.IsFlying, Is.True);
            yield return Keys(Key.Space, Key.LeftShift);
            yield return new WaitForSeconds(0.1f);
            Assert.That(player.IsFlying, Is.True, "Space and Shift cancel vertical movement, so this is not a landing.");
        }

        [UnityTest]
        public IEnumerator DoubleTap_TogglesFlight_HoversAscendsDescendsAndFalls()
        {
            yield return ToggleFlight();
            Assert.That(player.IsFlying, Is.True);
            float height = player.transform.position.y;
            yield return new WaitForSeconds(0.15f);
            Assert.That(player.transform.position.y, Is.EqualTo(height).Within(0.02f));
            yield return Keys(Key.Space);
            yield return new WaitForSeconds(0.12f);
            yield return Keys();
            float upper = player.transform.position.y;
            Assert.That(upper, Is.GreaterThan(height + 0.5f));
            yield return Keys(Key.LeftShift);
            yield return new WaitForSeconds(0.04f);
            yield return Keys();
            Assert.That(player.transform.position.y, Is.LessThan(upper));
            // Wait beyond the previous ascent press before starting a new double tap.
            yield return new WaitForSeconds(0.31f);
            yield return ToggleFlight();
            Assert.That(player.IsFlying, Is.False);
            Assert.That(player.VerticalVelocity, Is.LessThan(0f));
            float falling = player.transform.position.y;
            yield return new WaitForSeconds(0.12f);
            Assert.That(player.transform.position.y, Is.LessThan(falling));
        }

        [UnityTest]
        public IEnumerator Ctrl_AcceleratesWalkingAndFlight_AnimationFollowsMovement()
        {
            yield return Keys(Key.W);
            yield return new WaitForSeconds(0.3f);
            Animator animator = player.GetComponentInChildren<Animator>();
            float walk = animator.GetFloat("Speed");
            Assert.That(walk, Is.GreaterThan(2f));
            yield return Keys(Key.W, Key.LeftCtrl);
            yield return new WaitForSeconds(0.3f);
            float run = animator.GetFloat("Speed");
            Assert.That(run, Is.GreaterThan(walk + 1f));
            yield return Keys();
            yield return ToggleFlight();
            yield return Keys(Key.W);
            yield return new WaitForSeconds(0.7f);
            float fly = animator.GetFloat("Speed");
            Assert.That(fly, Is.GreaterThan(run + 2f));
            yield return Keys(Key.W, Key.RightCtrl);
            yield return new WaitForSeconds(0.5f);
            Assert.That(animator.GetFloat("Speed"), Is.GreaterThan(fly + 3f));
        }

        [UnityTest]
        public IEnumerator Backpack_BlocksMovementAndPreventsFlightTapAcrossUi()
        {
            yield return Keys(Key.Space);
            yield return Keys();
            PlayerInputModeController input = Object.FindFirstObjectByType<PlayerInputModeController>();
            GameUiController ui = Object.FindFirstObjectByType<GameUiController>();
            ui.HandleBackpackToggle();
            yield return null;
            Assert.That(input.RequiresPointer, Is.True);
            Assert.That(player.IsControlling, Is.False);
            Assert.That(Cursor.visible, Is.True);
            yield return Keys(Key.Space, Key.W);
            Assert.That(player.IsFlying, Is.False);
            yield return Keys();
            ui.CloseBackpack();
            yield return null;
            Assert.That(player.IsControlling, Is.True);
            yield return Keys(Key.Space);
            Assert.That(player.IsFlying, Is.False);
        }

        [UnityTest]
        public IEnumerator Motor_LandsOnDotsFoundationInsteadOfPassingThrough()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldGridConfig>());
            for (int i = 0; i < 120 && query.CalculateEntityCount() == 0; i++) yield return null;
            Assert.That(query.CalculateEntityCount(), Is.EqualTo(1));
            SurfaceRegistrySystem surfaces = world.GetExistingSystemManaged<SurfaceRegistrySystem>();
            Entity grid = query.GetSingletonEntity();
            surfaces.AddFoundation(grid, new GridCell(5, 4, 5), 0, SurfacePermission.All, true);
            yield return new WaitForSeconds(0.15f);
            WorldGridConfig config = query.GetSingleton<WorldGridConfig>();
            float surfaceY = EcsGridUtility.LevelToWorldY(4, config);
            player.GetComponent<EcsCharacterMotor>().Teleport(new Vector3(5.5f, surfaceY + 2f, 5.5f));
            yield return new WaitForSeconds(0.8f);
            Assert.That(player.GetComponent<EcsCharacterMotor>().IsGrounded, Is.True);
            Assert.That(player.transform.position.y, Is.EqualTo(surfaceY).Within(0.08f));
        }
    }
}
