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
    private readonly Dictionary<GridBuilding, List<ItemTransferRequest>> requestsByTarget =
        new Dictionary<GridBuilding, List<ItemTransferRequest>>();
    private readonly Dictionary<GridBuilding, ItemTransferRequest> candidateByTarget =
        new Dictionary<GridBuilding, ItemTransferRequest>();
    private readonly Dictionary<ITransportNode, ItemTransferRequest> outgoingByNode =
        new Dictionary<ITransportNode, ItemTransferRequest>();
    private readonly Dictionary<ItemTransferRequest, ResolutionState> states =
        new Dictionary<ItemTransferRequest, ResolutionState>();
    private readonly HashSet<IItemTransferSource> acceptedSources =
        new HashSet<IItemTransferSource>();
    private readonly HashSet<GridBuilding> reservedTargets =
        new HashSet<GridBuilding>();

    public IReadOnlyList<ItemTransferRequest> Resolve(
        IReadOnlyList<ItemTransferRequest> requests,
        IReadOnlyList<List<BeltLogic>> loops)
    {
        sortedRequests.Clear();
        acceptedRequests.Clear();
        requestsByTarget.Clear();
        candidateByTarget.Clear();
        outgoingByNode.Clear();
        states.Clear();
        acceptedSources.Clear();
        reservedTargets.Clear();

        for (int i = 0; i < requests.Count; i++)
        {
            ItemTransferRequest request = requests[i];
            sortedRequests.Add(request);
            if (request.Source is ITransportNode sourceNode)
            {
                outgoingByNode[sourceNode] = request;
            }
        }

        sortedRequests.Sort(CompareRequests);
        AcceptReadyFullLoops(loops);

        for (int i = 0; i < sortedRequests.Count; i++)
        {
            ItemTransferRequest request = sortedRequests[i];
            if (request.TargetBuilding == null ||
                !(request.TargetBuilding is IItemReceiver receiver) ||
                reservedTargets.Contains(request.TargetBuilding) ||
                acceptedSources.Contains(request.Source))
            {
                continue;
            }

            if (request.TargetBuilding is BeltLogic targetBelt &&
                request.Source is GridBuilding sourceBuilding &&
                !targetBelt.AcceptsInputFrom(sourceBuilding))
            {
                continue;
            }

            if (!requestsByTarget.TryGetValue(
                    request.TargetBuilding,
                    out List<ItemTransferRequest> targetRequests))
            {
                targetRequests = new List<ItemTransferRequest>(3);
                requestsByTarget.Add(request.TargetBuilding, targetRequests);
            }

            targetRequests.Add(request);
        }

        foreach (KeyValuePair<GridBuilding, List<ItemTransferRequest>> pair in requestsByTarget)
        {
            List<ItemTransferRequest> targetRequests = pair.Value;
            ItemTransferRequest candidate = pair.Key is MergerLogic merger
                ? merger.SelectIncomingRequest(targetRequests)
                : targetRequests[0];

            if (candidate == null)
            {
                continue;
            }

            IItemReceiver receiver = (IItemReceiver)pair.Key;
            if (pair.Key is ITransportNode targetNode)
            {
                if (targetNode.CurrentItem == null &&
                    !receiver.CanAccept(candidate.Item, candidate.SourceCell))
                {
                    continue;
                }
            }
            else if (!receiver.CanAccept(candidate.Item, candidate.SourceCell))
            {
                continue;
            }

            candidateByTarget.Add(pair.Key, candidate);
        }

        foreach (ItemTransferRequest request in candidateByTarget.Values)
        {
            if (!acceptedSources.Contains(request.Source) && CanAccept(request))
            {
                acceptedRequests.Add(request);
                acceptedSources.Add(request.Source);
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
        if (request.TargetBuilding is ITransportNode targetNode)
        {
            accepted = targetNode.CurrentItem == null;
            if (!accepted &&
                outgoingByNode.TryGetValue(targetNode, out ItemTransferRequest outgoingRequest) &&
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

    private void AcceptReadyFullLoops(IReadOnlyList<List<BeltLogic>> loops)
    {
        if (loops == null)
        {
            return;
        }

        for (int loopIndex = 0; loopIndex < loops.Count; loopIndex++)
        {
            List<BeltLogic> loop = loops[loopIndex];
            bool ready = loop.Count > 1;

            for (int i = 0; i < loop.Count && ready; i++)
            {
                BeltLogic source = loop[i];
                if (!outgoingByNode.TryGetValue(source, out ItemTransferRequest request) ||
                    !(request.TargetBuilding is BeltLogic target) ||
                    !target.IsInLoop ||
                    !loop.Contains(target))
                {
                    ready = false;
                }
            }

            if (!ready)
            {
                continue;
            }

            for (int i = 0; i < loop.Count; i++)
            {
                ItemTransferRequest request = outgoingByNode[loop[i]];
                acceptedRequests.Add(request);
                acceptedSources.Add(request.Source);
                reservedTargets.Add(request.TargetBuilding);
                states[request] = ResolutionState.Accepted;
            }
        }
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
