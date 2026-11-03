using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[UpdateBefore(typeof(GridBuildCommandSystem))]
public partial class VerticalConnectorIndexSystem : SystemBase
{
    private NativeParallelHashMap<GridCell, Entity> nodes;
    private EntityQuery query;
    private int lastOrder = -1;
    public int Count => nodes.Count();

    protected override void OnCreate()
    {
        nodes = new NativeParallelHashMap<GridCell, Entity>(16, Allocator.Persistent);
        query = GetEntityQuery(ComponentType.ReadOnly<VerticalBeltNode>(),
            ComponentType.ReadOnly<GridPlacement>());
    }

    protected override void OnDestroy()
    {
        Dependency.Complete();
        if (nodes.IsCreated) nodes.Dispose();
    }

    protected override void OnUpdate() => Refresh();

    private void Refresh()
    {
        int order = EntityManager.GetComponentOrderVersion<VerticalBeltNode>();
        if (lastOrder == order) return;
        Dependency.Complete();
        nodes.Clear();
        using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
        using NativeArray<GridPlacement> placements = query.ToComponentDataArray<GridPlacement>(Allocator.Temp);
        if (nodes.Capacity < entities.Length) nodes.Capacity = entities.Length;
        for (int i = 0; i < entities.Length; i++)
            nodes.TryAdd(placements[i].AnchorCell, entities[i]);
        lastOrder = order;
    }

    public bool TryGet(GridCell cell, out Entity entity)
    {
        Refresh();
        return nodes.TryGetValue(cell, out entity);
    }
}
