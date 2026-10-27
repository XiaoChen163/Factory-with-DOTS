using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class VerticalBeltColliderCacheSystem : SystemBase
{
    private readonly Dictionary<(float, float, bool), BlobAssetReference<Collider>> cache =
        new Dictionary<(float, float, bool), BlobAssetReference<Collider>>();

    public BlobAssetReference<Collider> GetOrCreate(WorldGridConfig grid, bool segment)
    {
        var key = (grid.CellSize, grid.LayerHeight, segment);
        if (cache.TryGetValue(key, out var collider)) return collider;
        float width = grid.CellSize * (segment ? .22f : .75f);
        collider = BoxCollider.Create(new BoxGeometry
        {
            Center = new float3(0, grid.LayerHeight * .5f, 0),
            Size = new float3(width, grid.LayerHeight * (segment ? 1f : .75f), width),
            Orientation = quaternion.identity,
            BevelRadius = 0f
        }, FoundationCollisionCategories.FoundationFilter);
        cache.Add(key, collider);
        return collider;
    }
    protected override void OnUpdate() { }
    protected override void OnDestroy()
    {
        Dependency.Complete();
        foreach (var collider in cache.Values)
            if (collider.IsCreated) collider.Dispose();
        cache.Clear();
    }
}
