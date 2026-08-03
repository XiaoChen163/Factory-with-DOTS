using UnityEngine;

public sealed class ItemTransferRequest
{
    public ItemTransferRequest(
        IItemTransferSource source,
        ItemState item,
        Vector2Int sourceCell,
        Vector2Int targetCell,
        GridBuilding targetBuilding)
    {
        Source = source;
        Item = item;
        SourceCell = sourceCell;
        TargetCell = targetCell;
        TargetBuilding = targetBuilding;
    }

    public IItemTransferSource Source { get; }
    public ItemState Item { get; }
    public Vector2Int SourceCell { get; }
    public Vector2Int TargetCell { get; }
    public GridBuilding TargetBuilding { get; }
}
