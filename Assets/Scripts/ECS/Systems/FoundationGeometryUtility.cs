using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

public enum FoundationFaceDirection : byte
{
    NegativeX,
    PositiveX,
    NegativeY,
    PositiveY,
    NegativeZ,
    PositiveZ
}

public readonly struct FoundationVoxel
{
    public FoundationVoxel(GridCell cell, ushort materialId = 0, bool occludesFaces = true)
    {
        Cell = cell;
        VisualMaterialId = materialId;
        OccludesFaces = occludesFaces;
    }
    public GridCell Cell { get; }
    public ushort VisualMaterialId { get; }
    public bool OccludesFaces { get; }
}

public readonly struct FoundationQuad
{
    public FoundationQuad(
        float3 a, float3 b, float3 c, float3 d,
        float3 normal, ushort materialId, FoundationFaceDirection direction,
        GridCell sourceCell)
    {
        A = a; B = b; C = c; D = d;
        Normal = normal;
        VisualMaterialId = materialId;
        Direction = direction;
        SourceCell = sourceCell;
    }
    public float3 A { get; }
    public float3 B { get; }
    public float3 C { get; }
    public float3 D { get; }
    public float3 Normal { get; }
    public ushort VisualMaterialId { get; }
    public FoundationFaceDirection Direction { get; }
    public GridCell SourceCell { get; }
}

