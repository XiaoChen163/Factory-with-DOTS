using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(BeltProgressSystem))]
[UpdateBefore(typeof(BeltItemPositionSystem))]
public partial class BeltTransferSystem : SystemBase
{
    private readonly Dictionary<int2, int> nextWinnerByTarget =
        new Dictionary<int2, int>();

    private EntityQuery beltQuery;
    private Entity statsEntity;

    protected override void OnCreate()
    {
        beltQuery = GetEntityQuery(
            ComponentType.ReadWrite<Belt>());
        statsEntity = EntityManager.CreateEntity(
            typeof(Stage3SimulationStats));
        RequireForUpdate(beltQuery);
    }

    protected override void OnUpdate()
    {
        Dependency.Complete();

        using NativeArray<Entity> entitySnapshot =
            beltQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<Belt> beltSnapshot =
            beltQuery.ToComponentDataArray<Belt>(Allocator.Temp);

        Entity[] entities = entitySnapshot.ToArray();
        Belt[] belts = beltSnapshot.ToArray();

        BeltTransferResolver.Resolve(
            entities,
            belts,
            nextWinnerByTarget,
            out int loopCount,
            out int readyRequestCount,
            out int acceptedTransferCount);

        for (int i = 0; i < entities.Length; i++)
        {
            EntityManager.SetComponentData(entities[i], belts[i]);
        }

        Stage3SimulationStats stats =
            EntityManager.GetComponentData<Stage3SimulationStats>(
                statsEntity);
        stats.BeltCount = belts.Length;
        stats.LoopCount = loopCount;
        stats.ReadyRequestCount = readyRequestCount;
        stats.AcceptedTransferCount = acceptedTransferCount;
        stats.TickCount++;
        EntityManager.SetComponentData(statsEntity, stats);
    }
}
