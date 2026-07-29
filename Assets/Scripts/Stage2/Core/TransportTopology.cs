using System.Collections.Generic;
using UnityEngine;

public static class TransportTopology
{
    public struct IncomingConnection
    {
        public IncomingConnection(
            GridBuilding source,
            IGridOutputProvider outputProvider,
            int outputIndex)
        {
            Source = source;
            OutputProvider = outputProvider;
            OutputIndex = outputIndex;
        }

        public GridBuilding Source { get; }
        public IGridOutputProvider OutputProvider { get; }
        public int OutputIndex { get; }
        public Vector2Int TravelDirection => OutputProvider.GetOutputDirection(OutputIndex);
    }

    public static void FindIncoming(
        GridMap grid,
        Vector2Int targetCell,
        List<IncomingConnection> results)
    {
        results.Clear();
        if (grid == null)
        {
            return;
        }

        IReadOnlyList<GridBuilding> buildings = grid.Buildings;
        for (int i = 0; i < buildings.Count; i++)
        {
            GridBuilding building = buildings[i];
            if (!(building is IGridOutputProvider outputProvider))
            {
                continue;
            }

            for (int outputIndex = 0; outputIndex < outputProvider.OutputCount; outputIndex++)
            {
                if (outputProvider.GetOutputCell(outputIndex) == targetCell)
                {
                    results.Add(new IncomingConnection(building, outputProvider, outputIndex));
                }
            }
        }

    }

    public static bool HasOutputTo(
        IGridOutputProvider provider,
        Vector2Int targetCell)
    {
        for (int i = 0; i < provider.OutputCount; i++)
        {
            if (provider.GetOutputCell(i) == targetCell)
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryGetRequestTravelDirection(
        ItemTransferRequest request,
        out Vector2Int direction)
    {
        if (request.Source is IGridOutputProvider provider)
        {
            for (int i = 0; i < provider.OutputCount; i++)
            {
                if (provider.GetOutputCell(i) == request.TargetCell)
                {
                    direction = provider.GetOutputDirection(i);
                    return true;
                }
            }
        }

        direction = request.TargetCell - request.SourceCell;
        if (direction.sqrMagnitude > 0)
        {
            direction = new Vector2Int(
                direction.x == 0 ? 0 : direction.x > 0 ? 1 : -1,
                direction.y == 0 ? 0 : direction.y > 0 ? 1 : -1);
            return true;
        }

        return false;
    }

    public static bool CanPlace(
        GridMap grid,
        BuildingKind kind,
        Vector2Int anchor,
        int quarterTurns,
        IReadOnlyList<Vector2Int> occupiedCells,
        out string reason)
    {
        if (grid == null || !grid.CanOccupy(occupiedCells))
        {
            reason = "the footprint is outside the grid or already occupied";
            return false;
        }

        Vector2Int direction = GridDirection.FromQuarterTurns(quarterTurns);
        List<OutputDescriptor> candidateOutputs = new List<OutputDescriptor>(3);
        AddCandidateOutputs(kind, anchor, quarterTurns, direction, candidateOutputs);

        // A splitter has one strict back-side input. A normal belt is deliberately
        // more permissive: if several outputs point at it, its first connection wins
        // and later connections are simply ignored.
        if (kind == BuildingKind.Splitter)
        {
            int incomingCount = CountExistingOutputsTo(grid, anchor);
            if (incomingCount > 1)
            {
                reason = kind + " can have only one input";
                return false;
            }

            if (incomingCount == 1)
            {
                if (!TryGetOnlyIncomingDirection(grid, anchor, out Vector2Int travelDirection) ||
                    travelDirection != direction)
                {
                    reason = "a splitter accepts input only from its back side";
                    return false;
                }
            }
        }

        // A merger accepts three sides but never accepts from its output face.
        if (kind == BuildingKind.Merger)
        {
            List<IncomingConnection> incoming = new List<IncomingConnection>(4);
            FindIncoming(grid, anchor, incoming);
            for (int i = 0; i < incoming.Count; i++)
            {
                if (incoming[i].TravelDirection == -direction)
                {
                    reason = "a merger cannot accept input from its output side";
                    return false;
                }
            }
        }

        for (int i = 0; i < candidateOutputs.Count; i++)
        {
            OutputDescriptor output = candidateOutputs[i];
            GridBuilding target = grid.GetOccupant(output.Cell);
            if (target is SplitterLogic)
            {
                if (CountExistingOutputsTo(grid, output.Cell) >= 1 ||
                    output.Direction != target.Direction)
                {
                    reason = "the target splitter already has an input or is facing another direction";
                    return false;
                }
            }
            else if (target is MergerLogic && output.Direction == -target.Direction)
            {
                reason = "the target merger cannot accept input from its output side";
                return false;
            }
        }

        reason = null;
        return true;
    }

    private static int CountExistingOutputsTo(GridMap grid, Vector2Int targetCell)
    {
        int count = 0;
        IReadOnlyList<GridBuilding> buildings = grid.Buildings;
        for (int i = 0; i < buildings.Count; i++)
        {
            if (!(buildings[i] is IGridOutputProvider provider))
            {
                continue;
            }

            for (int outputIndex = 0; outputIndex < provider.OutputCount; outputIndex++)
            {
                if (provider.GetOutputCell(outputIndex) == targetCell)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static bool TryGetOnlyIncomingDirection(
        GridMap grid,
        Vector2Int targetCell,
        out Vector2Int direction)
    {
        List<IncomingConnection> incoming = new List<IncomingConnection>(2);
        FindIncoming(grid, targetCell, incoming);
        if (incoming.Count == 1)
        {
            direction = incoming[0].TravelDirection;
            return true;
        }

        direction = Vector2Int.zero;
        return false;
    }

    private static void AddCandidateOutputs(
        BuildingKind kind,
        Vector2Int anchor,
        int quarterTurns,
        Vector2Int direction,
        List<OutputDescriptor> outputs)
    {
        switch (kind)
        {
            case BuildingKind.Belt:
            case BuildingKind.Merger:
                outputs.Add(new OutputDescriptor(anchor + direction, direction));
                break;
            case BuildingKind.Splitter:
                outputs.Add(new OutputDescriptor(anchor + direction, direction));
                Vector2Int left = GridDirection.Rotate(direction, 1);
                Vector2Int right = GridDirection.Rotate(direction, -1);
                outputs.Add(new OutputDescriptor(anchor + left, left));
                outputs.Add(new OutputDescriptor(anchor + right, right));
                break;
            case BuildingKind.Miner:
            case BuildingKind.Furnace:
                outputs.Add(new OutputDescriptor(
                    anchor + GridDirection.Rotate(new Vector2Int(2, 0), quarterTurns),
                    direction));
                break;
        }
    }

    private readonly struct OutputDescriptor
    {
        public OutputDescriptor(Vector2Int cell, Vector2Int direction)
        {
            Cell = cell;
            Direction = direction;
        }

        public Vector2Int Cell { get; }
        public Vector2Int Direction { get; }
    }
}