public static class FoundationMeshGenerator
{
    private readonly struct FaceKey : IEquatable<FaceKey>
    {
        public FaceKey(FoundationFaceDirection direction, ushort material, int plane, int u, int v)
        { Direction = direction; Material = material; Plane = plane; U = u; V = v; }
        public readonly FoundationFaceDirection Direction;
        public readonly ushort Material;
        public readonly int Plane;
        public readonly int U;
        public readonly int V;
        public bool Equals(FaceKey other) => Direction == other.Direction && Material == other.Material &&
            Plane == other.Plane && U == other.U && V == other.V;
        public override bool Equals(object obj) => obj is FaceKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Direction;
                hash = (hash * 397) ^ Material;
                hash = (hash * 397) ^ Plane;
                hash = (hash * 397) ^ U;
                return (hash * 397) ^ V;
            }
        }
    }

    private static readonly GridCell[] Neighbors =
    {
        new GridCell(-1, 0, 0), new GridCell(1, 0, 0),
        new GridCell(0, -1, 0), new GridCell(0, 1, 0),
        new GridCell(0, 0, -1), new GridCell(0, 0, 1)
    };

    public static List<FoundationQuad> ExtractVisibleFaces(
        IReadOnlyList<FoundationVoxel> voxels)
    {
        Dictionary<GridCell, FoundationVoxel> lookup =
            new Dictionary<GridCell, FoundationVoxel>(voxels.Count);
        for (int i = 0; i < voxels.Count; i++) lookup[voxels[i].Cell] = voxels[i];
        List<FoundationQuad> result = new List<FoundationQuad>();
        for (int i = 0; i < voxels.Count; i++)
        {
            FoundationVoxel voxel = voxels[i];
            for (int face = 0; face < 6; face++)
            {
                GridCell offset = Neighbors[face];
                GridCell neighbor = new GridCell(
                    voxel.Cell.X + offset.X,
                    voxel.Cell.Level + offset.Level,
                    voxel.Cell.Z + offset.Z);
                if (lookup.TryGetValue(neighbor, out FoundationVoxel adjacent) &&
                    adjacent.OccludesFaces)
                    continue;
                result.Add(CreateQuad(voxel, (FoundationFaceDirection)face));
            }
        }
        return result;
    }

    public static SortedDictionary<ushort, List<FoundationQuad>> GroupByMaterial(
        IReadOnlyList<FoundationQuad> quads)
    {
        SortedDictionary<ushort, List<FoundationQuad>> groups = new();
        for (int i = 0; i < quads.Count; i++)
        {
            FoundationQuad quad = quads[i];
            if (!groups.TryGetValue(quad.VisualMaterialId, out List<FoundationQuad> group))
            {
                group = new List<FoundationQuad>();
                groups.Add(quad.VisualMaterialId, group);
            }
            group.Add(quad);
        }
        return groups;
    }

    public static List<FoundationQuad> MergeCoplanarFaces(
        IReadOnlyList<FoundationQuad> unitFaces)
    {
        Dictionary<FaceKey, FoundationQuad> remaining = new(unitFaces.Count);
        List<FaceKey> order = new(unitFaces.Count);
        for (int i = 0; i < unitFaces.Count; i++)
        {
            FaceKey key = GetFaceKey(unitFaces[i]);
            remaining[key] = unitFaces[i];
            order.Add(key);
        }
        order.Sort(CompareFaceKeys);
        List<FoundationQuad> result = new();
        for (int i = 0; i < order.Count; i++)
        {
            FaceKey origin = order[i];
            if (!remaining.ContainsKey(origin)) continue;
            int width = 1;
            while (remaining.ContainsKey(new FaceKey(
                       origin.Direction, origin.Material, origin.Plane,
                       origin.U + width, origin.V))) width++;
            int height = 1;
            while (CanExpandFaceRow(remaining, origin, width, height)) height++;
            for (int v = 0; v < height; v++)
            for (int u = 0; u < width; u++)
                remaining.Remove(new FaceKey(
                    origin.Direction, origin.Material, origin.Plane,
                    origin.U + u, origin.V + v));
            result.Add(CreateMergedQuad(origin, width, height));
        }
        return result;
    }

    private static bool CanExpandFaceRow(
        Dictionary<FaceKey, FoundationQuad> remaining,
        FaceKey origin,
        int width,
        int height)
    {
        for (int u = 0; u < width; u++)
            if (!remaining.ContainsKey(new FaceKey(
                    origin.Direction, origin.Material, origin.Plane,
                    origin.U + u, origin.V + height))) return false;
        return true;
    }

    private static FaceKey GetFaceKey(FoundationQuad face)
    {
        GridCell cell = face.SourceCell;
        switch (face.Direction)
        {
            case FoundationFaceDirection.NegativeX:
                return new FaceKey(face.Direction, face.VisualMaterialId, cell.X, cell.Z, cell.Level);
            case FoundationFaceDirection.PositiveX:
                return new FaceKey(face.Direction, face.VisualMaterialId, cell.X + 1, cell.Z, cell.Level);
            case FoundationFaceDirection.NegativeY:
                return new FaceKey(face.Direction, face.VisualMaterialId, cell.Level - 1, cell.X, cell.Z);
            case FoundationFaceDirection.PositiveY:
                return new FaceKey(face.Direction, face.VisualMaterialId, cell.Level, cell.X, cell.Z);
            case FoundationFaceDirection.NegativeZ:
                return new FaceKey(face.Direction, face.VisualMaterialId, cell.Z, cell.X, cell.Level);
            default:
                return new FaceKey(face.Direction, face.VisualMaterialId, cell.Z + 1, cell.X, cell.Level);
        }
    }

    private static int CompareFaceKeys(FaceKey a, FaceKey b)
    {
        int direction = a.Direction.CompareTo(b.Direction);
        if (direction != 0) return direction;
        int material = a.Material.CompareTo(b.Material);
        if (material != 0) return material;
        int plane = a.Plane.CompareTo(b.Plane);
        if (plane != 0) return plane;
        int v = a.V.CompareTo(b.V);
        return v != 0 ? v : a.U.CompareTo(b.U);
    }

    private static FoundationQuad CreateMergedQuad(FaceKey key, int width, int height)
    {
        float3 a, b, c, d, normal;
        GridCell source;
        switch (key.Direction)
        {
            case FoundationFaceDirection.NegativeX:
            case FoundationFaceDirection.PositiveX:
            {
                float y0 = key.V - 1;
                float y1 = key.V + height - 1;
                float z0 = key.U;
                float z1 = key.U + width;
                source = new GridCell(
                    key.Direction == FoundationFaceDirection.NegativeX ? key.Plane : key.Plane - 1,
                    key.V, key.U);
                if (key.Direction == FoundationFaceDirection.NegativeX)
                {
                    a = new float3(key.Plane,y0,z1); b = new float3(key.Plane,y0,z0);
                    c = new float3(key.Plane,y1,z0); d = new float3(key.Plane,y1,z1); normal = new float3(-1,0,0);
                }
                else
                {
                    a = new float3(key.Plane,y0,z0); b = new float3(key.Plane,y0,z1);
                    c = new float3(key.Plane,y1,z1); d = new float3(key.Plane,y1,z0); normal = new float3(1,0,0);
                }
                break;
            }
            case FoundationFaceDirection.NegativeY:
            case FoundationFaceDirection.PositiveY:
            {
                float x0 = key.U; float x1 = key.U + width;
                float z0 = key.V; float z1 = key.V + height;
                source = new GridCell(key.U,
                    key.Direction == FoundationFaceDirection.NegativeY ? key.Plane + 1 : key.Plane,
                    key.V);
                if (key.Direction == FoundationFaceDirection.NegativeY)
                {
                    a = new float3(x0,key.Plane,z0); b = new float3(x1,key.Plane,z0);
                    c = new float3(x1,key.Plane,z1); d = new float3(x0,key.Plane,z1); normal = new float3(0,-1,0);
                }
                else
                {
                    a = new float3(x0,key.Plane,z1); b = new float3(x1,key.Plane,z1);
                    c = new float3(x1,key.Plane,z0); d = new float3(x0,key.Plane,z0); normal = new float3(0,1,0);
                }
                break;
            }
            default:
            {
                float x0 = key.U; float x1 = key.U + width;
                float y0 = key.V - 1; float y1 = key.V + height - 1;
                source = new GridCell(key.U, key.V,
                    key.Direction == FoundationFaceDirection.NegativeZ ? key.Plane : key.Plane - 1);
                if (key.Direction == FoundationFaceDirection.NegativeZ)
                {
                    a = new float3(x0,y0,key.Plane); b = new float3(x0,y1,key.Plane);
                    c = new float3(x1,y1,key.Plane); d = new float3(x1,y0,key.Plane); normal = new float3(0,0,-1);
                }
                else
                {
                    a = new float3(x1,y0,key.Plane); b = new float3(x1,y1,key.Plane);
                    c = new float3(x0,y1,key.Plane); d = new float3(x0,y0,key.Plane); normal = new float3(0,0,1);
                }
                break;
            }
        }
        return new FoundationQuad(a, b, c, d, normal, key.Material, key.Direction, source);
    }

    private static FoundationQuad CreateQuad(FoundationVoxel voxel, FoundationFaceDirection face)
    {
        float x0 = voxel.Cell.X;
        float x1 = x0 + 1f;
        float y1 = voxel.Cell.Level;
        float y0 = y1 - 1f;
        float z0 = voxel.Cell.Z;
        float z1 = z0 + 1f;
        ushort material = voxel.VisualMaterialId;
        switch (face)
        {
            case FoundationFaceDirection.NegativeX:
                return new FoundationQuad(new float3(x0,y0,z1), new float3(x0,y0,z0), new float3(x0,y1,z0), new float3(x0,y1,z1), new float3(-1,0,0), material, face, voxel.Cell);
            case FoundationFaceDirection.PositiveX:
                return new FoundationQuad(new float3(x1,y0,z0), new float3(x1,y0,z1), new float3(x1,y1,z1), new float3(x1,y1,z0), new float3(1,0,0), material, face, voxel.Cell);
            case FoundationFaceDirection.NegativeY:
                return new FoundationQuad(new float3(x0,y0,z0), new float3(x1,y0,z0), new float3(x1,y0,z1), new float3(x0,y0,z1), new float3(0,-1,0), material, face, voxel.Cell);
            case FoundationFaceDirection.PositiveY:
                return new FoundationQuad(new float3(x0,y1,z1), new float3(x1,y1,z1), new float3(x1,y1,z0), new float3(x0,y1,z0), new float3(0,1,0), material, face, voxel.Cell);
            case FoundationFaceDirection.NegativeZ:
                return new FoundationQuad(new float3(x0,y0,z0), new float3(x0,y1,z0), new float3(x1,y1,z0), new float3(x1,y0,z0), new float3(0,0,-1), material, face, voxel.Cell);
            default:
                return new FoundationQuad(new float3(x1,y0,z1), new float3(x1,y1,z1), new float3(x0,y1,z1), new float3(x0,y0,z1), new float3(0,0,1), material, face, voxel.Cell);
        }
    }
}

