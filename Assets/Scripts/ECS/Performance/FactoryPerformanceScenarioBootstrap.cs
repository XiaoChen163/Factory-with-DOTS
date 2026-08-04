using System.Collections;
using System.Collections.Generic;
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
        bootstrap.definition = FactoryPerformanceScenarioLayout.Create(scenario);
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
            ComponentType.ReadWrite<Belt>());
        EntityQuery splitterQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Splitter>());
        EntityQuery storageQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<StorageState>());

        while ((beltQuery.CalculateEntityCount() != definition.BeltCount ||
                splitterQuery.CalculateEntityCount() !=
                    definition.SplitterCount ||
                storageQuery.CalculateEntityCount() !=
                    definition.StorageCount) &&
               Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        bool buildCountsMatch =
            beltQuery.CalculateEntityCount() == definition.BeltCount &&
            splitterQuery.CalculateEntityCount() == definition.SplitterCount &&
            storageQuery.CalculateEntityCount() == definition.StorageCount;
        if (!buildCountsMatch)
        {
            DisposeQueries(
                gridQuery,
                catalogQuery,
                beltQuery,
                splitterQuery,
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
                splitterQuery,
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
                out string itemFailure))
        {
            DisposeQueries(
                gridQuery,
                catalogQuery,
                beltQuery,
                splitterQuery,
                storageQuery);
            Fail(itemFailure);
            yield break;
        }

        ResetSimulationStats(entityManager);
        DisposeQueries(
            gridQuery,
            catalogQuery,
            beltQuery,
            splitterQuery,
            storageQuery);

        ready = true;
        status = "READY - start Profiler capture now";
        Debug.Log(
            "[ECS Performance] READY: " + definition.DisplayName +
            ". Grid=" + definition.GridSize.x + "x" +
            definition.GridSize.y +
            ", Belts=" + definition.BeltCount +
            ", Splitters=" + definition.SplitterCount +
            ", Items=" + definition.InitialItemCells.Length + ".");

        FactoryPerformanceMetricsCapture.StartIfRequested(definition);
    }

    private bool TryPopulateInitialItems(
        EntityManager entityManager,
        Entity catalog,
        EntityQuery beltQuery,
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

        if (itemPrefab == Entity.Null ||
            !entityManager.Exists(itemPrefab) ||
            !entityManager.HasComponent<Prefab>(itemPrefab) ||
            !entityManager.HasComponent<LocalTransform>(itemPrefab))
        {
            failure = "Iron ore Item Prefab is unavailable.";
            return false;
        }

        using NativeArray<Entity> beltEntities =
            beltQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<Belt> belts =
            beltQuery.ToComponentDataArray<Belt>(Allocator.Temp);
        Dictionary<int2, int> beltIndexByCell =
            new Dictionary<int2, int>(belts.Length);
        for (int i = 0; i < belts.Length; i++)
        {
            beltIndexByCell.Add(belts[i].Cell, i);
        }

        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);
        LocalTransform prefabTransform =
            entityManager.GetComponentData<LocalTransform>(itemPrefab);
        for (int i = 0; i < definition.InitialItemCells.Length; i++)
        {
            int2 cell = definition.InitialItemCells[i];
            if (!beltIndexByCell.TryGetValue(cell, out int beltIndex))
            {
                ecb.Dispose();
                failure = "No Belt entity exists for initial item cell " +
                          cell + ".";
                return false;
            }

            Entity item = ecb.Instantiate(itemPrefab);
            float3 position = new float3(
                cell.x + 0.5f,
                0.535f,
                cell.y + 0.5f);
            ecb.AddComponent(item, new Item
            {
                ItemType = itemType,
                Position = position
            });
            LocalTransform itemTransform = prefabTransform;
            itemTransform.Position = position;
            ecb.SetComponent(item, itemTransform);

            Belt belt = belts[beltIndex];
            belt.CurrentItem = item;
            belt.Progress = 0f;
            ecb.SetComponent(beltEntities[beltIndex], belt);
        }

        ecb.Playback(entityManager);
        ecb.Dispose();
        failure = null;
        return true;
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
        EcsGridInteractionController[] interactions =
            FindObjectsByType<EcsGridInteractionController>(
                FindObjectsSortMode.None);
        for (int i = 0; i < interactions.Length; i++)
        {
            interactions[i].enabled = false;
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
