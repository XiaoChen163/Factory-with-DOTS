using UnityEngine;

public sealed class Miner : PortBuilding, IItemTransferSource, IGridOutputProvider
{
    private float productionTimer;
    private ItemState pendingOutput;

    public override BuildingKind Kind => BuildingKind.Miner;
    public ItemData OutputItem { get; set; }
    public float ProductionInterval { get; set; } = 1f;
    public bool HasPendingOutput => pendingOutput != null;
    public int OutputCount => 1;

    protected override void OnInitialized()
    {
        PrototypeVisuals.CreateMachineVisual(transform, Footprint, new Color(0.24f, 0.62f, 0.95f), "M", false, true);
    }

    public void ProcessLogicTick(float deltaTime)
    {
        if (OutputItem == null || pendingOutput != null)
        {
            return;
        }

        productionTimer += deltaTime;
        if (productionTimer >= ProductionInterval)
        {
            pendingOutput = new ItemState(OutputItem);
        }
    }

    public bool TryCreateTransferRequest(out ItemTransferRequest request)
    {
        if (pendingOutput == null)
        {
            request = null;
            return false;
        }

        request = new ItemTransferRequest(
            this,
            pendingOutput,
            AnchorCell,
            OutputPortCell,
            Grid.GetOccupant(OutputPortCell));
        return true;
    }

    public void StageTransferOut(ItemState item)
    {
        if (!ReferenceEquals(item, pendingOutput))
        {
            return;
        }

        pendingOutput = null;
        productionTimer = Mathf.Max(0f, productionTimer - ProductionInterval);
    }

    public Vector2Int GetOutputCell(int index)
    {
        return OutputPortCell;
    }

    public Vector2Int GetOutputDirection(int index)
    {
        return Direction;
    }
}
