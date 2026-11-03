using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(GridBuildCommandSystem))]
[UpdateAfter(typeof(RampTopologyConnectionSystem))]
[UpdateBefore(typeof(FixedStepSimulationSystemGroup))]
public partial class VerticalBeltTopologySystem : SystemBase
{
    private uint lastRevision;
    private bool initialized;
    protected override void OnCreate() => RequireForUpdate<TransportTopologyRevision>();

    protected override void OnUpdate()
    {
        uint revision = SystemAPI.GetSingleton<TransportTopologyRevision>().Value;
        if (initialized && revision == lastRevision) return;
        Entity grid = SystemAPI.GetSingletonEntity<TransportTopologyRevision>();
        if (!EntityManager.HasBuffer<TransportExplicitEdge>(grid))
            EntityManager.AddBuffer<TransportExplicitEdge>(grid);
        DynamicBuffer<TransportExplicitEdge> edges = EntityManager.GetBuffer<TransportExplicitEdge>(grid);
        for (int i = edges.Length - 1; i >= 0; i--)
            if (edges[i].Generator == 2) edges.RemoveAt(i);
        VerticalConnectorIndexSystem index = World.GetOrCreateSystemManaged<VerticalConnectorIndexSystem>();
        foreach (var (nodeValue, placementValue, entity) in
            SystemAPI.Query<RefRO<VerticalBeltNode>, RefRO<GridPlacement>>().WithEntityAccess())
        {
            VerticalBeltNode node = nodeValue.ValueRO;
            GridCell cell = placementValue.ValueRO.AnchorCell;
            if (node.Kind == BuildingKind.VerticalBeltExit) continue;
            for (int step = -1; step <= 1; step += 2)
            {
                if (node.Kind == BuildingKind.VerticalBeltSegment && step != node.Step) continue;
                GridCell next = cell;
                next.Level += step;
                if (!index.TryGet(next, out Entity target)) continue;
                VerticalBeltNode targetNode = EntityManager.GetComponentData<VerticalBeltNode>(target);
                bool valid = targetNode.Kind == BuildingKind.VerticalBeltSegment && targetNode.Step == step ||
                    node.Kind == BuildingKind.VerticalBeltSegment && targetNode.Kind == BuildingKind.VerticalBeltExit;
                if (!valid) continue;
                edges.Add(new TransportExplicitEdge { Source = entity, Target = target, Generator = 2 });
                break;
            }
        }
        lastRevision = revision;
        initialized = true;
    }
}
