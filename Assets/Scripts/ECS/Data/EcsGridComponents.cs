using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

public struct GridDefinition : IComponentData
{
    public int2 Size;
    public float3 Origin;
    public float CellSize;
    public uint Revision;
}

public struct PendingOccupancyAdd : IComponentData
{
}

[InternalBufferCapacity(64)]
public struct BeltVisualDirtyCell : IBufferElementData
{
    public GridCell Value;
}

public struct GridPlacement : IComponentData
{
    public GridCell AnchorCell;
    public int2 FootprintSize;
    public byte QuarterTurns;
    public BuildingKind Kind;
}

[InternalBufferCapacity(4)]
public struct OccupiedCellOffset : IBufferElementData
{
    public int2 Value;
}

public enum BuildingPortType : byte
{
    Input,
    Output
}

[InternalBufferCapacity(5)]
public struct BuildingPort : IBufferElementData
{
    // CellOffset and Direction are expressed in the building's local
    // grid space, where +X is the unrotated forward direction.
    public int2 CellOffset;
    public int2 Direction;
    public BuildingPortType Type;
    public byte Index;
}

public struct BuildingPortVisual : IComponentData
{
    public Entity Owner;
    public BuildingPortType Type;
    public byte PortIndex;
}

public struct SurfaceTopologyRevision : IComponentData
{
    public uint Value;
}

public struct BuildingOccupancyRevision : IComponentData
{
    public uint Value;
}

public struct TransportTopologyRevision : IComponentData
{
    public uint Value;
}

public struct TransportVisualRevision : IComponentData
{
    public uint Value;
}

/// <summary>
/// Explicit opt-in for realigning an existing placement after an editor or
/// repair operation. Runtime-created buildings receive their final transform
/// at creation time and never need this marker.
/// </summary>
public struct GridTransformDirty : IComponentData
{
}

[Serializable]
public struct GridCell : IEquatable<GridCell>
{
    public int X;
    public int Level;
    public int Z;

    public GridCell(int x, int z)
        : this(x, 0, z) { }

    public GridCell(int x, int level, int z)
    {
        X = x;
        Level = level;
        Z = z;
    }

    public GridCell(int2 horizontal, int level = 0)
        : this(horizontal.x, level, horizontal.y) { }

    public int2 Horizontal => new int2(X, Z);
    public static implicit operator GridCell(int2 horizontal) =>
        new GridCell(horizontal, 0);
    public bool Equals(GridCell other) =>
        X == other.X && Level == other.Level && Z == other.Z;
    public override bool Equals(object obj) =>
        obj is GridCell other && Equals(other);
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = X;
            hash = (hash * 397) ^ Level;
            return (hash * 397) ^ Z;
        }
    }
    public override string ToString() => $"({X}, L{Level}, {Z})";
    public static GridCell LevelZero(int2 horizontal) => new GridCell(horizontal);
    public static GridCell operator +(GridCell cell, int2 offset) =>
        new GridCell(cell.X + offset.x, cell.Level, cell.Z + offset.y);
    public static GridCell operator -(GridCell cell, int2 offset) =>
        new GridCell(cell.X - offset.x, cell.Level, cell.Z - offset.y);
    public static bool operator ==(GridCell left, GridCell right) => left.Equals(right);
    public static bool operator !=(GridCell left, GridCell right) => !left.Equals(right);
}

public struct WorldGridConfig : IComponentData
{
    public float CellSize;
    public float LayerHeight;
    public float3 Origin;
}

public readonly struct BeltPathCell
{
    public BeltPathCell(GridCell cell, int2 direction)
    {
        Cell = cell;
        Direction = direction;
    }

    public GridCell Cell { get; }
    public int2 Direction { get; }
}

public static class EcsGridUtility
{
    public const float DefaultCellSize = 1f;
    public const float DefaultLayerHeight = 1f;

    public static GridCell WorldToCell(float3 worldPosition)
    {
        return WorldToCell(
            worldPosition,
            float3.zero,
            DefaultCellSize,
            0);
    }

    public static GridCell WorldToCell(
        float3 worldPosition,
        in GridDefinition grid)
    {
        return WorldToCell(worldPosition, grid.Origin, grid.CellSize, 0);
    }

    public static float3 CellToWorldCenter(
        GridCell cell,
        float worldY,
        in GridDefinition grid)
    {
        return CellToWorldCenter(cell, worldY, grid.Origin, grid.CellSize);
    }

    public static bool Contains(in GridDefinition grid, GridCell cell)
    {
        return cell.Level == 0 &&
               cell.X >= 0 &&
               cell.Z >= 0 &&
               cell.X < grid.Size.x &&
               cell.Z < grid.Size.y;
    }

    public static int2 SanitizeDirection(int2 direction)
    {
        if (math.abs(direction.x) >= math.abs(direction.y) &&
            direction.x != 0)
        {
            return new int2(direction.x > 0 ? 1 : -1, 0);
        }

        if (direction.y != 0)
        {
            return new int2(0, direction.y > 0 ? 1 : -1);
        }

        return new int2(1, 0);
    }

    public static byte QuarterTurnsFromDirection(int2 direction)
    {
        direction = SanitizeDirection(direction);
        if (direction.y > 0)
        {
            return 1;
        }

        if (direction.x < 0)
        {
            return 2;
        }

        return direction.y < 0 ? (byte)3 : (byte)0;
    }

