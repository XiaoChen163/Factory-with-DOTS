using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

[Flags]
public enum SurfacePermission : byte
{
    None = 0,
    Buildings = 1 << 0,
    Belts = 1 << 1,
    All = Buildings | Belts
}

[Flags]
public enum SurfaceShapeFlags : byte
{
    None = 0,
    Flat = 1 << 0
}

public struct Foundation : IComponentData
{
    public GridCell Cell;
    public ushort VisualMaterialId;
    public SurfacePermission Permissions;
    public byte OccludesFaces;
    public SurfaceShapeFlags ShapeFlags;
}

public struct SurfaceChunk : IComponentData
{
    public SurfaceChunkKey Key;
    public uint Revision;
}

public struct SurfaceChunkOccupancy : IComponentData
{
    public ulong Bits0;
    public ulong Bits1;
    public ulong Bits2;
    public ulong Bits3;

    public bool IsSet(int localIndex)
    {
        if ((uint)localIndex >= SurfaceChunkUtility.CellsPerChunk)
            return false;
        int bit = localIndex & 63;
        ulong mask = 1UL << bit;
        switch (localIndex >> 6)
        {
            case 0: return (Bits0 & mask) != 0;
            case 1: return (Bits1 & mask) != 0;
            case 2: return (Bits2 & mask) != 0;
            default: return (Bits3 & mask) != 0;
        }
    }

    public void Set(int localIndex, bool value)
    {
        if ((uint)localIndex >= SurfaceChunkUtility.CellsPerChunk)
            throw new ArgumentOutOfRangeException(nameof(localIndex));
        int bit = localIndex & 63;
        ulong mask = 1UL << bit;
        switch (localIndex >> 6)
        {
            case 0: Bits0 = value ? Bits0 | mask : Bits0 & ~mask; break;
            case 1: Bits1 = value ? Bits1 | mask : Bits1 & ~mask; break;
            case 2: Bits2 = value ? Bits2 | mask : Bits2 & ~mask; break;
            default: Bits3 = value ? Bits3 | mask : Bits3 & ~mask; break;
        }
    }

    public bool IsEmpty => (Bits0 | Bits1 | Bits2 | Bits3) == 0;
}

[InternalBufferCapacity(16)]
public struct SurfaceCellData : IBufferElementData
{
    public ushort LocalCellIndex;
    public ushort VisualMaterialId;
    public SurfacePermission Permissions;
    public byte OccludesFaces;
    public SurfaceShapeFlags ShapeFlags;
    public Entity Foundation;
}

[InternalBufferCapacity(8)]
public struct SurfaceRenderDirtyChunk : IBufferElementData
{
    public SurfaceChunkKey Value;
}

[InternalBufferCapacity(8)]
public struct SurfacePhysicsDirtyChunk : IBufferElementData
{
    public FoundationPhysicsChunkKey Value;
}

public struct LegacySurfaceInitialized : IComponentData { }

public enum InitialSurfaceMode : byte
{
    Empty,
    LegacyRectangle
}

public struct InitialSurfaceSettings : IComponentData
{
    public InitialSurfaceMode Mode;
}

public struct FoundationPhysicsChunk : IComponentData
{
    public FoundationPhysicsChunkKey Key;
}

public struct FoundationRenderChunk : IComponentData
{
    public SurfaceChunkKey Key;
}

public struct FoundationColliderOwner : ICleanupComponentData
{
    public BlobAssetReference<Collider> Value;
}

public struct FoundationCollisionSettings : IComponentData
{
    public CollisionFilter Filter;
    public Unity.Physics.Material Material;
}