public readonly struct FoundationBox
{
    public FoundationBox(int3 minimum, int3 size) { Minimum = minimum; Size = size; }
    public int3 Minimum { get; }
    public int3 Size { get; }
}

public static class FoundationGreedyBoxBuilder
{
    public static List<FoundationBox> Build(IEnumerable<int3> input)
    {
        HashSet<int3> remaining = new HashSet<int3>(input);
        List<int3> ordered = new List<int3>(remaining);
        ordered.Sort(Compare);
        List<FoundationBox> boxes = new List<FoundationBox>();
        for (int orderedIndex = 0; orderedIndex < ordered.Count; orderedIndex++)
        {
            int3 origin = ordered[orderedIndex];
            if (!remaining.Contains(origin)) continue;
            int sizeX = 1;
            while (remaining.Contains(origin + new int3(sizeX, 0, 0))) sizeX++;
            int sizeZ = 1;
            while (CanExpandZ(remaining, origin, sizeX, sizeZ)) sizeZ++;
            int sizeY = 1;
            while (CanExpandY(remaining, origin, sizeX, sizeZ, sizeY)) sizeY++;
            int3 size = new int3(sizeX, sizeY, sizeZ);
            for (int y = 0; y < sizeY; y++)
            for (int z = 0; z < sizeZ; z++)
            for (int x = 0; x < sizeX; x++)
                remaining.Remove(origin + new int3(x, y, z));
            boxes.Add(new FoundationBox(origin, size));
        }
        return boxes;
    }

