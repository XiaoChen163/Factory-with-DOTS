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
using Unity.Physics;
using Unity.Transforms;

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

            GameObject previewCell = GameObject.Find("Placement Preview Cell 0");
            Assert.That(previewCell, Is.Not.Null);
            World world = World.DefaultGameObjectInjectionWorld;
            EntityQuery worldGridQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<WorldGridConfig>());
            WorldGridConfig worldGrid = worldGridQuery.GetSingleton<WorldGridConfig>();
            worldGridQuery.Dispose();
            float3 expectedPreviewCenter = EcsGridUtility.CellToWorldCenter(
                new GridCell(0, 0, 0),
                worldGrid);
            expectedPreviewCenter.y -= worldGrid.LayerHeight * 0.5f;
            Assert.That(previewCell.transform.position.x,
                Is.EqualTo(expectedPreviewCenter.x).Within(0.0001f));
            Assert.That(previewCell.transform.position.y,
                Is.EqualTo(expectedPreviewCenter.y).Within(0.0001f));
            Assert.That(previewCell.transform.position.z,
                Is.EqualTo(expectedPreviewCenter.z).Within(0.0001f));
            Assert.That(previewCell.transform.localScale,
                Is.EqualTo(new Vector3(
                    worldGrid.CellSize,
                    worldGrid.LayerHeight,
                    worldGrid.CellSize)));

            interaction.SimulatePrimaryClick(new GridCell(0, 0, 0));
            interaction.SimulateHover(new GridCell(1, 3, 0));
            yield return null;
            GameObject secondPreviewCell =
                GameObject.Find("Placement Preview Cell 1");
            Assert.That(secondPreviewCell, Is.Not.Null);
            Assert.That(secondPreviewCell.transform.position.y,
                Is.EqualTo(expectedPreviewCenter.y).Within(0.0001f),
                "The second foundation corner must stay on the first corner's plane.");
            interaction.SimulatePrimaryClick(new GridCell(1, 3, 0));
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

            EntityQuery belts = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<BeltTopology>());
            int beltCountBeforeBuild = belts.CalculateEntityCount();

            interaction.SimulatePrimaryClick(new int2(0, 0));
            yield return null;
            Assert.That(interaction.IsBeltPathStarted, Is.True,
                "Player-mode checks must not clear a simulated belt path.");

            interaction.SimulateHover(new int2(1, 0));
            yield return null;
            interaction.SimulatePrimaryClick(new int2(1, 0));

            for (int i = 0;
                 i < 120 &&
                 belts.CalculateEntityCount() < beltCountBeforeBuild + 2;
                 i++)
            {
                yield return null;
            }

            Assert.That(belts.CalculateEntityCount(),
                Is.EqualTo(beltCountBeforeBuild + 2));
            belts.Dispose();
            interaction.StopSimulatedHover();
        }

        [UnityTest]
        public IEnumerator RampSelection_PlacesStraightConnectedLineAboveFoundation()
        {
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            EcsGridInteractionController interaction =
                Object.FindFirstObjectByType<EcsGridInteractionController>();
            bool selected = false;
            for (int i = 0; i < 240 && !selected; i++)
            {
                selected = interaction.TrySelectBuildingLevel(
                    new BuildingLevelId { Value = 12 });
                yield return null;
            }
            Assert.That(selected, Is.True);
            Assert.That(interaction.SelectedKind,
                Is.EqualTo(BuildingKind.RampFoundation));

            World world = World.DefaultGameObjectInjectionWorld;
            SurfaceRegistrySystem surfaces =
                world.GetExistingSystemManaged<SurfaceRegistrySystem>();
            EntityQuery gridQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<GridDefinition>());
            Entity gridEntity = gridQuery.GetSingletonEntity();
            gridQuery.Dispose();
            if (!surfaces.HasFoundationVoxel(new GridCell(2, 0, 2)))
            {
                surfaces.AddFoundation(
                    gridEntity,
                    new GridCell(2, 0, 2),
                    0,
                    SurfacePermission.All,
                    true);
            }
            Assert.That(surfaces.HasFoundationVoxel(new GridCell(2, 0, 2)),
                Is.True,
                "The ramp test must place on the existing level-zero foundation.");

            interaction.SimulatePrimaryClick(new GridCell(2, 0, 2));
            Assert.That(interaction.IsRampLineStarted, Is.True);
            interaction.SimulateHover(new GridCell(4, 7, 3));
            yield return null;
            interaction.SimulatePrimaryClick(new GridCell(4, 7, 3));

            RampRegistrySystem ramps =
                world.GetExistingSystemManaged<RampRegistrySystem>();
            for (int i = 0; i < 120 &&
                 (ramps == null ||
                  !ramps.ContainsCell(new GridCell(4, 1, 2))); i++)
            {
                yield return null;
                ramps = world.GetExistingSystemManaged<RampRegistrySystem>();
            }

            Assert.That(interaction.IsRampLineStarted, Is.False);
            Assert.That(ramps.TryGet(
                new GridCell(2, 0, 2), out RampRegistrySystem.Record first),
                Is.True);
            Assert.That(ramps.TryGet(
                new GridCell(3, 0, 2), out RampRegistrySystem.Record second),
                Is.True);
            Assert.That(ramps.TryGet(
                new GridCell(4, 1, 2), out RampRegistrySystem.Record third),
                Is.True);
            Assert.That(first.Connector.LowHeight.Units, Is.EqualTo(0));
            Assert.That(second.Connector.LowHeight.Units, Is.EqualTo(4));
            Assert.That(third.Connector.LowHeight.Units, Is.EqualTo(8));
            Assert.That(first.Connector.UphillDirection, Is.EqualTo(new int2(1, 0)));
            Assert.That(surfaces.HasFoundationVoxel(new GridCell(2, 0, 2)),
                Is.True,
                "A ramp above a foundation must not replace the foundation voxel.");

            yield return new WaitForFixedUpdate();
            EntityQuery physicsQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PhysicsWorldSingleton>());
            Assert.That(physicsQuery.CalculateEntityCount(), Is.EqualTo(1));
            PhysicsWorldSingleton physicsWorld = physicsQuery.GetSingleton<
                PhysicsWorldSingleton>();
            WorldGridConfig worldGrid = world.EntityManager.GetComponentData<
                WorldGridConfig>(gridEntity);
            float3 rampCenter = RampUtility.GetSurfaceCenter(
                first.Connector, worldGrid);
            RaycastInput ray = new RaycastInput
            {
                Start = rampCenter + new float3(0f, 10f, 0f),
                End = rampCenter - new float3(0f, 10f, 0f),
                Filter = FoundationCollisionCategories.FoundationQueryFilter
            };
            Assert.That(physicsWorld.CastRay(ray, out Unity.Physics.RaycastHit hit),
                Is.True,
                "The slope collider must block the grid picking ray.");
            Assert.That(EcsGridInteractionController.TryResolveRampPhysicsHit(
                    world.EntityManager, hit.Entity, out RampConnector raycastRamp),
                Is.True,
                "A slope collider hit must resolve to its RampConnector.");
            Assert.That(raycastRamp.Cell, Is.EqualTo(first.Connector.Cell));
            physicsQuery.Dispose();

            Assert.That(interaction.TrySelectBuildingLevel(
                new BuildingLevelId { Value = 1 }), Is.True);
            interaction.SimulateHover(first.Connector.Cell);
            yield return null;
            interaction.SimulatePrimaryClick(first.Connector.Cell);
            for (int i = 0; i < 120; i++)
            {
                yield return null;
                if (ramps.TryGet(first.Connector.Cell, out first) &&
                    first.BeltEntity != Entity.Null &&
                    world.EntityManager.Exists(first.BeltEntity))
                    break;
            }

            Assert.That(first.BeltEntity, Is.Not.EqualTo(Entity.Null));
            Assert.That(world.EntityManager.HasComponent<RampBelt>(first.BeltEntity),
                Is.True);
            RampBelt rampBelt = world.EntityManager.GetComponentData<RampBelt>(
                first.BeltEntity);
            Assert.That(rampBelt.TravelDirection, Is.EqualTo(new int2(1, 0)));
            LocalTransform beltTransform = world.EntityManager.GetComponentData<
                LocalTransform>(first.BeltEntity);
            float3 expectedCenter = RampUtility.GetSurfaceCenter(
                first.Connector, worldGrid);
            Assert.That(math.distance(beltTransform.Position, expectedCenter),
                Is.LessThan(0.001f),
                "The ramp belt must be positioned at the slope surface center.");
            PostTransformMatrix beltScale = world.EntityManager.GetComponentData<
                PostTransformMatrix>(first.BeltEntity);
            float expectedLengthScale = RampUtility.GetSlopeLength(
                first.Connector, worldGrid) / worldGrid.CellSize;
            Assert.That(beltScale.Value.c0.x,
                Is.EqualTo(expectedLengthScale).Within(0.001f),
                "The ramp belt must scale along its travel axis to the slope length.");
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
