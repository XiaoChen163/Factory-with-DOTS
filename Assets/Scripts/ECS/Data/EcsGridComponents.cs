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
    public int2 Value;
}

public struct GridPlacement : IComponentData
{
    public int2 AnchorCell;
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

public readonly struct BeltPathCell
{
    public BeltPathCell(int2 cell, int2 direction)
    {
        Cell = cell;
        Direction = direction;
    }

    public int2 Cell { get; }
    public int2 Direction { get; }
}

public static class EcsGridUtility
{
    public const float DefaultCellSize = 1f;

    public static int2 WorldToCell(float3 worldPosition)
    {
        return new int2(
            (int)math.floor(worldPosition.x),
            (int)math.floor(worldPosition.z));
    }

    public static int2 WorldToCell(
        float3 worldPosition,
        in GridDefinition grid)
    {
        float cellSize = math.max(math.EPSILON, grid.CellSize);
        return new int2(
            (int)math.floor(
                (worldPosition.x - grid.Origin.x) / cellSize),
            (int)math.floor(
                (worldPosition.z - grid.Origin.z) / cellSize));
    }

    public static float3 CellToWorldCenter(
        int2 cell,
        float worldY,
        in GridDefinition grid)
    {
        float cellSize = math.max(math.EPSILON, grid.CellSize);
        return new float3(
            grid.Origin.x + (cell.x + 0.5f) * cellSize,
            worldY,
            grid.Origin.z + (cell.y + 0.5f) * cellSize);
    }

    public static bool Contains(in GridDefinition grid, int2 cell)
    {
        return cell.x >= 0 &&
               cell.y >= 0 &&
               cell.x < grid.Size.x &&
               cell.y < grid.Size.y;
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

    public static int2 GetBuildingCell(
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
        int2 start,
        int2 end,
        bool horizontalFirst,
        int2 initialDirection)
    {
        List<int2> cells = new List<int2>();
        int2 corner = horizontalFirst
            ? new int2(end.x, start.y)
            : new int2(start.x, end.y);

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
                direction = cells[i + 1] - cells[i];
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
        List<int2> cells,
        int2 from,
        int2 to,
        bool includeStart)
    {
        int2 delta = to - from;
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
}
