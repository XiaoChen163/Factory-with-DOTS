using Unity.Mathematics;

public enum GridSurfaceHitKind : byte
{
    Direct,
    FlatTop,
    FlatBottom,
    FlatSide,
    RampSlope,
    RampSide,
    RampBack,
    RampBottom
}

public struct GridSurfaceHit
{
    public GridCell SourceCell;
    public float3 Position;
    public float3 Normal;
    public GridSurfaceHitKind Kind;
    public RampConnector Ramp;

    public bool IsRamp =>
        Kind == GridSurfaceHitKind.RampSlope ||
        Kind == GridSurfaceHitKind.RampSide ||
        Kind == GridSurfaceHitKind.RampBack ||
        Kind == GridSurfaceHitKind.RampBottom;

    public static GridSurfaceHit Direct(GridCell cell, float3 position) =>
        new GridSurfaceHit
        {
            SourceCell = cell,
            Position = position,
            Kind = GridSurfaceHitKind.Direct
        };
}

public struct ResolvedBuildAnchor
{
    public GridCell Cell;
    public int RampStartHeightUnits;
    public int2 RampUphillDirection;
    public byte IsValid;
}

/// <summary>
/// Converts a physical surface hit into the logical anchor of the building
/// being held. A hit cell identifies the existing object; an anchor cell
/// identifies the new object. Keeping those concepts separate prevents ramp
/// transforms and flat-foundation top levels from being interpreted as the
/// same vertical address.
/// </summary>
public static class GridBuildPlacementResolver
{
    private const float NormalEpsilon = 0.0001f;
    private const float HeightBoundaryEpsilon = 0.0001f;

    public static GridSurfaceHit CreateFlatHit(
        GridCell sourceCell,
        float3 position,
        float3 normal)
    {
        float3 normalized = math.normalizesafe(normal);
        GridSurfaceHitKind kind;
        if (normalized.y > 0.5f)
            kind = GridSurfaceHitKind.FlatTop;
        else if (normalized.y < -0.5f)
            kind = GridSurfaceHitKind.FlatBottom;
        else
            kind = GridSurfaceHitKind.FlatSide;
        return new GridSurfaceHit
        {
            SourceCell = sourceCell,
            Position = position,
            Normal = normalized,
            Kind = kind
        };
    }

    public static GridSurfaceHit CreateRampHit(
        in RampConnector ramp,
        float3 position,
        float3 normal)
    {
        float3 normalized = math.normalizesafe(normal);
        GridSurfaceHitKind kind;
        if (normalized.y > NormalEpsilon)
        {
            kind = GridSurfaceHitKind.RampSlope;
        }
        else if (normalized.y < -NormalEpsilon)
        {
            kind = GridSurfaceHitKind.RampBottom;
        }
        else
        {
            int2 faceDirection = CardinalDirection(normalized.xz);
            kind = math.dot(faceDirection, ramp.UphillDirection) > 0
                ? GridSurfaceHitKind.RampBack
                : GridSurfaceHitKind.RampSide;
        }

        return new GridSurfaceHit
        {
            SourceCell = ramp.Cell,
            Position = position,
            Normal = normalized,
            Kind = kind,
            Ramp = ramp
        };
    }

    public static ResolvedBuildAnchor Resolve(
        in GridSurfaceHit hit,
        BuildingKind heldKind,
        int selectedRampRiseHeightUnits,
        int2 selectedRampDirection,
        in WorldGridConfig grid)
    {
        int2 selectedDirection =
            EcsGridUtility.SanitizeDirection(selectedRampDirection);
        ResolvedBuildAnchor direct = new ResolvedBuildAnchor
        {
            Cell = hit.SourceCell,
            RampStartHeightUnits = checked(
                hit.SourceCell.Level * GridHeight.UnitsPerLayer),
            RampUphillDirection = selectedDirection,
            IsValid = 1
        };

        if (hit.Kind == GridSurfaceHitKind.Direct)
            return direct;
        if (heldKind == BuildingKind.Foundation)
            return hit.IsRamp
                ? ResolveFlatFoundationOnRamp(hit, direct, grid)
                : ResolveFlatFoundationOnFlat(hit, direct);
        if (heldKind == BuildingKind.RampFoundation)
            return hit.IsRamp
                ? ResolveRampOnRamp(
                    hit,
                    math.abs(selectedRampRiseHeightUnits),
                    direct,
                    grid)
                : ResolveRampOnFlat(hit, direct);
        return direct;
    }

    private static ResolvedBuildAnchor ResolveFlatFoundationOnFlat(
        in GridSurfaceHit hit,
        ResolvedBuildAnchor result)
    {
        switch (hit.Kind)
        {
            case GridSurfaceHitKind.FlatTop:
                result.Cell.Level++;
                break;
            case GridSurfaceHitKind.FlatBottom:
                result.Cell.Level--;
                break;
            default:
                result.Cell += CardinalDirection(hit.Normal.xz);
                break;
        }
        return result;
    }

