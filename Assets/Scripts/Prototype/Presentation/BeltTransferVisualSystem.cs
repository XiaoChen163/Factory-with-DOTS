using System.Collections.Generic;
using UnityEngine;

public sealed class BeltTransferVisualSystem : MonoBehaviour
{
    private struct DetachedVisual
    {
        public GameObject GameObject;
        public Vector3 StartPosition;
    }

    [SerializeField, Range(1f, 2f)] private float transferSpeedMultiplier = 1.15f;

    private readonly Dictionary<ItemState, DetachedVisual> detachedVisuals =
        new Dictionary<ItemState, DetachedVisual>();
    private BeltSimulation simulation;

    public float TransferSpeedMultiplier
    {
        get => transferSpeedMultiplier;
        set => transferSpeedMultiplier = Mathf.Clamp(value, 1f, 2f);
    }

    public void Initialize(BeltSimulation beltSimulation)
    {
        if (simulation != null)
        {
            simulation.TransfersCommitted -= HandleTransfersCommitted;
        }

        simulation = beltSimulation;
        if (simulation != null)
        {
            simulation.TransfersCommitted += HandleTransfersCommitted;
        }
    }

    private void OnDestroy()
    {
        if (simulation != null)
        {
            simulation.TransfersCommitted -= HandleTransfersCommitted;
        }
    }

    private void HandleTransfersCommitted(IReadOnlyList<ItemTransferRequest> transfers)
    {
        detachedVisuals.Clear();

        // Detach every source first so simultaneous A -> B -> C transfers do not
        // overwrite B's outgoing visual before it can be handed to C.
        for (int i = 0; i < transfers.Count; i++)
        {
            ItemTransferRequest request = transfers[i];
            if (!(request.Source is BeltLogic sourceBelt) ||
                !(request.TargetBuilding is BeltLogic))
            {
                continue;
            }

            Vector3 startPosition;
            GameObject itemVisual;
            if (sourceBelt.Visual != null)
            {
                itemVisual = sourceBelt.Visual.DetachForTransfer(
                    request.Item,
                    out startPosition);
            }
            else
            {
                itemVisual = null;
                Vector3 direction = new Vector3(
                    sourceBelt.Direction.x,
                    0f,
                    sourceBelt.Direction.y);
                startPosition = sourceBelt.Grid.CellToWorld(sourceBelt.AnchorCell) +
                                direction * 0.5f +
                                Vector3.up * 0.3f;
            }

            detachedVisuals[request.Item] = new DetachedVisual
            {
                GameObject = itemVisual,
                StartPosition = startPosition
            };
        }

        for (int i = 0; i < transfers.Count; i++)
        {
            ItemTransferRequest request = transfers[i];
            if (!(request.Source is BeltLogic) ||
                !(request.TargetBuilding is BeltLogic targetBelt) ||
                targetBelt.Visual == null ||
                !detachedVisuals.TryGetValue(request.Item, out DetachedVisual detached))
            {
                continue;
            }

            targetBelt.Visual.BeginIncomingTransfer(
                request.Item,
                detached.GameObject,
                detached.StartPosition,
                transferSpeedMultiplier);
        }
    }

    private void OnValidate()
    {
        transferSpeedMultiplier = Mathf.Clamp(transferSpeedMultiplier, 1f, 2f);
    }
}
