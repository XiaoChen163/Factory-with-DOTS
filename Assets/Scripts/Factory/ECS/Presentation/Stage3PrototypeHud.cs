using Unity.Entities;
using UnityEngine;

public sealed class Stage3PrototypeHud : MonoBehaviour
{
    private EntityManager entityManager;
    private World entityWorld;

    private EntityQuery statsQuery;
    private bool queryCreated;

    private void Start()
    {
        TryCreateQuery();
    }

    private void OnDestroy()
    {
        if (queryCreated &&
            entityWorld != null &&
            entityWorld.IsCreated &&
            entityManager.IsQueryValid(statsQuery))
        {
            statsQuery.Dispose();
        }

        queryCreated = false;
    }

    private void OnGUI()
    {
        if (!queryCreated)
        {
            TryCreateQuery();
        }

        GUI.Box(
            new Rect(12f, 12f, 520f, 150f),
            "Factory With DOTS — Stage 3 ECS Runtime");
        GUI.Label(
            new Rect(28f, 42f, 470f, 22f),
            "Entities 1.4 + Baking/SubScene + Burst IJobEntity");

        if (!queryCreated || statsQuery.IsEmptyIgnoreFilter)
        {
            GUI.Label(
                new Rect(28f, 68f, 470f, 22f),
                "Waiting for the SubScene to finish loading and baking...");
            return;
        }

        Stage3SimulationStats stats =
            statsQuery.GetSingleton<Stage3SimulationStats>();
        GUI.Label(
            new Rect(28f, 68f, 470f, 22f),
            "Belts: " + stats.BeltCount +
            "   Loops: " + stats.LoopCount +
            "   Fixed Tick: " + stats.TickCount);
        GUI.Label(
            new Rect(28f, 94f, 470f, 22f),
            "Ready requests: " + stats.ReadyRequestCount +
            "   Accepted this tick: " +
            stats.AcceptedTransferCount);
        GUI.Label(
            new Rect(28f, 120f, 470f, 22f),
            "Full loops rotate atomically; blocked sources remain at progress 1.");
    }

    private void TryCreateQuery()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            return;
        }

        entityManager = world.EntityManager;
        entityWorld = world;

        statsQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Stage3SimulationStats>());
        queryCreated = true;
    }
}
