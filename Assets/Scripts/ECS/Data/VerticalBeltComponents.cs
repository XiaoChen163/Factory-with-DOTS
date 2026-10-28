using Unity.Entities;

// Each node owns one unit interval [Level, Level + 1) in its X/Z column.
// Foundation surfaces deliberately use a different occupancy channel.
public struct VerticalBeltNode : IComponentData
{
    public BuildingKind Kind;
    public int Step;
}

// Correlates a provisional entry with the player command that created it.
public struct VerticalBeltPlacementToken : IComponentData
{
    public PlayerId Player;
    public ulong RequestId;
}

public struct PendingVerticalRemoval : IComponentData { }

public static class VerticalBeltUtility
{
    public const int MaxPathNodes = 256;

    public static bool TryGetPathLevels(ref FactoryDatabaseBlob database, BuildingLevelId entry,
        out BuildingLevelId segment, out BuildingLevelId exit)
    {
        segment = default;
        exit = default;
        if (!FactoryDatabaseUtility.TryGetBuildingLevel(ref database, entry, out var entryLevel, out var building) ||
            building.Kind != BuildingKind.VerticalBeltEntry) return false;
        for (int i = 1; i < database.BuildingLevelsById.Length; i++)
        {
            var id = database.BuildingLevelsById[i].Id;
            if (!FactoryDatabaseUtility.TryGetBuildingLevel(ref database, id, out var level, out var kind) ||
                level.Level != entryLevel.Level) continue;
            if (kind.Kind == BuildingKind.VerticalBeltSegment) segment = id;
            if (kind.Kind == BuildingKind.VerticalBeltExit) exit = id;
        }
        return segment.IsValid && exit.IsValid;
    }

    public static bool IsVertical(BuildingKind kind) =>
        kind == BuildingKind.VerticalBeltEntry ||
        kind == BuildingKind.VerticalBeltExit ||
        kind == BuildingKind.VerticalBeltSegment;

    public static int Step(byte quarterTurns) => quarterTurns < 2 ? 1 : -1;

    public static TransportConnectionMode ConnectionMode(BuildingKind kind) =>
        kind == BuildingKind.VerticalBeltEntry ? TransportConnectionMode.SinglePlanarInputOnly :
        kind == BuildingKind.VerticalBeltExit ? TransportConnectionMode.PlanarOutputOnly :
        TransportConnectionMode.ExplicitOnly;
    public static bool Validate(BuildingKind kind, GridCell cell, byte quarterTurns,
        System.Func<GridCell, GridPlacement?> lookup, out GridBuildFailureReason failure)
    {
        failure = GridBuildFailureReason.None;

        if (!IsVertical(kind)) return true;
        if (kind == BuildingKind.VerticalBeltEntry)
        {
            GridCell above = cell; above.Level++;
            GridCell below = cell; below.Level--;
            if (TryGet(lookup, above, out GridPlacement up) &&
                up.Kind == BuildingKind.VerticalBeltSegment && Step(up.QuarterTurns) > 0 &&
                TryGet(lookup, below, out GridPlacement down) &&
                down.Kind == BuildingKind.VerticalBeltSegment && Step(down.QuarterTurns) < 0)
            {
                failure = GridBuildFailureReason.VerticalDirectionConflict;
                return false;
            }
            return true;
        }

        if (kind == BuildingKind.VerticalBeltSegment)
        {
            int step = Step(quarterTurns);
            GridCell previous = cell;
            previous.Level -= step;
            if (!TryGet(lookup, previous, out GridPlacement support) ||
                (support.Kind != BuildingKind.VerticalBeltEntry &&
                 support.Kind != BuildingKind.VerticalBeltSegment))
            {
                failure = GridBuildFailureReason.VerticalSupportMissing;
                return false;
            }
            if (support.Kind == BuildingKind.VerticalBeltSegment &&
                Step(support.QuarterTurns) != step)
            {
                failure = GridBuildFailureReason.VerticalDirectionConflict;
                return false;
            }
            // An entry has exactly one vertical output, chosen by its first segment.
            GridCell opposite = previous;
            opposite.Level -= step;
            if (support.Kind == BuildingKind.VerticalBeltEntry &&
                TryGet(lookup, opposite, out GridPlacement other) &&
                other.Kind == BuildingKind.VerticalBeltSegment &&
                Step(other.QuarterTurns) == -step)
            {
                failure = GridBuildFailureReason.VerticalDirectionConflict;
                return false;
            }
            // Do not add a second input to an already supported exit.
            GridCell next = cell; next.Level += step;
            GridCell otherInput = cell; otherInput.Level += step * 2;
            if (TryGet(lookup, next, out GridPlacement target) && target.Kind == BuildingKind.VerticalBeltExit &&
                TryGet(lookup, otherInput, out GridPlacement incoming) &&
                incoming.Kind == BuildingKind.VerticalBeltSegment && Step(incoming.QuarterTurns) == -step)
            {
                failure = GridBuildFailureReason.VerticalDirectionConflict;
                return false;
            }
            return true;
        }
        int inputs = 0;
        for (int step = -1; step <= 1; step += 2)
        {
            GridCell previous = cell;
            previous.Level -= step;
            if (TryGet(lookup, previous, out GridPlacement support) &&
                support.Kind == BuildingKind.VerticalBeltSegment &&
                Step(support.QuarterTurns) == step)
                inputs++;
        }
        if (inputs != 1)
        {
            failure = GridBuildFailureReason.VerticalSupportMissing;
            return false;
        }
        return true;
    }

    private static bool TryGet(System.Func<GridCell, GridPlacement?> lookup,
        GridCell cell, out GridPlacement placement)
    {
        GridPlacement? value = lookup(cell);
        placement = value.GetValueOrDefault();
        return value.HasValue;
    }
}
