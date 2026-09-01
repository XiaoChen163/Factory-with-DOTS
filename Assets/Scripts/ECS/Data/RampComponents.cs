using System;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Exact height used by ramp endpoints. Eight units equal one grid layer, so
/// all supported 1, 1/2 and 1/4 slopes are represented without floats.
/// Flat surfaces always use a multiple of UnitsPerLayer.
/// </summary>
[Serializable]
public readonly struct GridHeight : IEquatable<GridHeight>, IComparable<GridHeight>
{
    public const int UnitsPerLayer = 8;
    public readonly int Units;

    public GridHeight(int units) => Units = units;
    public static GridHeight FromLevel(int level) =>
        new GridHeight(checked(level * UnitsPerLayer));
    public bool IsWholeLevel => Units % UnitsPerLayer == 0;
    public int WholeLevel => Units / UnitsPerLayer;
    public float ToWorldY(in WorldGridConfig grid) =>
        grid.Origin.y + Units *
        (math.max(math.EPSILON, grid.LayerHeight) / UnitsPerLayer);
    public int CompareTo(GridHeight other) => Units.CompareTo(other.Units);
    public bool Equals(GridHeight other) => Units == other.Units;
    public override bool Equals(object obj) => obj is GridHeight other && Equals(other);
    public override int GetHashCode() => Units;
    public override string ToString() => $"H{Units}/{UnitsPerLayer}";
    public static bool operator ==(GridHeight left, GridHeight right) => left.Equals(right);
    public static bool operator !=(GridHeight left, GridHeight right) => !left.Equals(right);
}

/// <summary>A one-cell ramp foundation. UphillDirection is always cardinal.</summary>
public struct RampConnector : IComponentData
{
    public GridCell Cell;
    public GridHeight LowHeight;
    public GridHeight HighHeight;
    public int2 UphillDirection;
    public ushort VisualMaterialId;
}

/// <summary>
/// Marks a belt as a straight ramp belt. TravelDirection must be either the
/// connector's uphill direction or its exact opposite.
/// </summary>
public struct RampBelt : IComponentData
{
    public Entity Connector;
    public int2 TravelDirection;
    public GridHeight EntryHeight;
    public GridHeight ExitHeight;
}

public enum TransportConnectionMode : byte
{
    Planar,
    ExplicitOnly,
    PlanarInputOnly,
    PlanarOutputOnly
}

public readonly struct RampEndpointKey : IEquatable<RampEndpointKey>
{
    // Horizontal boundary coordinates are doubled: a cell center is odd and
    // an edge is even on one axis. This avoids floating-point endpoint keys.
    public readonly int BoundaryX2;
    public readonly int BoundaryZ2;
    public readonly int HeightUnits;

    public RampEndpointKey(int boundaryX2, int boundaryZ2, int heightUnits)
    {
        BoundaryX2 = boundaryX2;
        BoundaryZ2 = boundaryZ2;
        HeightUnits = heightUnits;
    }

    public bool Equals(RampEndpointKey other) =>
        BoundaryX2 == other.BoundaryX2 &&
        BoundaryZ2 == other.BoundaryZ2 &&
        HeightUnits == other.HeightUnits;
    public override bool Equals(object obj) => obj is RampEndpointKey other && Equals(other);
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = BoundaryX2;
            hash = (hash * 397) ^ BoundaryZ2;
            return (hash * 397) ^ HeightUnits;
        }
    }
}

public static class RampUtility
{
    // Unity mirrors the OBJ X axis during import, so the authored +X uphill
    // edge is stored as local -X in the imported mesh. Keep gameplay
    // directions in grid space and compensate only when rotating the visual.
    public static quaternion GetFoundationVisualRotation(int2 uphillDirection)
    {
        int quarterTurns = EcsGridUtility.QuarterTurnsFromDirection(
            uphillDirection);
        return EcsGridUtility.RotationFromQuarterTurns(quarterTurns + 2);
    }

    public static bool IsAllowedRise(int riseHeightUnits)
    {
        int rise = math.abs(riseHeightUnits);
        return rise == 2 || rise == 4 || rise == 8;
    }