    public static int2 Rotate(int2 value, int quarterTurns)
    {
        switch (NormalizeQuarterTurns(quarterTurns))
        {
            case 0:
                return value;
            case 1:
                return new int2(-value.y, value.x);
            case 2:
                return -value;
            default:
                return new int2(value.y, -value.x);
        }
    }

    public static float2 Rotate(float2 value, int quarterTurns)
    {
        switch (NormalizeQuarterTurns(quarterTurns))
        {
            case 0:
                return value;
            case 1:
                return new float2(-value.y, value.x);
            case 2:
                return -value;
            default:
                return new float2(value.y, -value.x);
        }
    }

    public static float2 GetVisualCenterOffset(
        int2 footprintSize)
    {
        return new float2(
            math.max(0, footprintSize.x - 1) * 0.5f,
            math.max(0, footprintSize.y - 1) * 0.5f);
    }

    public static int2 RotateBuildingCellOffset(
        int2 cellOffset,
        int2 footprintSize,
        int quarterTurns)
    {
        // Keep footprint rotation entirely in integer space. Rotating around
        // a half-cell center and rounding collapses cells for even-by-odd
        // footprints (for example 2x3 at 90 degrees).
        switch (NormalizeQuarterTurns(quarterTurns))
        {
            case 0:
                return cellOffset;
            case 1:
                return new int2(
                    footprintSize.y - 1 - cellOffset.y,
                    cellOffset.x);
            case 2:
                return new int2(
                    footprintSize.x - 1 - cellOffset.x,
                    footprintSize.y - 1 - cellOffset.y);
            default:
                return new int2(
                    cellOffset.y,
                    footprintSize.x - 1 - cellOffset.x);
        }
    }

    public static GridCell WorldToCell(
        float3 worldPosition,
        in WorldGridConfig grid)
    {
        float cellSize = math.max(math.EPSILON, grid.CellSize);
        float layerHeight = math.max(math.EPSILON, grid.LayerHeight);
        int level = (int)math.round(
            (worldPosition.y - grid.Origin.y) / layerHeight);
        return WorldToCell(worldPosition, grid.Origin, cellSize, level);
    }

    public static float3 CellToWorldCenter(
        GridCell cell,
        in WorldGridConfig grid)
    {
        float layerHeight = math.max(math.EPSILON, grid.LayerHeight);
        return CellToWorldCenter(
            cell,
            grid.Origin.y + cell.Level * layerHeight,
            grid.Origin,
            grid.CellSize);
    }

    public static GridCell GetBuildingCell(
        in GridPlacement placement,
        int2 localCellOffset)
    {
        return placement.AnchorCell + RotateBuildingCellOffset(
            localCellOffset,
            placement.FootprintSize,
            placement.QuarterTurns);
    }

    public static quaternion RotationFromQuarterTurns(int quarterTurns)
    {
        return quaternion.RotateY(
            -NormalizeQuarterTurns(quarterTurns) *
            (math.PI * 0.5f));
    }

    public static List<BeltPathCell> BuildBeltPath(
        GridCell start,
        GridCell end,
        bool horizontalFirst,
        int2 initialDirection)
    {
        if (start.Level != end.Level)
        {
            return new List<BeltPathCell>();
        }

        List<GridCell> cells = new List<GridCell>();
        GridCell corner = horizontalFirst
            ? new GridCell(end.X, start.Level, start.Z)
            : new GridCell(start.X, start.Level, end.Z);

        AppendSegment(cells, start, corner, true);
        AppendSegment(cells, corner, end, false);

        List<BeltPathCell> path =
            new List<BeltPathCell>(cells.Count);
        int2 fallbackDirection = SanitizeDirection(initialDirection);
        for (int i = 0; i < cells.Count; i++)
        {
            int2 direction;
            if (i < cells.Count - 1)
            {
                direction = cells[i + 1].Horizontal - cells[i].Horizontal;
                fallbackDirection = direction;
            }
            else
            {
                direction = fallbackDirection;
            }

            path.Add(new BeltPathCell(cells[i], direction));
        }

        return path;
    }

    private static void AppendSegment(
        List<GridCell> cells,
        GridCell from,
        GridCell to,
        bool includeStart)
    {
        int2 delta = to.Horizontal - from.Horizontal;
        int2 step = new int2(
            delta.x == 0 ? 0 : delta.x > 0 ? 1 : -1,
            delta.y == 0 ? 0 : delta.y > 0 ? 1 : -1);
        int length = math.abs(delta.x) + math.abs(delta.y);
        int first = includeStart ? 0 : 1;
        for (int i = first; i <= length; i++)
        {
            cells.Add(from + step * i);
        }
    }

    private static int NormalizeQuarterTurns(int quarterTurns)
    {
        int normalized = quarterTurns % 4;
        return normalized < 0 ? normalized + 4 : normalized;
    }

    private static GridCell WorldToCell(
        float3 worldPosition,
        float3 origin,
        float cellSize,
        int level)
    {
        cellSize = math.max(math.EPSILON, cellSize);
        return new GridCell(
            (int)math.floor((worldPosition.x - origin.x) / cellSize),
            level,
            (int)math.floor((worldPosition.z - origin.z) / cellSize));
    }

    private static float3 CellToWorldCenter(
        GridCell cell,
        float worldY,
        float3 origin,
        float cellSize)
    {
        cellSize = math.max(math.EPSILON, cellSize);
        return new float3(
            origin.x + (cell.X + 0.5f) * cellSize,
            worldY,
            origin.z + (cell.Z + 0.5f) * cellSize);
    }
}
