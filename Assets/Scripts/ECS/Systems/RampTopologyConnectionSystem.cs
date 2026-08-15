using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(GridBuildCommandSystem))]
[UpdateBefore(typeof(FixedStepSimulationSystemGroup))]
public partial class RampTopologyConnectionSystem : SystemBase
{
    private const byte RampEdgeGenerator = 1;
    private uint cachedRevision;
    private bool hasCachedRevision;

    protected override void OnCreate()
    {
        RequireForUpdate<TransportTopologyRevision>();
    }

    protected override void OnUpdate()
    {
        TransportTopologyRevision revision =
            SystemAPI.GetSingleton<TransportTopologyRevision>();
        if (hasCachedRevision && cachedRevision == revision.Value)
            return;

        Entity grid = SystemAPI.GetSingletonEntity<TransportTopologyRevision>();
        if (!EntityManager.HasBuffer<TransportExplicitEdge>(grid))
            EntityManager.AddBuffer<TransportExplicitEdge>(grid);
        DynamicBuffer<TransportExplicitEdge> edges =
            EntityManager.GetBuffer<TransportExplicitEdge>(grid);
        for (int i = edges.Length - 1; i >= 0; i--)
            if (edges[i].Generator == RampEdgeGenerator)
                edges.RemoveAt(i);

        EntityQuery beltQuery = GetEntityQuery(
            ComponentType.ReadOnly<BeltTopology>());
        using NativeArray<Entity> beltEntities =
            beltQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<BeltTopology> beltTopologies =
            beltQuery.ToComponentDataArray<BeltTopology>(Allocator.Temp);
        Dictionary<GridCell, Entity> planarBelts =
            new Dictionary<GridCell, Entity>();
        for (int i = 0; i < beltEntities.Length; i++)
            if (!EntityManager.HasComponent<RampBelt>(beltEntities[i]) &&
                beltTopologies[i].ConnectionMode == TransportConnectionMode.Planar)
                planarBelts[beltTopologies[i].Cell] = beltEntities[i];

        Dictionary<RampEndpointKey, Entity> rampEntries =
            new Dictionary<RampEndpointKey, Entity>();
        List<(Entity Entity, RampBelt Belt, RampConnector Connector,
            BeltTopology Topology)> ramps =
            new List<(Entity, RampBelt, RampConnector, BeltTopology)>();
        foreach ((RefRO<RampBelt> beltValue, Entity entity) in
                 SystemAPI.Query<RefRO<RampBelt>>().WithEntityAccess())
        {
            RampBelt belt = beltValue.ValueRO;
            if (!EntityManager.Exists(belt.Connector) ||
                !EntityManager.HasComponent<RampConnector>(belt.Connector))
                continue;
            RampConnector connector =
                EntityManager.GetComponentData<RampConnector>(belt.Connector);
            if (!EntityManager.HasComponent<BeltTopology>(entity))
                continue;
            ramps.Add((entity, belt, connector,
                EntityManager.GetComponentData<BeltTopology>(entity)));
            rampEntries[RampUtility.GetEntryEndpoint(
                connector, belt.TravelDirection)] = entity;
        }

        for (int i = 0; i < ramps.Count; i++)
        {
            (Entity rampEntity, RampBelt belt, RampConnector connector,
                BeltTopology topology) = ramps[i];
            int2 direction = belt.TravelDirection;
            bool uphill = math.all(direction == connector.UphillDirection);
            GridHeight entryHeight = uphill
                ? connector.LowHeight
                : connector.HighHeight;
            GridHeight exitHeight = uphill
                ? connector.HighHeight
                : connector.LowHeight;
            GridCell inputCell = connector.Cell - direction;
            GridCell outputCell = connector.Cell + direction;

            if (!RampUtility.AllowsPlanarInput(topology.ConnectionMode) &&
                entryHeight.IsWholeLevel)
            {
                inputCell.Level = entryHeight.WholeLevel;
                if (planarBelts.TryGetValue(inputCell, out Entity source) &&
                    EntityManager.GetComponentData<BeltTopology>(source).Direction.Equals(direction))
                    AddEdge(edges, source, rampEntity);
            }

            RampEndpointKey exit = RampUtility.GetExitEndpoint(connector, direction);
            if (rampEntries.TryGetValue(exit, out Entity nextRamp) &&
                nextRamp != rampEntity)
            {
                RampBelt next = EntityManager.GetComponentData<RampBelt>(nextRamp);
                BeltTopology nextTopology =
                    EntityManager.GetComponentData<BeltTopology>(nextRamp);
                if (math.all(next.TravelDirection == direction) &&
                    (!RampUtility.AllowsPlanarOutput(topology.ConnectionMode) ||
                     !RampUtility.AllowsPlanarInput(nextTopology.ConnectionMode)))
                    AddEdge(edges, rampEntity, nextRamp);
            }
            else if (!RampUtility.AllowsPlanarOutput(topology.ConnectionMode) &&
                     exitHeight.IsWholeLevel)
            {
                outputCell.Level = exitHeight.WholeLevel;
                if (planarBelts.TryGetValue(outputCell, out Entity target))
                    AddEdge(edges, rampEntity, target);
            }
        }

        cachedRevision = revision.Value;
        hasCachedRevision = true;
    }

    private static void AddEdge(
        DynamicBuffer<TransportExplicitEdge> edges,
        Entity source,
        Entity target)
    {
        edges.Add(new TransportExplicitEdge
        {
            Source = source,
            Target = target,
            SourceOutputIndex = 0,
            TargetInputIndex = 0,
            Generator = RampEdgeGenerator
        });
    }
}
