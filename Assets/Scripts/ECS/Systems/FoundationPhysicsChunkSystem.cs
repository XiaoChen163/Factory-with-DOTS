using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;

[UpdateInGroup(typeof(BeforePhysicsSystemGroup))]
public partial class FoundationPhysicsChunkSystem : SystemBase
{
    private readonly Dictionary<SurfaceChunkKey, Entity> physicsChunks = new();
    private EntityQuery gridQuery;

    public ulong CreatedColliderBlobCount { get; private set; }
    public ulong DisposedColliderBlobCount { get; private set; }
    public int ActiveColliderBlobCount =>
        (int)(CreatedColliderBlobCount - DisposedColliderBlobCount);

    protected override void OnCreate()
    {
        gridQuery = GetEntityQuery(
            ComponentType.ReadOnly<WorldGridConfig>(),
            ComponentType.ReadOnly<FoundationCollisionSettings>(),
            ComponentType.ReadWrite<SurfacePhysicsDirtyChunk>());
    }

    protected override void OnDestroy()
    {
        Dependency.Complete();
        EntityManager.CompleteAllTrackedJobs();
        foreach (Entity entity in physicsChunks.Values)
        {
            if (!EntityManager.Exists(entity) ||
                !EntityManager.HasComponent<FoundationColliderOwner>(entity)) continue;
            FoundationColliderOwner owner =
                EntityManager.GetComponentData<FoundationColliderOwner>(entity);
            if (owner.Value.IsCreated)
            {
                owner.Value.Dispose();
                DisposedColliderBlobCount++;
            }
        }
        physicsChunks.Clear();
    }

    protected override void OnUpdate()
    {
        if (gridQuery.CalculateEntityCount() != 1) return;
        Entity grid = gridQuery.GetSingletonEntity();
        DynamicBuffer<SurfacePhysicsDirtyChunk> dirty =
            EntityManager.GetBuffer<SurfacePhysicsDirtyChunk>(grid);
        if (dirty.IsEmpty) return;
        using Unity.Collections.NativeArray<SurfacePhysicsDirtyChunk> dirtySnapshot =
            dirty.ToNativeArray(Unity.Collections.Allocator.Temp);
        dirty.Clear();
        Dependency.Complete();
        EntityManager.CompleteAllTrackedJobs();
        RebuildChunkMap();
        WorldGridConfig config = EntityManager.GetComponentData<WorldGridConfig>(grid);
        FoundationCollisionSettings settings =
            EntityManager.GetComponentData<FoundationCollisionSettings>(grid);
        for (int i = 0; i < dirtySnapshot.Length; i++)
            Rebuild(dirtySnapshot[i].Value, config, settings);
    }

    private void Rebuild(
        SurfaceChunkKey key,
        in WorldGridConfig config,
        in FoundationCollisionSettings settings)
    {
        Entity surfaceChunk = FindSurfaceChunk(key);
        if (surfaceChunk == Entity.Null)
        {
            DestroyPhysicsChunk(key);
            return;
        }

        DynamicBuffer<SurfaceCellData> cells =
            EntityManager.GetBuffer<SurfaceCellData>(surfaceChunk, true);
        List<int3> voxels = new List<int3>(cells.Length);
        for (int i = 0; i < cells.Length; i++)
        {
            int index = cells[i].LocalCellIndex;
            voxels.Add(new int3(
                index % SurfaceChunkUtility.ChunkSize,
                -1,
                index / SurfaceChunkUtility.ChunkSize));
        }
        List<FoundationBox> boxes = FoundationGreedyBoxBuilder.Build(voxels);
        BlobAssetReference<Collider> collider = FoundationCompoundColliderBuilder.Build(
            boxes,
            settings.Filter,
            settings.Material,
            config.CellSize,
            config.LayerHeight);
        if (collider.IsCreated) CreatedColliderBlobCount++;

        if (!physicsChunks.TryGetValue(key, out Entity entity) ||
            !EntityManager.Exists(entity))
        {
            entity = EntityManager.CreateEntity();
            float3 origin = config.Origin + new float3(
                key.ChunkX * SurfaceChunkUtility.ChunkSize * config.CellSize,
                key.Level * config.LayerHeight,
                key.ChunkZ * SurfaceChunkUtility.ChunkSize * config.CellSize);
            EntityManager.AddComponentData(entity, new FoundationPhysicsChunk { Key = key });
            EntityManager.AddComponentData(entity, LocalTransform.FromPosition(origin));
            EntityManager.AddComponentData(entity, new LocalToWorld
            {
                Value = float4x4.Translate(origin)
            });
            EntityManager.AddSharedComponentManaged(entity, new PhysicsWorldIndex { Value = 0 });
            EntityManager.AddComponentData(entity, new PhysicsCollider { Value = default });
            EntityManager.AddComponentData(entity, new FoundationColliderOwner());
            physicsChunks[key] = entity;
        }

        FoundationColliderOwner previous =
            EntityManager.GetComponentData<FoundationColliderOwner>(entity);
        EntityManager.SetComponentData(entity, new PhysicsCollider { Value = collider });
        EntityManager.SetComponentData(entity, new FoundationColliderOwner { Value = collider });
        if (previous.Value.IsCreated)
        {
            previous.Value.Dispose();
            DisposedColliderBlobCount++;
        }
    }