public readonly struct SurfaceChunkKey : IEquatable<SurfaceChunkKey>
{
    public readonly int ChunkX;
    public readonly int Level;
    public readonly int ChunkZ;

    public SurfaceChunkKey(int chunkX, int level, int chunkZ)
    {
        ChunkX = chunkX;
        Level = level;
        ChunkZ = chunkZ;
    }

    public bool Equals(SurfaceChunkKey other) =>
        ChunkX == other.ChunkX && Level == other.Level && ChunkZ == other.ChunkZ;
    public override bool Equals(object obj) => obj is SurfaceChunkKey other && Equals(other);
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = ChunkX;
            hash = (hash * 397) ^ Level;
            return (hash * 397) ^ ChunkZ;
        }
    }
    public static bool operator ==(SurfaceChunkKey a, SurfaceChunkKey b) => a.Equals(b);
    public static bool operator !=(SurfaceChunkKey a, SurfaceChunkKey b) => !a.Equals(b);
    public override string ToString() => $"({ChunkX}, L{Level}, {ChunkZ})";
}

public readonly struct FoundationPhysicsChunkKey :
    IEquatable<FoundationPhysicsChunkKey>
{
    public readonly int ChunkX;
    public readonly int LevelBand;
    public readonly int ChunkZ;

    public FoundationPhysicsChunkKey(int chunkX, int levelBand, int chunkZ)
    {
        ChunkX = chunkX;
        LevelBand = levelBand;
        ChunkZ = chunkZ;
    }

    public bool Equals(FoundationPhysicsChunkKey other) =>
        ChunkX == other.ChunkX &&
        LevelBand == other.LevelBand &&
        ChunkZ == other.ChunkZ;
    public override bool Equals(object obj) =>
        obj is FoundationPhysicsChunkKey other && Equals(other);
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = ChunkX;
            hash = (hash * 397) ^ LevelBand;
            return (hash * 397) ^ ChunkZ;
        }
    }
    public static bool operator ==(
        FoundationPhysicsChunkKey left,
        FoundationPhysicsChunkKey right) => left.Equals(right);
    public static bool operator !=(
        FoundationPhysicsChunkKey left,
        FoundationPhysicsChunkKey right) => !left.Equals(right);
    public override string ToString() =>
        $"({ChunkX}, B{LevelBand}, {ChunkZ})";
}

public static class FoundationCollisionCategories
{
    public const uint Foundation = 1u << 0;
    public const uint Building = 1u << 1;
    public const uint TransportItem = 1u << 2;
    public const uint QueryRay = 1u << 3;

    public static CollisionFilter FoundationFilter => new CollisionFilter
    {
        BelongsTo = Foundation,
        CollidesWith = Building | TransportItem | QueryRay,
        GroupIndex = 0
    };

    public static CollisionFilter FoundationQueryFilter => new CollisionFilter
    {
        BelongsTo = QueryRay,
        CollidesWith = Foundation,
        GroupIndex = 0
    };
}

public static class SurfaceChunkUtility
{
    public const int ChunkSize = 16;
    public const int CellsPerChunk = ChunkSize * ChunkSize;
    public const int PhysicsLevelBandSize = 8;

    public static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        int remainder = value % divisor;
        return remainder < 0 ? quotient - 1 : quotient;
    }

    public static int FloorMod(int value, int divisor)
    {
        int result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    public static SurfaceChunkKey GetChunkKey(GridCell cell) => new SurfaceChunkKey(
        FloorDiv(cell.X, ChunkSize), cell.Level, FloorDiv(cell.Z, ChunkSize));

    public static int GetLocalCellIndex(GridCell cell) =>
        FloorMod(cell.Z, ChunkSize) * ChunkSize + FloorMod(cell.X, ChunkSize);

    public static GridCell GetCell(SurfaceChunkKey key, int localCellIndex) => new GridCell(
        key.ChunkX * ChunkSize + localCellIndex % ChunkSize,
        key.Level,
        key.ChunkZ * ChunkSize + localCellIndex / ChunkSize);

    public static FoundationPhysicsChunkKey GetPhysicsChunkKey(GridCell cell) =>
        new FoundationPhysicsChunkKey(
            FloorDiv(cell.X, ChunkSize),
            FloorDiv(cell.Level, PhysicsLevelBandSize),
            FloorDiv(cell.Z, ChunkSize));

    public static int GetPhysicsBandMinimumLevel(
        FoundationPhysicsChunkKey key) =>
        key.LevelBand * PhysicsLevelBandSize;
}