    private static bool CanExpandZ(HashSet<int3> cells, int3 origin, int width, int z)
    {
        for (int x = 0; x < width; x++)
            if (!cells.Contains(origin + new int3(x, 0, z))) return false;
        return true;
    }

    private static bool CanExpandY(HashSet<int3> cells, int3 origin, int width, int depth, int y)
    {
        for (int z = 0; z < depth; z++)
        for (int x = 0; x < width; x++)
            if (!cells.Contains(origin + new int3(x, y, z))) return false;
        return true;
    }

    private static int Compare(int3 a, int3 b)
    {
        int y = a.y.CompareTo(b.y);
        if (y != 0) return y;
        int z = a.z.CompareTo(b.z);
        return z != 0 ? z : a.x.CompareTo(b.x);
    }
}

public static class FoundationCompoundColliderBuilder
{
    public static BlobAssetReference<Collider> Build(
        IReadOnlyList<FoundationBox> boxes,
        CollisionFilter filter,
        Unity.Physics.Material material,
        float cellSize = 1f,
        float layerHeight = 1f)
    {
        if (boxes.Count == 0) return default;
        NativeArray<CompoundCollider.ColliderBlobInstance> children =
            new NativeArray<CompoundCollider.ColliderBlobInstance>(boxes.Count, Allocator.Temp);
        try
        {
            for (int i = 0; i < boxes.Count; i++)
            {
                FoundationBox box = boxes[i];
                float3 size = new float3(
                    box.Size.x * cellSize,
                    box.Size.y * layerHeight,
                    box.Size.z * cellSize);
                BlobAssetReference<Collider> child = BoxCollider.Create(
                    new BoxGeometry
                    {
                        Center = float3.zero,
                        Orientation = quaternion.identity,
                        Size = size,
                        BevelRadius = 0f
                    }, filter, material);
                children[i] = new CompoundCollider.ColliderBlobInstance
                {
                    Collider = child,
                    CompoundFromChild = new RigidTransform(
                        quaternion.identity,
                        new float3(
                            (box.Minimum.x + box.Size.x * 0.5f) * cellSize,
                            (box.Minimum.y + box.Size.y * 0.5f) * layerHeight,
                            (box.Minimum.z + box.Size.z * 0.5f) * cellSize))
                };
            }
            BlobAssetReference<Collider> compound = CompoundCollider.Create(children);
            for (int i = 0; i < children.Length; i++) children[i].Collider.Dispose();
            return compound;
        }
        catch
        {
            for (int i = 0; i < children.Length; i++)
                if (children[i].Collider.IsCreated) children[i].Collider.Dispose();
            throw;
        }
        finally
        {
            children.Dispose();
        }
    }
}