    private Entity FindSurfaceChunk(SurfaceChunkKey key)
    {
        foreach ((RefRO<SurfaceChunk> chunk, Entity entity) in
                 SystemAPI.Query<RefRO<SurfaceChunk>>().WithEntityAccess())
            if (chunk.ValueRO.Key == key) return entity;
        return Entity.Null;
    }

    private void RebuildChunkMap()
    {
        physicsChunks.Clear();
        foreach ((RefRO<FoundationPhysicsChunk> chunk, Entity entity) in
                 SystemAPI.Query<RefRO<FoundationPhysicsChunk>>().WithEntityAccess())
            physicsChunks[chunk.ValueRO.Key] = entity;
    }

    private void DestroyPhysicsChunk(SurfaceChunkKey key)
    {
        if (!physicsChunks.TryGetValue(key, out Entity entity) ||
            !EntityManager.Exists(entity)) return;
        FoundationColliderOwner owner =
            EntityManager.GetComponentData<FoundationColliderOwner>(entity);
        if (owner.Value.IsCreated)
        {
            owner.Value.Dispose();
            DisposedColliderBlobCount++;
        }
        EntityManager.DestroyEntity(entity);
        physicsChunks.Remove(key);
    }
}

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class FoundationColliderCleanupSystem : SystemBase
{
    protected override void OnUpdate()
    {
        EntityManager.CompleteAllTrackedJobs();
        EntityQuery query = GetEntityQuery(
            new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadWrite<FoundationColliderOwner>() },
                None = new[] { ComponentType.ReadOnly<FoundationPhysicsChunk>() }
            });
        using Unity.Collections.NativeArray<Entity> entities =
            query.ToEntityArray(Unity.Collections.Allocator.Temp);
        for (int i = 0; i < entities.Length; i++)
        {
            FoundationColliderOwner owner =
                EntityManager.GetComponentData<FoundationColliderOwner>(entities[i]);
            if (owner.Value.IsCreated) owner.Value.Dispose();
            EntityManager.RemoveComponent<FoundationColliderOwner>(entities[i]);
        }
    }
}

public static class FoundationQueryUtility
{
    public const float DefaultInsideEpsilon = 0.001f;

    public static GridCell ResolveVoxelCell(
        float3 hitPosition,
        float3 surfaceNormal,
        in WorldGridConfig config,
        float epsilon = DefaultInsideEpsilon)
    {
        float3 inside = hitPosition - math.normalizesafe(surfaceNormal) * epsilon;
        float cellSize = math.max(math.EPSILON, config.CellSize);
        float layerHeight = math.max(math.EPSILON, config.LayerHeight);
        return new GridCell(
            (int)math.floor((inside.x - config.Origin.x) / cellSize),
            (int)math.ceil((inside.y - config.Origin.y) / layerHeight),
            (int)math.floor((inside.z - config.Origin.z) / cellSize));
    }

    public static bool TryResolveFoundation(
        float3 hitPosition,
        float3 surfaceNormal,
        in WorldGridConfig config,
        SurfaceRegistrySystem registry,
        out GridCell cell,
        out Entity foundation)
    {
        cell = ResolveVoxelCell(hitPosition, surfaceNormal, config);
        foundation = Entity.Null;
        return registry != null && registry.TryGetFoundation(cell, out foundation);
    }
}
