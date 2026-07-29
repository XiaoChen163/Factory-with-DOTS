using System.Collections.Generic;
using UnityEngine;

    [DefaultExecutionOrder(-1000)]
    public sealed class Stage0Prototype : MonoBehaviour
    {
        private const int GridWidth = 32;
        private const int GridHeight = 32;

        private GridMap grid;
        private BuildController buildController;
        private GameManager gameManager;
        private BeltSimulation beltSimulation;
        private BeltTransferVisualSystem beltTransferVisualSystem;
        private ItemData ore;
        private ItemData ingot;
        private RecipeData recipe;
        private Storage demoStorage;
        private Furnace demoFurnace;
        private readonly List<GridBuilding> demoBuildings = new List<GridBuilding>();

        private void Awake()
        {
            Application.targetFrameRate = 60;
            CreatePrototypeData();
            Camera gameCamera = CreateCamera();
            CreateLighting();

            grid = new GridMap(GridWidth, GridHeight, Vector3.zero);
            gameManager = gameObject.AddComponent<GameManager>();
            beltSimulation = gameObject.AddComponent<BeltSimulation>();
            beltSimulation.Initialize(gameManager, grid);
            beltTransferVisualSystem = gameObject.AddComponent<BeltTransferVisualSystem>();
            beltTransferVisualSystem.Initialize(beltSimulation);

            GameObject gridObject = new GameObject("Grid Debug View");
            gridObject.AddComponent<GridDebugView>().Build(grid);

            buildController = gameObject.AddComponent<BuildController>();
            buildController.Initialize(gameCamera, grid, ore, recipe);

            CreateDemoLine();
            CreateSplitterDemo();
            CreateMergerDemo();
            CreateFullLoopDemo();
        }

        private void OnDestroy()
        {
            if (ore != null) Destroy(ore);
            if (ingot != null) Destroy(ingot);
            if (recipe != null) Destroy(recipe);
        }

        private void OnGUI()
        {
            GUI.Box(new Rect(12f, 12f, 560f, 238f), "Factory With DOTS — Stage 2 Loops & Junctions");
            GUI.Label(new Rect(28f, 42f, 400f, 22f), "WASD Move   |   Hold RMB + mouse: rotate view around Y");
            GUI.Label(new Rect(28f, 64f, 480f, 22f), "1 Belt   2 Miner   3 Furnace   4 Storage   5 Merger   6 Splitter");
            GUI.Label(new Rect(28f, 86f, 400f, 22f), "Selected: " + buildController.SelectedKind +
                (buildController.HasHoveredCell ? "   Cell: " + buildController.HoveredCell : ""));

            int stored = demoStorage == null ? 0 : demoStorage.GetCount(ingot);
            string furnaceState = demoFurnace == null
                ? "removed"
                : demoFurnace.IsCrafting
                    ? "crafting " + Mathf.RoundToInt(demoFurnace.CraftProgress * 100f) + "%"
                    : "waiting (buffer " + demoFurnace.BufferedInputs + ")";
            GUI.Label(new Rect(28f, 108f, 520f, 22f),
                buildController.SelectedKind == BuildingKind.Belt
                    ? buildController.IsBeltPathStarted
                        ? "Belt: click end   |   R: switch route   |   Esc: cancel"
                        : "Belt: click start   |   R: rotate initial direction"
                    : "Left click: build   |   R: rotate   |   F: remove under mouse");
            GUI.Label(new Rect(28f, 130f, 400f, 22f), "Furnace: " + furnaceState + "   |   Stored: " + stored + " ingot(s)");
            GUI.Label(new Rect(28f, 152f, 430f, 22f), "Logic: 60 Hz fixed Tick   |   Tick #" +
                (gameManager == null ? 0 : gameManager.LogicTickCount));
            GUI.Label(new Rect(28f, 174f, 480f, 22f), "Detected belt loops: " +
                (beltSimulation == null ? 0 : beltSimulation.LoopCount) +
                "   |   Last transfers: " +
                (beltSimulation == null ? 0 : beltSimulation.LastAcceptedCount));
            GUI.Label(new Rect(28f, 196f, 480f, 22f),
                "Yellow edge = disconnected   |   Corner triangle points 45° toward exit");
            GUI.Label(new Rect(28f, 218f, 520f, 22f), "Belt route: " +
                buildController.BeltPathMode +
                (buildController.IsBeltPathStarted
                    ? "   |   Start: " + buildController.BeltPathStart
                    : "   |   Initial: " + buildController.BeltInitialDirection));
        }

        private void CreatePrototypeData()
        {
            ore = ScriptableObject.CreateInstance<ItemData>();
            ore.name = "Iron Ore";
            ore.ConfigureForPrototype("Iron Ore", new Color(0.48f, 0.58f, 0.70f));

            ingot = ScriptableObject.CreateInstance<ItemData>();
            ingot.name = "Iron Ingot";
            ingot.ConfigureForPrototype("Iron Ingot", new Color(0.92f, 0.72f, 0.26f));

            recipe = ScriptableObject.CreateInstance<RecipeData>();
            recipe.name = "Smelt Iron";
            recipe.ConfigureForPrototype(ore, 2, ingot, 1, 2f);
        }

        private Camera CreateCamera()
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera gameCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            gameCamera.transform.position = new Vector3(8f, 13.5f, -7.5f);
            gameCamera.transform.LookAt(new Vector3(8f, 0f, 4.5f));
            gameCamera.fieldOfView = 48f;
            gameCamera.clearFlags = CameraClearFlags.SolidColor;
            gameCamera.backgroundColor = new Color(0.035f, 0.05f, 0.07f);
            cameraObject.AddComponent<TopDownPlayerController>();
            return gameCamera;
        }

        private static void CreateLighting()
        {
            GameObject lightObject = new GameObject("Directional Light");
            Light directionalLight = lightObject.AddComponent<Light>();
            directionalLight.type = LightType.Directional;
            directionalLight.intensity = 1.4f;
            directionalLight.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.45f, 0.50f);
        }

        private void CreateDemoLine()
        {
            Miner miner = buildController.TryBuild(BuildingKind.Miner, new Vector2Int(1, 4), 0) as Miner;
            AddDemo(miner);

            for (int x = 3; x <= 6; x++)
            {
                AddDemo(buildController.TryBuild(BuildingKind.Belt, new Vector2Int(x, 4), 0));
            }

            demoFurnace = buildController.TryBuild(BuildingKind.Furnace, new Vector2Int(7, 4), 0) as Furnace;
            AddDemo(demoFurnace);

            for (int x = 9; x <= 12; x++)
            {
                AddDemo(buildController.TryBuild(BuildingKind.Belt, new Vector2Int(x, 4), 0));
            }

            demoStorage = buildController.TryBuild(BuildingKind.Storage, new Vector2Int(13, 4), 0) as Storage;
            AddDemo(demoStorage);
        }

        private void AddDemo(GridBuilding building)
        {
            if (building != null)
            {
                demoBuildings.Add(building);
            }
        }

        private void CreateSplitterDemo()
        {
            Miner miner = buildController.TryBuild(
                BuildingKind.Miner,
                new Vector2Int(1, 10),
                0) as Miner;
            AddDemo(miner);
            AddDemo(buildController.TryBuild(BuildingKind.Belt, new Vector2Int(3, 10), 0));
            AddDemo(buildController.TryBuild(BuildingKind.Splitter, new Vector2Int(4, 10), 0));

            AddDemo(buildController.TryBuild(BuildingKind.Belt, new Vector2Int(5, 10), 0));
            AddDemo(buildController.TryBuild(BuildingKind.Storage, new Vector2Int(6, 10), 0));

            AddDemo(buildController.TryBuild(BuildingKind.Belt, new Vector2Int(4, 11), 1));
            AddDemo(buildController.TryBuild(BuildingKind.Storage, new Vector2Int(4, 12), 1));

            AddDemo(buildController.TryBuild(BuildingKind.Belt, new Vector2Int(4, 9), 3));
            AddDemo(buildController.TryBuild(BuildingKind.Storage, new Vector2Int(4, 8), 3));
        }

        private void CreateMergerDemo()
        {
            AddDemo(buildController.TryBuild(BuildingKind.Merger, new Vector2Int(10, 10), 0));

            Miner westMiner = buildController.TryBuild(
                BuildingKind.Miner,
                new Vector2Int(8, 10),
                0) as Miner;
            Miner southMiner = buildController.TryBuild(
                BuildingKind.Miner,
                new Vector2Int(10, 8),
                1) as Miner;
            Miner northMiner = buildController.TryBuild(
                BuildingKind.Miner,
                new Vector2Int(10, 12),
                3) as Miner;
            AddDemo(westMiner);
            AddDemo(southMiner);
            AddDemo(northMiner);

            AddDemo(buildController.TryBuild(BuildingKind.Belt, new Vector2Int(11, 10), 0));
            AddDemo(buildController.TryBuild(BuildingKind.Storage, new Vector2Int(12, 10), 0));
        }

        private void CreateFullLoopDemo()
        {
            BeltLogic east = buildController.TryBuild(
                BuildingKind.Belt,
                new Vector2Int(17, 4),
                0) as BeltLogic;
            BeltLogic north = buildController.TryBuild(
                BuildingKind.Belt,
                new Vector2Int(18, 4),
                1) as BeltLogic;
            BeltLogic west = buildController.TryBuild(
                BuildingKind.Belt,
                new Vector2Int(18, 5),
                2) as BeltLogic;
            BeltLogic south = buildController.TryBuild(
                BuildingKind.Belt,
                new Vector2Int(17, 5),
                3) as BeltLogic;

            BeltLogic[] loop = { east, north, west, south };
            for (int i = 0; i < loop.Length; i++)
            {
                BeltLogic belt = loop[i];
                AddDemo(belt);
                if (belt != null)
                {
                    belt.SetItemForPrototype(new ItemState(ore));
                }
            }
        }
    }
