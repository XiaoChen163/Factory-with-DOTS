using UnityEngine;

public sealed class SplitterLogic : TransportJunctionLogic
{
    private int nextOutputIndex;

    public override BuildingKind Kind => BuildingKind.Splitter;
    public override int OutputCount => 3;
    public int NextOutputIndex => nextOutputIndex;

    public override Vector2Int GetOutputCell(int index)
    {
        return AnchorCell + GetOutputDirection(index);
    }

    public override Vector2Int GetOutputDirection(int index)
    {
        switch (WrapIndex(index))
        {
            case 0:
                return Direction;
            case 1:
                return GridDirection.Rotate(Direction, 1);
            default:
                return GridDirection.Rotate(Direction, -1);
        }
    }

    protected override bool IsInputDirectionAllowed(Vector2Int travelDirection)
    {
        return travelDirection == Direction;
    }

    protected override int GetInputIndex(Vector2Int travelDirection)
    {
        return travelDirection == Direction ? 0 : -1;
    }

    protected override bool TrySelectOutput(out int outputIndex)
    {
        int firstConnectedIndex = -1;
        for (int offset = 0; offset < OutputCount; offset++)
        {
            int candidateIndex = WrapIndex(nextOutputIndex + offset);
            GridBuilding target = Grid.GetOccupant(GetOutputCell(candidateIndex));
            if (!(target is IItemReceiver receiver))
            {
                continue;
            }

            if (firstConnectedIndex < 0)
            {
                firstConnectedIndex = candidateIndex;
            }

            // Prefer an output that can accept immediately. This preserves the
            // round-robin starting point while skipping back-pressured branches.
            if (target is ITransportNode targetNode)
            {
                if (targetNode.CurrentItem == null &&
                    receiver.CanAccept(CurrentItem, AnchorCell))
                {
                    outputIndex = candidateIndex;
                    return true;
                }
            }
            else if (receiver.CanAccept(CurrentItem, AnchorCell))
            {
                outputIndex = candidateIndex;
                return true;
            }
        }

        // If every connected branch is currently occupied, keep one request alive.
        // The arbiter can still accept it when that branch vacates in the same Tick.
        outputIndex = firstConnectedIndex;
        return outputIndex >= 0;
    }

    protected override void OnOutputTransferred(int outputIndex)
    {
        nextOutputIndex = WrapIndex(outputIndex + 1);
    }

    private static int WrapIndex(int index)
    {
        int wrapped = index % 3;
        return wrapped < 0 ? wrapped + 3 : wrapped;
    }
}
