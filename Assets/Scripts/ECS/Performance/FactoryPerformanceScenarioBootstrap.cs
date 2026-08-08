using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class FactoryPerformanceScenarioBootstrap : MonoBehaviour
{
    private const string BaseSceneName = "Stage3Ecs";
    private const float SetupTimeoutSeconds = 300f;
    private const string ScaleArgument = "-factoryPerformanceScale";
    private const string NodeCountArgument = "-factoryPerformanceNodeCount";
    private const string LoadPercentArgument = "-factoryPerformanceLoadPercent";
    private const string TimeDelayArgument = "-factoryPerformanceTimeDelay";
    private const string DragSecondsArgument = "-factoryPerformanceDragSeconds";
    private const string DemolitionOrderArgument =
        "-factoryPerformanceDemolitionOrder";

    private static readonly Dictionary<string, FactoryPerformanceScenario>
        ScenarioBySceneName =
            new Dictionary<string, FactoryPerformanceScenario>
            {
                {
                    "Perf_4096_Mk4_HalfLoaded",
                    FactoryPerformanceScenario.Mk4SerpentineHalfLoaded
                },
                {
                    "Perf_F16_Mk4_1024Items",
                    FactoryPerformanceScenario.Mk4F16Branches
                },
                {
                    "Perf_4096_Mk4_Blocking",
                    FactoryPerformanceScenario.Mk4SerpentineBlockedByMk1
                },
                {
                    "Perf_Straight_Scalable",
                    FactoryPerformanceScenario.ScalableStraight
                },
                {
                    "Perf_512_MixedJunction",
                    FactoryPerformanceScenario.MixedJunctions512
                },
                {
                    "Perf_4096_Mk4_FullLoop",
                    FactoryPerformanceScenario.Mk4FullLoop4096
                },
                {
                    "Perf_ProducerConsumer",
                    FactoryPerformanceScenario.ProducerConsumer
                },
                {
                    "Perf_ContinuousBeltBuild",
                    FactoryPerformanceScenario.ContinuousBeltBuild
                }
            };

    private FactoryPerformanceScenarioDefinition definition;
    private string status = "Loading shared Stage 3 scene...";
    private bool ready;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateForPerformanceScene()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!ScenarioBySceneName.TryGetValue(
                activeScene.name,
                out FactoryPerformanceScenario scenario))
        {
            return;
        }

        GameObject bootstrapObject = new GameObject(
            "Factory Performance Scenario Bootstrap");
        DontDestroyOnLoad(bootstrapObject);
        FactoryPerformanceScenarioBootstrap bootstrap =
            bootstrapObject.AddComponent<FactoryPerformanceScenarioBootstrap>();
        string[] arguments = Environment.GetCommandLineArgs();
        int scale = ReadOptionalIntArgument(arguments, ScaleArgument, 0);
        if (scale <= 0)
        {
            scale = ReadOptionalIntArgument(arguments, NodeCountArgument, 0);
        }

        bootstrap.definition = FactoryPerformanceScenarioLayout.Create(
            scenario,
            scale,
            ReadOptionalIntArgument(arguments, LoadPercentArgument, -1));
        bootstrap.definition.TimeDelaySeconds = ReadOptionalFloatArgument(
            arguments,
            TimeDelayArgument,
            0.25f);
        bootstrap.definition.DragSeconds = ReadOptionalFloatArgument(
            arguments,
            DragSecondsArgument,
            0.25f);
        bootstrap.definition.DemolitionOrder = ReadOptionalIntArgument(
            arguments,
            DemolitionOrderArgument,
            0);
        if (bootstrap.definition.DemolitionOrder != 0 &&
            bootstrap.definition.DemolitionOrder != 1)
        {
            throw new ArgumentException(
                "Demolition order must be 0 (forward) or 1 (reverse).");
        }
        bootstrap.StartCoroutine(bootstrap.Setup());
    }

    private IEnumerator Setup()
    {
        if (!SceneManager.GetSceneByName(BaseSceneName).isLoaded)
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(
                BaseSceneName,
                LoadSceneMode.Additive);
            while (load != null && !load.isDone)
            {
                yield return null;
            }
        }

        ConfigurePresentation();
        status = "Waiting for the ECS World and SubScene...";

        float deadline = Time.realtimeSinceStartup + SetupTimeoutSeconds;
        World world = null;
        while ((world == null || !world.IsCreated) &&
               Time.realtimeSinceStartup < deadline)
        {
            world = World.DefaultGameObjectInjectionWorld;
            yield return null;
        }

        if (world == null || !world.IsCreated)
        {
            Fail("The default ECS World did not become available.");
            yield break;
        }

        EntityManager entityManager = world.EntityManager;
        EntityQuery gridQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadWrite<GridDefinition>(),
            ComponentType.ReadWrite<GridBuildCommand>(),
            ComponentType.ReadWrite<GridBuildResult>());
        EntityQuery catalogQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<BuildingPrefabCatalog>(),
            ComponentType.ReadOnly<ItemPrefabEntry>());

        while ((gridQuery.CalculateEntityCount() != 1 ||
                catalogQuery.CalculateEntityCount() != 1) &&
               Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        if (gridQuery.CalculateEntityCount() != 1 ||
            catalogQuery.CalculateEntityCount() != 1)
        {
            gridQuery.Dispose();
            catalogQuery.Dispose();
            Fail("GridDefinition or BuildingPrefabCatalog failed to load.");
            yield break;
        }

        Entity gridEntity = gridQuery.GetSingletonEntity();
        GridDefinition grid =
            entityManager.GetComponentData<GridDefinition>(gridEntity);
        grid.Size = definition.GridSize;
        grid.Origin = float3.zero;
        grid.CellSize = EcsGridUtility.DefaultCellSize;
        grid.Revision++;
        entityManager.SetComponentData(gridEntity, grid);

        EntityQuery placementQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<GridPlacement>());
        if (!placementQuery.IsEmptyIgnoreFilter)
        {
            placementQuery.Dispose();
            gridQuery.Dispose();
            catalogQuery.Dispose();
            Fail("The shared Stage 3 SubScene already contains runtime buildings.");
            yield break;
        }
        placementQuery.Dispose();

        status = "Submitting " + definition.Placements.Length +
                 " build commands...";
        DynamicBuffer<GridBuildCommand> commands =
            entityManager.GetBuffer<GridBuildCommand>(gridEntity);
        for (int i = 0; i < definition.Placements.Length; i++)
        {
            FactoryPerformancePlacement placement = definition.Placements[i];
            commands.Add(new GridBuildCommand
            {
                RequestId = (uint)(i + 1),
                Type = GridBuildCommandType.Place,
                Kind = placement.Kind,
                BuildingLevel = placement.BuildingLevel,
                StartCell = placement.Cell,
                EndCell = placement.Cell,
                QuarterTurns = placement.QuarterTurns
            });
        }

        status = "Building the compact ECS layout...";
        EntityQuery beltQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<BeltTopology>(),
            ComponentType.ReadWrite<BeltState>());
        EntityQuery mergerQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadWrite<Merger>());
        EntityQuery splitterQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadWrite<Splitter>());
        EntityQuery processorQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<ItemProcessor>());
        EntityQuery storageQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<StorageState>());

        while ((beltQuery.CalculateEntityCount() != definition.BeltCount ||
                mergerQuery.CalculateEntityCount() != definition.MergerCount ||
                splitterQuery.CalculateEntityCount() !=
                    definition.SplitterCount ||
                processorQuery.CalculateEntityCount() !=
                    definition.ProcessorCount ||
                storageQuery.CalculateEntityCount() !=
                    definition.StorageCount) &&
               Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        bool buildCountsMatch =
            beltQuery.CalculateEntityCount() == definition.BeltCount &&
            mergerQuery.CalculateEntityCount() == definition.MergerCount &&
            splitterQuery.CalculateEntityCount() == definition.SplitterCount &&
            processorQuery.CalculateEntityCount() == definition.ProcessorCount &&
            storageQuery.CalculateEntityCount() == definition.StorageCount;
        if (!buildCountsMatch)
        {
            DisposeQueries(
                gridQuery,
                catalogQuery,
                beltQuery,
                mergerQuery,
                splitterQuery,
                processorQuery,
                storageQuery);
            Fail("Timed out while building the performance layout.");
            yield break;
        }

        DynamicBuffer<GridBuildResult> results =
            entityManager.GetBuffer<GridBuildResult>(gridEntity);
        int failedBuilds = 0;
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i].Success == 0)
            {
                failedBuilds++;
            }
        }

        if (failedBuilds > 0)
        {
            DisposeQueries(
                gridQuery,
                catalogQuery,
                beltQuery,
                mergerQuery,
                splitterQuery,
                processorQuery,
                storageQuery);
            Fail(failedBuilds + " performance placements were rejected.");
            yield break;
        }
        results.Clear();

        status = "Injecting " + definition.InitialItemCells.Length +
                 " initial items...";
        if (!TryPopulateInitialItems(
                entityManager,
                catalogQuery.GetSingletonEntity(),
                beltQuery,
                mergerQuery,
                splitterQuery,
                out string itemFailure))
        {
            DisposeQueries(
                gridQuery,
                catalogQuery,
                beltQuery,
                mergerQuery,
                splitterQuery,
                processorQuery,
                storageQuery);
            Fail(itemFailure);
            yield break;
        }

        ResetSimulationStats(entityManager);
        DisposeQueries(
            gridQuery,
            catalogQuery,
            beltQuery,
            mergerQuery,
            splitterQuery,
            processorQuery,
            storageQuery);

        ready = true;
        status = "READY - start Profiler capture now";
        Debug.Log(
            "[ECS Performance] READY: " + definition.DisplayName +
            ". Scale=" + definition.Scale + " " + definition.ScaleUnit +
            ", Grid=" + definition.GridSize.x + "x" +
            definition.GridSize.y +
            ", Belts=" + definition.BeltCount +
            ", Mergers=" + definition.MergerCount +
            ", Splitters=" + definition.SplitterCount +
            ", Processors=" + definition.ProcessorCount +
            ", Items=" + definition.InitialItemCells.Length + ".");

        FactoryBuildStressDriver driver = null;
        if (definition.Scenario ==
            FactoryPerformanceScenario.ContinuousBeltBuild)
        {
            driver = FactoryBuildStressDriver.Create(definition);
            driver.Begin();
        }

        FactoryPerformanceMetricsCapture.StartIfRequested(
            definition,
            driver);
    }

    private bool TryPopulateInitialItems(
        EntityManager entityManager,
        Entity catalog,
        EntityQuery beltQuery,
        EntityQuery mergerQuery,
        EntityQuery splitterQuery,
        out string failure)
    {
        DynamicBuffer<ItemPrefabEntry> itemPrefabs =
            entityManager.GetBuffer<ItemPrefabEntry>(catalog, true);
        Entity itemPrefab = Entity.Null;
        ItemId itemType = new ItemId
        {
            Value = FactoryPerformanceScenarioLayout.IronOreItemId
        };
        for (int i = 0; i < itemPrefabs.Length; i++)
        {
            if (itemPrefabs[i].ItemType == itemType)
            {
                itemPrefab = itemPrefabs[i].Prefab;
                break;
            }
        }

        if (itemPrefab == Entity.Null)
        {
            failure = "Iron ore Item Prefab entry is unavailable.";
            return false;
        }
        if (!entityManager.Exists(itemPrefab))
        {
            failure = "Iron ore Item Prefab entity does not exist.";
            return false;
        }
        if (!entityManager.HasComponent<Prefab>(itemPrefab))
        {
            failure = "Iron ore Item Prefab is missing Prefab.";
            return false;
        }
        if (!entityManager.HasComponent<Item>(itemPrefab))
        {
            failure = "Iron ore Item Prefab is missing Item.";
            return false;
        }
        if (!entityManager.HasComponent<LocalTransform>(itemPrefab))
        {
            failure = "Iron ore Item Prefab is missing LocalTransform.";
            return false;
        }
        if (!entityManager.HasComponent<ItemVisualState>(itemPrefab))
        {
            failure = "Iron ore Item Prefab is missing ItemVisualState.";
            return false;
        }

        using NativeArray<Entity> beltEntities =
            beltQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<BeltTopology> beltTopologies =
            beltQuery.ToComponentDataArray<BeltTopology>(Allocator.Temp);
        using NativeArray<BeltState> beltStates =
            beltQuery.ToComponentDataArray<BeltState>(Allocator.Temp);
        using NativeArray<Entity> mergerEntities =
            mergerQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<Merger> mergers =
            mergerQuery.ToComponentDataArray<Merger>(Allocator.Temp);
        using NativeArray<Entity> splitterEntities =
            splitterQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<Splitter> splitters =
            splitterQuery.ToComponentDataArray<Splitter>(Allocator.Temp);
        Dictionary<int2, int> beltIndexByCell =
            new Dictionary<int2, int>(beltTopologies.Length);
        Dictionary<int2, int> mergerIndexByCell =
            new Dictionary<int2, int>(mergers.Length);
        Dictionary<int2, int> splitterIndexByCell =
            new Dictionary<int2, int>(splitters.Length);
        for (int i = 0; i < beltTopologies.Length; i++)
        {
            beltIndexByCell.Add(beltTopologies[i].Cell, i);
        }
        for (int i = 0; i < mergers.Length; i++)
        {
            mergerIndexByCell.Add(mergers[i].Cell, i);
        }
        for (int i = 0; i < splitters.Length; i++)
        {
            splitterIndexByCell.Add(splitters[i].Cell, i);
        }

        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
        LocalTransform prefabTransform =
            entityManager.GetComponentData<LocalTransform>(itemPrefab);
        for (int i = 0; i < definition.InitialItemCells.Length; i++)
        {
            int2 cell = definition.InitialItemCells[i];
            bool isBelt = beltIndexByCell.TryGetValue(cell, out int beltIndex);
            bool isMerger = mergerIndexByCell.TryGetValue(cell, out int mergerIndex);
            bool isSplitter = splitterIndexByCell.TryGetValue(
                cell,
                out int splitterIndex);
            if (!isBelt && !isMerger && !isSplitter)
            {
                ecb.Dispose();
                failure = "No transport entity exists for initial item cell " +
                          cell + ".";
                return false;
            }

            Entity item = ecb.Instantiate(itemPrefab);
            float3 position = new float3(
                cell.x + 0.5f,
                0.535f,
                cell.y + 0.5f);
            ecb.SetComponent(item, new Item
            {
                ItemType = itemType
            });
            ecb.SetComponentEnabled<Item>(item, true);
            ecb.SetComponent(item, new ItemVisualState
            {
                FromPosition = position,
                ToPosition = position,
                Progress = 0f
            });
            LocalTransform itemTransform = prefabTransform;
            itemTransform.Position = position;
            ecb.SetComponent(item, itemTransform);

            if (isBelt)
            {
                BeltState beltState = beltStates[beltIndex];
                beltState.CurrentItem = item;
                beltState.Progress = 0f;
                ecb.SetComponent(beltEntities[beltIndex], beltState);
            }
            else if (isMerger)
            {
                Merger merger = mergers[mergerIndex];
                merger.CurrentItem = item;
                ecb.SetComponent(mergerEntities[mergerIndex], merger);
            }
            else
            {
                Splitter splitter = splitters[splitterIndex];
                splitter.CurrentItem = item;
                ecb.SetComponent(splitterEntities[splitterIndex], splitter);
            }
        }

        ecb.Playback(entityManager);
        ecb.Dispose();
        failure = null;
        return true;
    }

    private static int ReadOptionalIntArgument(
        string[] arguments,
        string name,
        int fallback)
    {
        int index = Array.IndexOf(arguments, name);
        if (index < 0)
        {
            return fallback;
        }

        if (index + 1 >= arguments.Length ||
            !int.TryParse(
                arguments[index + 1],
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value))
        {
            throw new ArgumentException(
                "Invalid integer value for " + name + ".");
        }

        return value;
    }

    private static float ReadOptionalFloatArgument(
        string[] arguments,
        string name,
        float fallback)
    {
        int index = Array.IndexOf(arguments, name);
        if (index < 0)
        {
            return fallback;
        }

        if (index + 1 >= arguments.Length ||
            !float.TryParse(
                arguments[index + 1],
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float value) ||
            value < 0f)
        {
            throw new ArgumentException(
                "Invalid non-negative float value for " + name + ".");
        }

        return value;
    }

    private static void ResetSimulationStats(EntityManager entityManager)
    {
        EntityQuery statsQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadWrite<Stage3SimulationStats>());
        if (statsQuery.CalculateEntityCount() == 1)
        {
            Entity statsEntity = statsQuery.GetSingletonEntity();
            Stage3SimulationStats stats =
                entityManager.GetComponentData<Stage3SimulationStats>(
                    statsEntity);
            stats.TickCount = 0;
            stats.ReadyRequestCount = 0;
            stats.AcceptedTransferCount = 0;
            stats.TotalReadyRequestCount = 0;
            stats.TotalAcceptedTransferCount = 0;
            entityManager.SetComponentData(statsEntity, stats);
        }
        statsQuery.Dispose();
    }

    private void ConfigurePresentation()
    {
        bool keepInteractionEnabled =
            definition.Scenario ==
            FactoryPerformanceScenario.ContinuousBeltBuild;
        EcsGridInteractionController[] interactions =
            FindObjectsByType<EcsGridInteractionController>(
                FindObjectsSortMode.None);
        for (int i = 0; i < interactions.Length; i++)
        {
            interactions[i].enabled = keepInteractionEnabled;
        }

        TopDownPlayerController[] controllers =
            FindObjectsByType<TopDownPlayerController>(
                FindObjectsSortMode.None);
        for (int i = 0; i < controllers.Length; i++)
        {
            controllers[i].enabled = false;
        }

        Stage3PrototypeHud[] huds = FindObjectsByType<Stage3PrototypeHud>(
            FindObjectsSortMode.None);
        for (int i = 0; i < huds.Length; i++)
        {
            huds[i].enabled = false;
        }

        GameObject floor = GameObject.Find("Factory Floor");
        if (floor != null)
        {
            floor.transform.position = new Vector3(
                definition.GridSize.x * 0.5f,
                -0.1f,
                definition.GridSize.y * 0.5f);
            floor.transform.localScale = new Vector3(
                definition.GridSize.x,
                0.2f,
                definition.GridSize.y);
        }

        Camera camera = Camera.main;
        if (camera != null)
        {
            camera.orthographic = true;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 500f;
            camera.transform.position = new Vector3(
                definition.GridSize.x * 0.5f,
                100f,
                definition.GridSize.y * 0.5f);
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            float aspect = math.max(0.5f, camera.aspect);
            camera.orthographicSize = math.max(
                definition.GridSize.y * 0.5f,
                definition.GridSize.x / (2f * aspect)) + 2f;
        }
    }

    private static void DisposeQueries(params EntityQuery[] queries)
    {
        for (int i = 0; i < queries.Length; i++)
        {
            queries[i].Dispose();
        }
    }

    private void Fail(string reason)
    {
        status = "FAILED - " + reason;
        Debug.LogError("[ECS Performance] " + status);
    }

    private void OnGUI()
    {
        if (definition == null)
        {
            return;
        }

        GUI.Box(new Rect(12f, 12f, 720f, 106f), "ECS Performance Scene");
        GUI.Label(new Rect(28f, 40f, 680f, 22f), definition.DisplayName);
        GUI.Label(new Rect(28f, 64f, 680f, 22f), definition.Description);
        GUI.Label(
            new Rect(28f, 88f, 680f, 22f),
            ready ? status : "SETUP - " + status);
    }
}
