using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(BeltProgressSystem))]
[UpdateBefore(typeof(BeltItemPositionSystem))]
public partial class BeltTransferSystem : SystemBase
{
    private readonly HashSet<Entity> forwardedJunctions =
        new HashSet<Entity>();

    private EntityQuery beltQuery;
    private EntityQuery mergerQuery;
    private EntityQuery splitterQuery;
    private Entity statsEntity;

    protected override void OnCreate()
    {
        beltQuery = GetEntityQuery(
            ComponentType.ReadWrite<Belt>());
        mergerQuery = GetEntityQuery(
            ComponentType.ReadWrite<Merger>());
        splitterQuery = GetEntityQuery(
            ComponentType.ReadWrite<Splitter>());
        statsEntity = EntityManager.CreateEntity(
            typeof(Stage3SimulationStats));
        RequireForUpdate<Stage3SimulationStats>();
    }

    protected override void OnUpdate()
    {
        Dependency.Complete();
        float deltaTime = SystemAPI.Time.DeltaTime;

        using NativeArray<Entity> entitySnapshot =
            beltQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<Belt> beltSnapshot =
            beltQuery.ToComponentDataArray<Belt>(Allocator.Temp);
        using NativeArray<Entity> mergerEntitySnapshot =
            mergerQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<Merger> mergerSnapshot =
            mergerQuery.ToComponentDataArray<Merger>(Allocator.Temp);
        using NativeArray<Entity> splitterEntitySnapshot =
            splitterQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<Splitter> splitterSnapshot =
            splitterQuery.ToComponentDataArray<Splitter>(Allocator.Temp);

        Entity[] entities = entitySnapshot.ToArray();
        Belt[] belts = beltSnapshot.ToArray();
        Entity[] mergerEntities = mergerEntitySnapshot.ToArray();
        Merger[] mergers = mergerSnapshot.ToArray();
        Entity[] splitterEntities = splitterEntitySnapshot.ToArray();
        Splitter[] splitters = splitterSnapshot.ToArray();

        AdvanceJunctionTimers(
            mergers,
            splitters,
            deltaTime);

        forwardedJunctions.Clear();
        int loopCount = 0;
        int readyRequestCount = 0;
        int acceptedTransferCount = 0;
        int passLimit = mergers.Length + splitters.Length + 1;
        for (int pass = 0; pass < passLimit; pass++)
        {
            FactoryTransferResolver.Resolve(
                entities,
                belts,
                mergerEntities,
                mergers,
                splitterEntities,
                splitters,
                forwardedJunctions,
                out int passLoopCount,
                out int passReadyRequestCount,
                out int passAcceptedTransferCount);

            loopCount = passLoopCount;
            readyRequestCount += passReadyRequestCount;
            acceptedTransferCount += passAcceptedTransferCount;
            if (passAcceptedTransferCount == 0)
            {
                break;
            }
        }

        for (int i = 0; i < entities.Length; i++)
        {
            EntityManager.SetComponentData(entities[i], belts[i]);
        }
        for (int i = 0; i < mergerEntities.Length; i++)
        {
            EntityManager.SetComponentData(
                mergerEntities[i],
                mergers[i]);
        }
        for (int i = 0; i < splitterEntities.Length; i++)
        {
            EntityManager.SetComponentData(
                splitterEntities[i],
                splitters[i]);
        }

        Stage3SimulationStats stats =
            EntityManager.GetComponentData<Stage3SimulationStats>(
                statsEntity);
        stats.BeltCount = belts.Length;
        stats.MergerCount = mergers.Length;
        stats.SplitterCount = splitters.Length;
        stats.LoopCount = loopCount;
        stats.ReadyRequestCount = readyRequestCount;
        stats.AcceptedTransferCount = acceptedTransferCount;
        stats.TickCount++;
        EntityManager.SetComponentData(statsEntity, stats);
    }

    private static void AdvanceJunctionTimers(
        Merger[] mergers,
        Splitter[] splitters,
        float deltaTime)
    {
        for (int i = 0; i < mergers.Length; i++)
        {
            Merger merger = mergers[i];
            if (merger.CurrentItem == Entity.Null)
            {
                merger.TransferElapsed = 0f;
                merger.InputInterval = 0f;
            }
            else
            {
                merger.TransferElapsed += deltaTime;
            }

            mergers[i] = merger;
        }

        for (int i = 0; i < splitters.Length; i++)
        {
            Splitter splitter = splitters[i];
            if (splitter.CurrentItem == Entity.Null)
            {
                splitter.TransferElapsed = 0f;
                splitter.InputInterval = 0f;
            }
            else
            {
                splitter.TransferElapsed += deltaTime;
            }

            splitters[i] = splitter;
        }
    }
}
