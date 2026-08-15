using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class SurfaceRegistrySystem : SystemBase
{
    public struct SurfaceRecord
    {
        public Entity Foundation;
        public Entity Chunk;
        public ushort VisualMaterialId;
        public SurfacePermission Permissions;
        public byte OccludesFaces;
        public SurfaceShapeFlags ShapeFlags;
    }

    private NativeParallelHashMap<GridCell, SurfaceRecord> surfaces;
    private NativeParallelHashMap<SurfaceChunkKey, Entity> chunks;
    private EntityQuery gridQuery;
    private uint indexedRevision;

    public bool IsReady { get; private set; }
    public int SurfaceCount => surfaces.IsCreated ? surfaces.Count() : 0;
    public int ChunkCount => chunks.IsCreated ? chunks.Count() : 0;

    protected override void OnCreate()
    {
        surfaces = new NativeParallelHashMap<GridCell, SurfaceRecord>(256, Allocator.Persistent);
        chunks = new NativeParallelHashMap<SurfaceChunkKey, Entity>(16, Allocator.Persistent);
        gridQuery = GetEntityQuery(ComponentType.ReadOnly<GridDefinition>());
        indexedRevision = uint.MaxValue;
    }

    protected override void OnDestroy()
    {
        if (surfaces.IsCreated) surfaces.Dispose();
        if (chunks.IsCreated) chunks.Dispose();
    }

    protected override void OnUpdate()
    {
        if (gridQuery.CalculateEntityCount() != 1)
        {
            IsReady = false;
            return;
        }

        Dependency.Complete();
        Entity grid = gridQuery.GetSingletonEntity();
        EnsureGridState(grid);
        if (EntityManager.HasComponent<InitialSurfaceSettings>(grid) &&
            EntityManager.GetComponentData<InitialSurfaceSettings>(grid).Mode ==
                InitialSurfaceMode.LegacyRectangle &&
            !EntityManager.HasComponent<LegacySurfaceInitialized>(grid))
            InitializeLegacyRectangle(grid);

        uint revision = EntityManager.GetComponentData<SurfaceTopologyRevision>(grid).Value;
        if (!IsReady || indexedRevision != revision)
            RebuildIndex();
        indexedRevision = revision;
        IsReady = true;
    }

    public bool HasSurface(GridCell cell) => surfaces.ContainsKey(cell);
    public bool HasFoundationVoxel(GridCell cell) => surfaces.ContainsKey(cell);
    public bool IsOpaqueFoundationVoxel(GridCell cell) =>
        surfaces.TryGetValue(cell, out SurfaceRecord value) && value.OccludesFaces != 0;
    public bool AllowsBuildings(GridCell cell) =>
        surfaces.TryGetValue(cell, out SurfaceRecord value) &&
        (value.Permissions & SurfacePermission.Buildings) != 0;
    public bool AllowsBelts(GridCell cell) =>
        surfaces.TryGetValue(cell, out SurfaceRecord value) &&
        (value.Permissions & SurfacePermission.Belts) != 0;
    public bool HasFlatSurface(GridCell cell) =>
        surfaces.TryGetValue(cell, out SurfaceRecord value) &&
        (value.ShapeFlags & SurfaceShapeFlags.Flat) != 0;
    public bool AllowsFlat(
        GridCell cell,
        SurfacePermission requiredPermission) =>
        surfaces.TryGetValue(cell, out SurfaceRecord value) &&
        (value.ShapeFlags & SurfaceShapeFlags.Flat) != 0 &&
        (value.Permissions & requiredPermission) == requiredPermission;
    public bool TryGetFoundation(GridCell cell, out Entity foundation)
    {
        if (surfaces.TryGetValue(cell, out SurfaceRecord value))
        {
            foundation = value.Foundation;
            return true;
        }
        foundation = Entity.Null;
        return false;
    }
    public bool TryGetSurface(GridCell cell, out SurfaceRecord value) =>
        surfaces.TryGetValue(cell, out value);
    public bool TryGetChunk(SurfaceChunkKey key, out Entity entity) =>
        chunks.TryGetValue(key, out entity);

    public bool HasSurfaceForFootprint(
        GridCell anchor,
        int width,
        int depth,
        SurfacePermission requiredPermission)
    {
        for (int z = 0; z < depth; z++)
        for (int x = 0; x < width; x++)
        {
            GridCell cell = new GridCell(anchor.X + x, anchor.Level, anchor.Z + z);
            if (!surfaces.TryGetValue(cell, out SurfaceRecord value) ||
                (value.Permissions & requiredPermission) != requiredPermission)
                return false;
        }
        return true;
    }

    public Entity AddFoundation(
        Entity grid,
        GridCell cell,
        ushort visualMaterialId,
        SurfacePermission permissions,
        bool occludesFaces)
    {
        if (surfaces.ContainsKey(cell))
            return Entity.Null;
        EnsureSurfaceCapacity(surfaces.Count() + 1);

        Entity foundation = EntityManager.CreateEntity();
        EntityManager.AddComponentData(foundation, new Foundation
        {
            Cell = cell,
            VisualMaterialId = visualMaterialId,
            Permissions = permissions,
            OccludesFaces = occludesFaces ? (byte)1 : (byte)0,
            ShapeFlags = SurfaceShapeFlags.Flat
        });
        RegisterFoundation(grid, foundation, EntityManager.GetComponentData<Foundation>(foundation));
        return foundation;
    }

    public int AddFoundationArea(
        Entity grid,
        IReadOnlyList<GridCell> cells,
        ushort visualMaterialId,
        SurfacePermission permissions,
        bool occludesFaces)
    {
        if (cells == null || cells.Count == 0)
            return 0;
        HashSet<GridCell> uniqueCells = new HashSet<GridCell>();
        for (int i = 0; i < cells.Count; i++)
        {
            if (surfaces.ContainsKey(cells[i]) || !uniqueCells.Add(cells[i]))
                return 0;
        }
        EnsureSurfaceCapacity(surfaces.Count() + cells.Count);
        HashSet<SurfaceChunkKey> changedChunks = new HashSet<SurfaceChunkKey>();
        byte occlusion = occludesFaces ? (byte)1 : (byte)0;
        for (int i = 0; i < cells.Count; i++)
        {
            GridCell cell = cells[i];
            Entity foundation = EntityManager.CreateEntity();
            Foundation data = new Foundation
            {
                Cell = cell,
                VisualMaterialId = visualMaterialId,
                Permissions = permissions,
                OccludesFaces = occlusion,
                ShapeFlags = SurfaceShapeFlags.Flat
            };
            EntityManager.AddComponentData(foundation, data);
            SurfaceChunkKey key = SurfaceChunkUtility.GetChunkKey(cell);
            Entity chunk = GetOrCreateChunk(key);
            int localIndex = SurfaceChunkUtility.GetLocalCellIndex(cell);
            SurfaceChunkOccupancy occupancy =
                EntityManager.GetComponentData<SurfaceChunkOccupancy>(chunk);
            occupancy.Set(localIndex, true);
            EntityManager.SetComponentData(chunk, occupancy);
            EntityManager.GetBuffer<SurfaceCellData>(chunk).Add(new SurfaceCellData
            {
                LocalCellIndex = (ushort)localIndex,
                VisualMaterialId = visualMaterialId,
                Permissions = permissions,
                OccludesFaces = occlusion,
                ShapeFlags = SurfaceShapeFlags.Flat,
                Foundation = foundation
            });
            surfaces[cell] = new SurfaceRecord
            {
                Foundation = foundation,
                Chunk = chunk,
                VisualMaterialId = visualMaterialId,
                Permissions = permissions,
                OccludesFaces = occlusion,
                ShapeFlags = SurfaceShapeFlags.Flat
            };
            changedChunks.Add(key);
            MarkDirty(grid, cell);
        }
        foreach (SurfaceChunkKey key in changedChunks)
            if (chunks.TryGetValue(key, out Entity chunk))
                IncrementChunkRevision(chunk);
        IncrementTopologyRevision(grid);
        indexedRevision = EntityManager.GetComponentData<SurfaceTopologyRevision>(grid).Value;
        return cells.Count;
    }

    public bool RemoveFoundation(Entity grid, GridCell cell, out Entity foundation)
    {
        if (!surfaces.TryGetValue(cell, out SurfaceRecord record))
        {
            foundation = Entity.Null;
            return false;
        }

        foundation = record.Foundation;
        Entity chunk = record.Chunk;
        SurfaceChunkOccupancy occupancy = EntityManager.GetComponentData<SurfaceChunkOccupancy>(chunk);
        int localIndex = SurfaceChunkUtility.GetLocalCellIndex(cell);
        occupancy.Set(localIndex, false);
        EntityManager.SetComponentData(chunk, occupancy);
        DynamicBuffer<SurfaceCellData> cells = EntityManager.GetBuffer<SurfaceCellData>(chunk);
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i].LocalCellIndex != localIndex) continue;
            cells.RemoveAt(i);
            break;
        }
        SurfaceChunkKey key = SurfaceChunkUtility.GetChunkKey(cell);
        if (occupancy.IsEmpty)
        {
            chunks.Remove(key);
            EntityManager.DestroyEntity(chunk);
        }
        else
        {
            IncrementChunkRevision(chunk);
        }
        surfaces.Remove(cell);
        MarkDirty(grid, cell);
        IncrementTopologyRevision(grid);
        indexedRevision = EntityManager.GetComponentData<SurfaceTopologyRevision>(grid).Value;
        return true;
    }

    private void RegisterFoundation(Entity grid, Entity foundation, Foundation data)
    {
        SurfaceChunkKey key = SurfaceChunkUtility.GetChunkKey(data.Cell);
        Entity chunk = GetOrCreateChunk(key);
        int localIndex = SurfaceChunkUtility.GetLocalCellIndex(data.Cell);
        SurfaceChunkOccupancy occupancy = EntityManager.GetComponentData<SurfaceChunkOccupancy>(chunk);
        occupancy.Set(localIndex, true);
        EntityManager.SetComponentData(chunk, occupancy);
        EntityManager.GetBuffer<SurfaceCellData>(chunk).Add(new SurfaceCellData
        {
            LocalCellIndex = (ushort)localIndex,
            VisualMaterialId = data.VisualMaterialId,
            Permissions = data.Permissions,
            OccludesFaces = data.OccludesFaces,
            ShapeFlags = data.ShapeFlags == SurfaceShapeFlags.None
                ? SurfaceShapeFlags.Flat
                : data.ShapeFlags,
            Foundation = foundation
        });
        surfaces[data.Cell] = new SurfaceRecord
        {
            Foundation = foundation,
            Chunk = chunk,
            VisualMaterialId = data.VisualMaterialId,
            Permissions = data.Permissions,
            OccludesFaces = data.OccludesFaces,
            ShapeFlags = data.ShapeFlags == SurfaceShapeFlags.None
                ? SurfaceShapeFlags.Flat
                : data.ShapeFlags
        };
        IncrementChunkRevision(chunk);
        MarkDirty(grid, data.Cell);
        IncrementTopologyRevision(grid);
        indexedRevision = EntityManager.GetComponentData<SurfaceTopologyRevision>(grid).Value;
    }

    private Entity GetOrCreateChunk(SurfaceChunkKey key)
    {
        if (chunks.TryGetValue(key, out Entity existing) && EntityManager.Exists(existing))
            return existing;
        if (chunks.Count() >= chunks.Capacity)
            chunks.Capacity = math.max(16, chunks.Capacity * 2);
        Entity chunk = EntityManager.CreateEntity();
        EntityManager.AddComponentData(chunk, new SurfaceChunk { Key = key, Revision = 0 });
        EntityManager.AddComponentData(chunk, new SurfaceChunkOccupancy());
        EntityManager.AddBuffer<SurfaceCellData>(chunk);
        chunks[key] = chunk;
        return chunk;
    }

    private void InitializeLegacyRectangle(Entity grid)
    {
        GridDefinition definition = EntityManager.GetComponentData<GridDefinition>(grid);
        EnsureSurfaceCapacity(definition.Size.x * definition.Size.y);
        for (int z = 0; z < definition.Size.y; z++)
        for (int x = 0; x < definition.Size.x; x++)
            AddFoundation(grid, new GridCell(x, 0, z), 0, SurfacePermission.All, true);
        EntityManager.AddComponent<LegacySurfaceInitialized>(grid);
    }

    private void EnsureGridState(Entity grid)
    {
        if (!EntityManager.HasComponent<SurfaceTopologyRevision>(grid))
            EntityManager.AddComponentData(grid, new SurfaceTopologyRevision { Value = 1 });
        if (!EntityManager.HasBuffer<SurfaceRenderDirtyChunk>(grid))
            EntityManager.AddBuffer<SurfaceRenderDirtyChunk>(grid);
        if (!EntityManager.HasBuffer<SurfacePhysicsDirtyChunk>(grid))
            EntityManager.AddBuffer<SurfacePhysicsDirtyChunk>(grid);
        if (!EntityManager.HasComponent<FoundationCollisionSettings>(grid))
            EntityManager.AddComponentData(grid, new FoundationCollisionSettings
            {
                Filter = FoundationCollisionCategories.FoundationFilter,
                Material = Unity.Physics.Material.Default
            });
    }

    private void RebuildIndex()
    {
        surfaces.Clear();
        chunks.Clear();
        EntityQuery foundationQuery = GetEntityQuery(ComponentType.ReadOnly<Foundation>());
        EntityQuery chunkQuery = GetEntityQuery(ComponentType.ReadOnly<SurfaceChunk>());
        EnsureSurfaceCapacity(foundationQuery.CalculateEntityCount());
        int requiredChunks = chunkQuery.CalculateEntityCount();
        if (chunks.Capacity < requiredChunks) chunks.Capacity = math.max(16, requiredChunks);
        foreach ((RefRO<SurfaceChunk> chunk, Entity entity) in
                 SystemAPI.Query<RefRO<SurfaceChunk>>().WithEntityAccess())
            chunks[chunk.ValueRO.Key] = entity;

        foreach ((RefRO<Foundation> foundation, Entity entity) in
                 SystemAPI.Query<RefRO<Foundation>>().WithEntityAccess())
        {
            Foundation data = foundation.ValueRO;
            SurfaceChunkKey key = SurfaceChunkUtility.GetChunkKey(data.Cell);
            if (!chunks.TryGetValue(key, out Entity chunk)) continue;
            surfaces[data.Cell] = new SurfaceRecord
            {
                Foundation = entity,
                Chunk = chunk,
                VisualMaterialId = data.VisualMaterialId,
                Permissions = data.Permissions,
                OccludesFaces = data.OccludesFaces,
                ShapeFlags = data.ShapeFlags == SurfaceShapeFlags.None
                    ? SurfaceShapeFlags.Flat
                    : data.ShapeFlags
            };
        }
    }

    private void EnsureSurfaceCapacity(int required)
    {
        if (surfaces.Capacity >= required) return;
        surfaces.Capacity = math.max(required, math.max(256, surfaces.Capacity * 2));
    }

    private void IncrementChunkRevision(Entity chunk)
    {
        SurfaceChunk value = EntityManager.GetComponentData<SurfaceChunk>(chunk);
        value.Revision++;
        EntityManager.SetComponentData(chunk, value);
    }

    private void IncrementTopologyRevision(Entity grid)
    {
        SurfaceTopologyRevision revision = EntityManager.GetComponentData<SurfaceTopologyRevision>(grid);
        revision.Value++;
        EntityManager.SetComponentData(grid, revision);
    }

    private void MarkDirty(Entity grid, GridCell cell)
    {
        SurfaceChunkKey key = SurfaceChunkUtility.GetChunkKey(cell);
        AddUnique(EntityManager.GetBuffer<SurfaceRenderDirtyChunk>(grid), key);
        AddUnique(
            EntityManager.GetBuffer<SurfacePhysicsDirtyChunk>(grid),
            SurfaceChunkUtility.GetPhysicsChunkKey(cell));
        int localX = SurfaceChunkUtility.FloorMod(cell.X, SurfaceChunkUtility.ChunkSize);
        int localZ = SurfaceChunkUtility.FloorMod(cell.Z, SurfaceChunkUtility.ChunkSize);
        DynamicBuffer<SurfaceRenderDirtyChunk> render =
            EntityManager.GetBuffer<SurfaceRenderDirtyChunk>(grid);
        if (localX == 0) AddUnique(render, new SurfaceChunkKey(key.ChunkX - 1, key.Level, key.ChunkZ));
        if (localX == SurfaceChunkUtility.ChunkSize - 1) AddUnique(render, new SurfaceChunkKey(key.ChunkX + 1, key.Level, key.ChunkZ));
        if (localZ == 0) AddUnique(render, new SurfaceChunkKey(key.ChunkX, key.Level, key.ChunkZ - 1));
        if (localZ == SurfaceChunkUtility.ChunkSize - 1) AddUnique(render, new SurfaceChunkKey(key.ChunkX, key.Level, key.ChunkZ + 1));
        AddUnique(render, new SurfaceChunkKey(key.ChunkX, key.Level - 1, key.ChunkZ));
        AddUnique(render, new SurfaceChunkKey(key.ChunkX, key.Level + 1, key.ChunkZ));
    }

    private static void AddUnique(DynamicBuffer<SurfaceRenderDirtyChunk> buffer, SurfaceChunkKey key)
    {
        for (int i = 0; i < buffer.Length; i++) if (buffer[i].Value == key) return;
        buffer.Add(new SurfaceRenderDirtyChunk { Value = key });
    }

    private static void AddUnique(
        DynamicBuffer<SurfacePhysicsDirtyChunk> buffer,
        FoundationPhysicsChunkKey key)
    {
        for (int i = 0; i < buffer.Length; i++) if (buffer[i].Value == key) return;
        buffer.Add(new SurfacePhysicsDirtyChunk { Value = key });
    }
}