    /// <summary>
    /// A flat foundation cell stores the level of its top face and occupies
    /// the open vertical interval (Level - 1, Level). A ramp stores its exact
    /// low/high heights. Equal boundaries only touch and are not a conflict.
    /// </summary>
    public static bool OverlapsFoundationVoxel(
        in RampConnector ramp,
        GridCell foundationCell)
    {
        if (!math.all(ramp.Cell.Horizontal == foundationCell.Horizontal))
            return false;
        long foundationBottom =
            ((long)foundationCell.Level - 1L) * GridHeight.UnitsPerLayer;
        long foundationTop =
            (long)foundationCell.Level * GridHeight.UnitsPerLayer;
        return foundationBottom < ramp.HighHeight.Units &&
               foundationTop > ramp.LowHeight.Units;
    }

    public static bool IsTravelDirectionAllowed(
        in RampConnector ramp,
        int2 travelDirection)
    {
        int2 direction = EcsGridUtility.SanitizeDirection(travelDirection);
        return math.all(direction == ramp.UphillDirection) ||
               math.all(direction == -ramp.UphillDirection);
    }

    public static int GetLevelAtHeight(GridHeight height) =>
        SurfaceChunkUtility.FloorDiv(height.Units, GridHeight.UnitsPerLayer);

    public static TransportConnectionMode GetConnectionMode(
        GridCell cell,
        GridHeight entryHeight,
        GridHeight exitHeight)
    {
        int entryLevel = GetLevelAtHeight(entryHeight);
        int exitLevel = GetLevelAtHeight(exitHeight);
        if (entryLevel == exitLevel)
            return TransportConnectionMode.Planar;
        if (cell.Level == entryLevel)
            return TransportConnectionMode.PlanarInputOnly;
        if (cell.Level == exitLevel)
            return TransportConnectionMode.PlanarOutputOnly;
        return TransportConnectionMode.ExplicitOnly;
    }

    public static bool AllowsPlanarInput(TransportConnectionMode mode) =>
        mode == TransportConnectionMode.Planar ||
        mode == TransportConnectionMode.PlanarInputOnly;

    public static bool AllowsPlanarOutput(TransportConnectionMode mode) =>
        mode == TransportConnectionMode.Planar ||
        mode == TransportConnectionMode.PlanarOutputOnly;

    public static RampEndpointKey GetLowEndpoint(in RampConnector ramp) =>
        GetEndpoint(ramp.Cell, -ramp.UphillDirection, ramp.LowHeight);

    public static RampEndpointKey GetHighEndpoint(in RampConnector ramp) =>
        GetEndpoint(ramp.Cell, ramp.UphillDirection, ramp.HighHeight);

    public static RampEndpointKey GetEntryEndpoint(
        in RampConnector ramp,
        int2 travelDirection) =>
        math.all(travelDirection == ramp.UphillDirection)
            ? GetLowEndpoint(ramp)
            : GetHighEndpoint(ramp);

    public static RampEndpointKey GetExitEndpoint(
        in RampConnector ramp,
        int2 travelDirection) =>
        math.all(travelDirection == ramp.UphillDirection)
            ? GetHighEndpoint(ramp)
            : GetLowEndpoint(ramp);

    public static RampEndpointKey GetEndpoint(
        GridCell cell,
        int2 edgeDirection,
        GridHeight height) =>
        new RampEndpointKey(
            cell.X * 2 + 1 + edgeDirection.x,
            cell.Z * 2 + 1 + edgeDirection.y,
            height.Units);

    public static float3 GetSurfaceCenter(
        in RampConnector ramp,
        in WorldGridConfig grid)
    {
        float3 center = EcsGridUtility.CellToWorldCenter(ramp.Cell, grid);
        center.y = (ramp.LowHeight.ToWorldY(grid) +
                    ramp.HighHeight.ToWorldY(grid)) * 0.5f;
        return center;
    }

    public static float GetSlopeLength(
        in RampConnector ramp,
        in WorldGridConfig grid)
    {
        float run = math.max(math.EPSILON, grid.CellSize);
        float rise = math.abs(
            ramp.HighHeight.ToWorldY(grid) - ramp.LowHeight.ToWorldY(grid));
        return math.sqrt(run * run + rise * rise);
    }
}
