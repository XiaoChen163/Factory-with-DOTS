using System.Collections.Generic;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Rendering;

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class SurfaceChunkMeshBuildSystem : SystemBase
{
    private sealed class RuntimeChunk
    {
        public Entity Entity;
        public Mesh Mesh;
    }

    private readonly Dictionary<SurfaceChunkKey, RuntimeChunk> runtimeChunks = new();
    private EntityQuery gridQuery;
    private EntityQuery materialQuery;

    protected override void OnCreate()
    {
        gridQuery = GetEntityQuery(
            ComponentType.ReadOnly<WorldGridConfig>(),
            ComponentType.ReadWrite<SurfaceRenderDirtyChunk>());
        materialQuery = GetEntityQuery(ComponentType.ReadOnly<FoundationVisualMaterial>());
    }

    protected override void OnDestroy()
    {
        foreach (RuntimeChunk chunk in runtimeChunks.Values)
            if (chunk.Mesh != null) Object.Destroy(chunk.Mesh);
        runtimeChunks.Clear();
    }

    protected override void OnUpdate()
    {
        if (gridQuery.CalculateEntityCount() != 1 ||
            materialQuery.CalculateEntityCount() != 1) return;
        Entity grid = gridQuery.GetSingletonEntity();
        DynamicBuffer<SurfaceRenderDirtyChunk> dirty =
            EntityManager.GetBuffer<SurfaceRenderDirtyChunk>(grid);
        if (dirty.IsEmpty) return;
        using NativeArray<SurfaceRenderDirtyChunk> dirtySnapshot =
            dirty.ToNativeArray(Allocator.Temp);
        DynamicBuffer<FoundationVisualMaterial> materialBuffer =
            EntityManager.GetBuffer<FoundationVisualMaterial>(
                materialQuery.GetSingletonEntity(), true);
        if (materialBuffer.IsEmpty) return;
        Material[] materials = new Material[materialBuffer.Length];
        for (int i = 0; i < materials.Length; i++) materials[i] = materialBuffer[i].Value.Value;
        SurfaceRegistrySystem registry = World.GetExistingSystemManaged<SurfaceRegistrySystem>();
        if (registry == null || !registry.IsReady) return;

        WorldGridConfig config = EntityManager.GetComponentData<WorldGridConfig>(grid);
        dirty.Clear();
        for (int i = 0; i < dirtySnapshot.Length; i++)
            Rebuild(dirtySnapshot[i].Value, config, materials, registry);
    }

    private void Rebuild(
        SurfaceChunkKey key,
        in WorldGridConfig config,
        Material[] catalog,
        SurfaceRegistrySystem registry)
    {
        if (!registry.TryGetChunk(key, out Entity chunkEntity) ||
            !EntityManager.Exists(chunkEntity))
        {
            DestroyRuntimeChunk(key);
            return;
        }
        DynamicBuffer<SurfaceCellData> cells =
            EntityManager.GetBuffer<SurfaceCellData>(chunkEntity, true);
        List<FoundationVoxel> voxels = new List<FoundationVoxel>(cells.Length + 68);
        for (int i = 0; i < cells.Length; i++)
        {
            GridCell cell = SurfaceChunkUtility.GetCell(key, cells[i].LocalCellIndex);
            voxels.Add(new FoundationVoxel(cell, cells[i].VisualMaterialId, cells[i].OccludesFaces != 0));
        }
        AddOpaqueBorderNeighbors(key, registry, voxels);
        List<FoundationQuad> allFaces = FoundationMeshGenerator.ExtractVisibleFaces(voxels);
        List<FoundationQuad> faces = new List<FoundationQuad>();
        for (int i = 0; i < allFaces.Count; i++)
            if (SurfaceChunkUtility.GetChunkKey(allFaces[i].SourceCell) == key)
                faces.Add(allFaces[i]);
        faces = FoundationMeshGenerator.MergeCoplanarFaces(faces);
        if (faces.Count == 0)
        {
            DestroyRuntimeChunk(key);
            return;
        }

        SortedDictionary<ushort, List<FoundationQuad>> groups =
            FoundationMeshGenerator.GroupByMaterial(faces);
        Mesh mesh = BuildMesh(key, groups);
        Material[] materials = new Material[groups.Count];
        MaterialMeshIndex[] indices = new MaterialMeshIndex[groups.Count];
        int materialIndex = 0;
        foreach (ushort id in groups.Keys)
        {
            materials[materialIndex] = catalog[math.min(id, catalog.Length - 1)];
            indices[materialIndex] = new MaterialMeshIndex
            {
                MaterialIndex = materialIndex,
                MeshIndex = 0,
                SubMeshIndex = materialIndex
            };
            materialIndex++;
        }

        DestroyRuntimeChunk(key);
        Entity renderEntity = EntityManager.CreateEntity();
        EntityManager.AddComponentData(renderEntity, new FoundationRenderChunk { Key = key });
        EntityManager.AddComponentData(renderEntity, LocalTransform.FromPosition(config.Origin));
        EntityManager.AddComponentData(renderEntity, new LocalToWorld
        {
            Value = float4x4.Translate(config.Origin)
        });
        EntityManager.AddComponent<Static>(renderEntity);
        RenderMeshArray renderMeshArray = new RenderMeshArray(materials, new[] { mesh }, indices);
        RenderMeshUtility.AddComponents(
            renderEntity,
            EntityManager,
            new RenderMeshDescription(ShadowCastingMode.On, true),
            renderMeshArray,
            MaterialMeshInfo.FromMaterialMeshIndexRange(0, groups.Count));
        runtimeChunks[key] = new RuntimeChunk { Entity = renderEntity, Mesh = mesh };
    }

    private static Mesh BuildMesh(
        SurfaceChunkKey key,
        SortedDictionary<ushort, List<FoundationQuad>> groups)
    {
        Mesh mesh = new Mesh { name = $"FoundationChunk_{key.ChunkX}_{key.Level}_{key.ChunkZ}" };
        int quadCount = 0;
        foreach (List<FoundationQuad> group in groups.Values) quadCount += group.Count;
        if (quadCount * 4 > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
        List<Vector3> vertices = new List<Vector3>(quadCount * 4);
        List<Vector3> normals = new List<Vector3>(quadCount * 4);
        List<Vector2> uvs = new List<Vector2>(quadCount * 4);
        List<List<int>> triangles = new List<List<int>>(groups.Count);
        foreach (List<FoundationQuad> group in groups.Values)
        {
            List<int> groupTriangles = new List<int>(group.Count * 6);
            for (int i = 0; i < group.Count; i++)
            {
                FoundationQuad q = group[i];
                int first = vertices.Count;
                vertices.Add(q.A); vertices.Add(q.B); vertices.Add(q.C); vertices.Add(q.D);
                normals.Add(q.Normal); normals.Add(q.Normal); normals.Add(q.Normal); normals.Add(q.Normal);
                uvs.Add(new Vector2(0,0)); uvs.Add(new Vector2(1,0));
                uvs.Add(new Vector2(1,1)); uvs.Add(new Vector2(0,1));
                groupTriangles.Add(first); groupTriangles.Add(first + 1); groupTriangles.Add(first + 2);
                groupTriangles.Add(first); groupTriangles.Add(first + 2); groupTriangles.Add(first + 3);
            }
            triangles.Add(groupTriangles);
        }
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = triangles.Count;
        for (int i = 0; i < triangles.Count; i++) mesh.SetTriangles(triangles[i], i, false);
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddOpaqueBorderNeighbors(
        SurfaceChunkKey key,
        SurfaceRegistrySystem registry,
        List<FoundationVoxel> voxels)
    {
        int minX = key.ChunkX * SurfaceChunkUtility.ChunkSize;
        int minZ = key.ChunkZ * SurfaceChunkUtility.ChunkSize;
        for (int i = 0; i < SurfaceChunkUtility.ChunkSize; i++)
        {
            AddIfOpaque(new GridCell(minX - 1, key.Level, minZ + i), registry, voxels);
            AddIfOpaque(new GridCell(minX + SurfaceChunkUtility.ChunkSize, key.Level, minZ + i), registry, voxels);
            AddIfOpaque(new GridCell(minX + i, key.Level, minZ - 1), registry, voxels);
            AddIfOpaque(new GridCell(minX + i, key.Level, minZ + SurfaceChunkUtility.ChunkSize), registry, voxels);
        }
        for (int z = 0; z < SurfaceChunkUtility.ChunkSize; z++)
        for (int x = 0; x < SurfaceChunkUtility.ChunkSize; x++)
        {
            AddIfOpaque(new GridCell(minX + x, key.Level - 1, minZ + z), registry, voxels);
            AddIfOpaque(new GridCell(minX + x, key.Level + 1, minZ + z), registry, voxels);
        }
    }

    private static void AddIfOpaque(
        GridCell cell,
        SurfaceRegistrySystem registry,
        List<FoundationVoxel> voxels)
    {
        if (registry.TryGetSurface(cell, out SurfaceRegistrySystem.SurfaceRecord value) &&
            value.OccludesFaces != 0)
            voxels.Add(new FoundationVoxel(cell, value.VisualMaterialId, true));
    }

    private void DestroyRuntimeChunk(SurfaceChunkKey key)
    {
        if (!runtimeChunks.TryGetValue(key, out RuntimeChunk runtime)) return;
        if (EntityManager.Exists(runtime.Entity)) EntityManager.DestroyEntity(runtime.Entity);
        if (runtime.Mesh != null) Object.Destroy(runtime.Mesh);
        runtimeChunks.Remove(key);
    }
}
