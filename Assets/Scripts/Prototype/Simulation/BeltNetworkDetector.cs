using System.Collections.Generic;

public sealed class BeltNetworkDetector
{
    private readonly List<BeltLogic> belts = new List<BeltLogic>();
    private readonly List<List<BeltLogic>> loops = new List<List<BeltLogic>>();
    private readonly HashSet<BeltLogic> processed = new HashSet<BeltLogic>();
    private readonly Dictionary<BeltLogic, int> visitedAt = new Dictionary<BeltLogic, int>();
    private readonly List<BeltLogic> path = new List<BeltLogic>();

    public IReadOnlyList<List<BeltLogic>> Loops => loops;

    public void Rebuild(GridMap grid)
    {
        for (int i = 0; i < belts.Count; i++)
        {
            if (belts[i] != null)
            {
                belts[i].IsInLoop = false;
            }
        }

        belts.Clear();
        loops.Clear();
        processed.Clear();

        if (grid == null)
        {
            return;
        }

        IReadOnlyList<GridBuilding> buildings = grid.Buildings;
        for (int i = 0; i < buildings.Count; i++)
        {
            if (buildings[i] is BeltLogic belt)
            {
                belts.Add(belt);
            }
        }

        belts.Sort(CompareBelts);

        for (int i = 0; i < belts.Count; i++)
        {
            BeltLogic start = belts[i];
            if (processed.Contains(start))
            {
                continue;
            }

            visitedAt.Clear();
            path.Clear();
            BeltLogic current = start;

            while (current != null && !processed.Contains(current))
            {
                if (visitedAt.TryGetValue(current, out int loopStart))
                {
                    List<BeltLogic> loop = path.GetRange(loopStart, path.Count - loopStart);
                    for (int loopIndex = 0; loopIndex < loop.Count; loopIndex++)
                    {
                        loop[loopIndex].IsInLoop = true;
                    }

                    loops.Add(loop);
                    break;
                }

                visitedAt[current] = path.Count;
                path.Add(current);
                current = GetConnectedNextBelt(current);
            }

            for (int pathIndex = 0; pathIndex < path.Count; pathIndex++)
            {
                processed.Add(path[pathIndex]);
            }
        }
    }

    private static BeltLogic GetConnectedNextBelt(BeltLogic belt)
    {
        if (!(belt.Grid.GetOccupant(belt.NextCell) is BeltLogic nextBelt) ||
            !nextBelt.TryGetIncomingConnection(
                out TransportTopology.IncomingConnection incoming) ||
            !ReferenceEquals(incoming.Source, belt))
        {
            return null;
        }

        return nextBelt;
    }

    private static int CompareBelts(BeltLogic left, BeltLogic right)
    {
        int x = left.AnchorCell.x.CompareTo(right.AnchorCell.x);
        return x != 0 ? x : left.AnchorCell.y.CompareTo(right.AnchorCell.y);
    }
}
