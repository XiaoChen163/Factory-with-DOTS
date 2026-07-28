using System.Collections.Generic;

public sealed class TransferArbiter
{
    private enum ResolutionState
    {
        Resolving,
        Accepted,
        Rejected
    }

    private readonly List<ItemTransferRequest> sortedRequests = new List<ItemTransferRequest>();
    private readonly List<ItemTransferRequest> acceptedRequests = new List<ItemTransferRequest>();
    private readonly Dictionary<GridBuilding, ItemTransferRequest> candidateByTarget =
        new Dictionary<GridBuilding, ItemTransferRequest>();
    private readonly Dictionary<BeltLogic, ItemTransferRequest> outgoingByBelt =
        new Dictionary<BeltLogic, ItemTransferRequest>();
    private readonly Dictionary<ItemTransferRequest, ResolutionState> states =
        new Dictionary<ItemTransferRequest, ResolutionState>();

    public IReadOnlyList<ItemTransferRequest> Resolve(IReadOnlyList<ItemTransferRequest> requests)
    {
        sortedRequests.Clear();
        acceptedRequests.Clear();
        candidateByTarget.Clear();
        outgoingByBelt.Clear();
        states.Clear();

        for (int i = 0; i < requests.Count; i++)
        {
            ItemTransferRequest request = requests[i];
            sortedRequests.Add(request);
            if (request.Source is BeltLogic sourceBelt)
            {
                outgoingByBelt[sourceBelt] = request;
            }
        }

        sortedRequests.Sort(CompareRequests);

        for (int i = 0; i < sortedRequests.Count; i++)
        {
            ItemTransferRequest request = sortedRequests[i];
            if (request.TargetBuilding == null ||
                !(request.TargetBuilding is IItemReceiver receiver) ||
                candidateByTarget.ContainsKey(request.TargetBuilding))
            {
                continue;
            }

            if (!(request.TargetBuilding is BeltLogic) &&
                !receiver.CanAccept(request.Item, request.SourceCell))
            {
                continue;
            }

            candidateByTarget.Add(request.TargetBuilding, request);
        }

        foreach (ItemTransferRequest request in candidateByTarget.Values)
        {
            if (CanAccept(request))
            {
                acceptedRequests.Add(request);
            }
        }

        acceptedRequests.Sort(CompareRequests);
        return acceptedRequests;
    }

    private bool CanAccept(ItemTransferRequest request)
    {
        if (states.TryGetValue(request, out ResolutionState existingState))
        {
            return existingState == ResolutionState.Accepted;
        }

        states[request] = ResolutionState.Resolving;

        bool accepted;
        if (request.TargetBuilding is BeltLogic targetBelt)
        {
            accepted = targetBelt.CurrentItem == null;
            if (!accepted &&
                outgoingByBelt.TryGetValue(targetBelt, out ItemTransferRequest outgoingRequest) &&
                outgoingRequest.TargetBuilding != null &&
                candidateByTarget.TryGetValue(outgoingRequest.TargetBuilding, out ItemTransferRequest candidate) &&
                ReferenceEquals(candidate, outgoingRequest))
            {
                accepted = CanAccept(outgoingRequest);
            }
        }
        else if (request.TargetBuilding is IItemReceiver receiver)
        {
            accepted = receiver.CanAccept(request.Item, request.SourceCell);
        }
        else
        {
            accepted = false;
        }

        states[request] = accepted ? ResolutionState.Accepted : ResolutionState.Rejected;
        return accepted;
    }

    private static int CompareRequests(ItemTransferRequest left, ItemTransferRequest right)
    {
        int targetX = left.TargetCell.x.CompareTo(right.TargetCell.x);
        if (targetX != 0)
        {
            return targetX;
        }

        int targetY = left.TargetCell.y.CompareTo(right.TargetCell.y);
        if (targetY != 0)
        {
            return targetY;
        }

        int sourceX = left.SourceCell.x.CompareTo(right.SourceCell.x);
        return sourceX != 0 ? sourceX : left.SourceCell.y.CompareTo(right.SourceCell.y);
    }
}
