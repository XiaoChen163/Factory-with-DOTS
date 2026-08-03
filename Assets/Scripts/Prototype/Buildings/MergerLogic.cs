using System.Collections.Generic;
using UnityEngine;

public sealed class MergerLogic : TransportJunctionLogic
{
    private int nextInputIndex;

    public override BuildingKind Kind => BuildingKind.Merger;
    public override int OutputCount => 1;
    public int NextInputIndex => nextInputIndex;

    public override Vector2Int GetOutputCell(int index)
    {
        return AnchorCell + Direction;
    }

    public override Vector2Int GetOutputDirection(int index)
    {
        return Direction;
    }

    public ItemTransferRequest SelectIncomingRequest(
        IReadOnlyList<ItemTransferRequest> requests)
    {
        ItemTransferRequest best = null;
        int bestDistance = int.MaxValue;

        for (int i = 0; i < requests.Count; i++)
        {
            ItemTransferRequest request = requests[i];
            if (!TransportTopology.TryGetRequestTravelDirection(request, out Vector2Int travelDirection))
            {
                continue;
            }

            int inputIndex = GetInputIndex(travelDirection);
            if (inputIndex < 0)
            {
                continue;
            }

            int distance = (inputIndex - nextInputIndex + 3) % 3;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = request;
            }
        }

        return best;
    }

    protected override bool IsInputDirectionAllowed(Vector2Int travelDirection)
    {
        return GetInputIndex(travelDirection) >= 0;
    }

    protected override int GetInputIndex(Vector2Int travelDirection)
    {
        if (travelDirection == Direction)
        {
            return 0;
        }

        if (travelDirection == GridDirection.Rotate(Direction, -1))
        {
            return 1;
        }

        if (travelDirection == GridDirection.Rotate(Direction, 1))
        {
            return 2;
        }

        return -1;
    }

    protected override void OnInputAccepted(int inputIndex)
    {
        nextInputIndex = (inputIndex + 1) % 3;
    }
}
