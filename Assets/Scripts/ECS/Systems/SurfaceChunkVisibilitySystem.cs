using Unity.Collections;
using Unity.Entities;
using Unity.Rendering;

[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateAfter(typeof(SurfaceChunkMeshBuildSystem))]
public partial class SurfaceChunkVisibilitySystem : SystemBase
{
    private EntityQuery gridQuery;
    private EntityQuery renderChunkQuery;
    private uint lastViewRevision = uint.MaxValue;
    private int lastChunkOrderVersion = -1;

    protected override void OnCreate()
    {
        gridQuery = GetEntityQuery(
            ComponentType.ReadOnly<GridLayerViewState>());
        renderChunkQuery = GetEntityQuery(
            ComponentType.ReadOnly<FoundationRenderChunk>());
    }

    protected override void OnUpdate()
    {
        if (gridQuery.CalculateEntityCount() != 1)
            return;
        GridLayerViewState view =
            gridQuery.GetSingleton<GridLayerViewState>();
        int orderVersion =
            EntityManager.GetComponentOrderVersion<FoundationRenderChunk>();
        if (view.Revision == lastViewRevision &&
            orderVersion == lastChunkOrderVersion)
        {
            return;
        }

        using NativeArray<Entity> entities =
            renderChunkQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<FoundationRenderChunk> chunks =
            renderChunkQuery.ToComponentDataArray<FoundationRenderChunk>(
                Allocator.Temp);
        for (int i = 0; i < entities.Length; i++)
        {
            bool visible = EcsGridUtility.IsLevelVisible(
                chunks[i].Key.Level,
                view);
            bool hidden = EntityManager.HasComponent<DisableRendering>(
                entities[i]);
            if (visible && hidden)
                EntityManager.RemoveComponent<DisableRendering>(entities[i]);
            else if (!visible && !hidden)
                EntityManager.AddComponent<DisableRendering>(entities[i]);
        }

        lastViewRevision = view.Revision;
        lastChunkOrderVersion =
            EntityManager.GetComponentOrderVersion<FoundationRenderChunk>();
    }
}