    private static ResolvedBuildAnchor ResolveFlatFoundationOnRamp(
        in GridSurfaceHit hit,
        ResolvedBuildAnchor result,
        in WorldGridConfig grid)
    {
        if (hit.Kind == GridSurfaceHitKind.RampBottom)
        {
            result.IsValid = 0;
            return result;
        }

        int2 direction;
        if (hit.Kind == GridSurfaceHitKind.RampSlope)
            direction = -hit.Ramp.UphillDirection;
        else if (hit.Kind == GridSurfaceHitKind.RampBack)
            direction = hit.Ramp.UphillDirection;
        else
            direction = CardinalDirection(hit.Normal.xz);

        result.Cell = hit.Ramp.Cell + direction;
        result.Cell.Level = ResolveRampHitFoundationTopLevel(hit, grid);
        return result;
    }

    private static ResolvedBuildAnchor ResolveRampOnFlat(
        in GridSurfaceHit hit,
        ResolvedBuildAnchor result)
    {
        if (hit.Kind == GridSurfaceHitKind.FlatTop)
        {
            result.RampStartHeightUnits = checked(
                hit.SourceCell.Level * GridHeight.UnitsPerLayer);
            return result;
        }

        int baseLevel = hit.SourceCell.Level - 1;
        result.Cell = hit.SourceCell;
        if (hit.Kind == GridSurfaceHitKind.FlatSide)
            result.Cell += CardinalDirection(hit.Normal.xz);
        result.Cell.Level = baseLevel;
        result.RampStartHeightUnits = checked(
            baseLevel * GridHeight.UnitsPerLayer);
        return result;
    }

    private static ResolvedBuildAnchor ResolveRampOnRamp(
        in GridSurfaceHit hit,
        int selectedRise,
        ResolvedBuildAnchor result,
        in WorldGridConfig grid)
    {
        if (selectedRise <= 0 || hit.Kind == GridSurfaceHitKind.RampBottom)
        {
            result.IsValid = 0;
            return result;
        }

        int2 uphill = hit.Ramp.UphillDirection;
        int startHeight;
        int2 offset;
        if (hit.Kind == GridSurfaceHitKind.RampSide)
        {
            offset = CardinalDirection(hit.Normal.xz);
            startHeight = hit.Ramp.LowHeight.Units;
        }
        else if (hit.Kind == GridSurfaceHitKind.RampBack ||
                 IsUpperSlopeHalf(hit, grid))
        {
            offset = uphill;
            startHeight = hit.Ramp.HighHeight.Units;
        }
        else
        {
            offset = -uphill;
            startHeight = checked(hit.Ramp.LowHeight.Units - selectedRise);
        }

        result.Cell = hit.Ramp.Cell + offset;
        result.Cell.Level = SurfaceChunkUtility.FloorDiv(
            startHeight,
            GridHeight.UnitsPerLayer);
        result.RampStartHeightUnits = startHeight;
        result.RampUphillDirection = uphill;
        return result;
    }

    private static bool IsUpperSlopeHalf(
        in GridSurfaceHit hit,
        in WorldGridConfig grid)
    {
        float3 center = EcsGridUtility.CellToWorldCenter(hit.Ramp.Cell, grid);
        float2 fromCenter = hit.Position.xz - center.xz;
        return math.dot(fromCenter, hit.Ramp.UphillDirection) >= 0f;
    }

    private static int ResolveRampHitFoundationTopLevel(
        in GridSurfaceHit hit,
        in WorldGridConfig grid)
    {
        float layerHeight = math.max(math.EPSILON, grid.LayerHeight);
        float continuousLevel =
            (hit.Position.y - grid.Origin.y) / layerHeight;
        int hitLevel = (int)math.ceil(
            continuousLevel + HeightBoundaryEpsilon);
        int minimum = SurfaceChunkUtility.FloorDiv(
                          hit.Ramp.LowHeight.Units,
                          GridHeight.UnitsPerLayer) + 1;
        int maximum = CeilDiv(
            hit.Ramp.HighHeight.Units,
            GridHeight.UnitsPerLayer);
        return math.clamp(hitLevel, minimum, math.max(minimum, maximum));
    }

    private static int2 CardinalDirection(float2 direction)
    {
        if (math.abs(direction.x) >= math.abs(direction.y) &&
            math.abs(direction.x) > NormalEpsilon)
            return new int2(direction.x > 0f ? 1 : -1, 0);
        if (math.abs(direction.y) > NormalEpsilon)
            return new int2(0, direction.y > 0f ? 1 : -1);
        return int2.zero;
    }

    private static int CeilDiv(int value, int divisor) =>
        -SurfaceChunkUtility.FloorDiv(-value, divisor);
}
