using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class RampPhysicsColliderCacheSystem : SystemBase
{
    private readonly Dictionary<int, BlobAssetReference<Collider>> colliders =
        new Dictionary<int, BlobAssetReference<Collider>>();

    public BlobAssetReference<Collider> GetOrCreate(float normalizedRise)
    {
        int key = math.asint(normalizedRise);
        if (colliders.TryGetValue(key, out BlobAssetReference<Collider> collider))
            return collider;

        float rise = math.max(math.EPSILON, normalizedRise);
        using NativeArray<float3> points = new NativeArray<float3>(
            new[]
            {
                new float3(0.5f, 0f, -0.5f),
                new float3(0.5f, 0f, 0.5f),
                new float3(-0.5f, 0f, -0.5f),
                new float3(-0.5f, 0f, 0.5f),
                new float3(-0.5f, rise, -0.5f),
                new float3(-0.5f, rise, 0.5f)
            },
            Allocator.Temp);
        collider = ConvexCollider.Create(
            points,
            ConvexHullGenerationParameters.Default,
            FoundationCollisionCategories.FoundationFilter,
            Unity.Physics.Material.Default);
        colliders.Add(key, collider);
        return collider;
    }

    protected override void OnUpdate() { }

    protected override void OnDestroy()
    {
        foreach (BlobAssetReference<Collider> collider in colliders.Values)
            if (collider.IsCreated)
                collider.Dispose();
        colliders.Clear();
    }
}
