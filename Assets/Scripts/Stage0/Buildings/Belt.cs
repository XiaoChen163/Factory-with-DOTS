using UnityEngine;

public sealed class Belt : GridBuilding, IItemReceiver
{
    private ItemInstance currentItem;

    public override BuildingKind Kind => BuildingKind.Belt;
    public float Speed { get; set; } = 1.6f;
    public int ItemCount => currentItem == null ? 0 : 1;

    public bool TryAccept(ItemInstance item, Vector2Int sourceCell)
    {
        if (item == null || currentItem != null)
        {
            return false;
        }

        currentItem = item;
        currentItem.Progress = 0f;
        if (currentItem.Visual != null)
        {
            currentItem.Visual.transform.SetParent(transform, true);
        }

        UpdateItemPosition(currentItem);
        return true;
    }

    protected override void OnInitialized()
    {
        PrototypeVisuals.CreateBeltVisual(transform);
    }

    protected override void OnRemoved()
    {
        if (currentItem != null)
        {
            currentItem.DestroyVisual();
            currentItem = null;
        }
    }

    private void Update()
    {
        if (currentItem == null)
        {
            return;
        }

        currentItem.Progress = Mathf.Min(currentItem.Progress + Speed * Time.deltaTime, 1f);
        if (currentItem.Progress >= 1f && TryTransfer(currentItem))
        {
            currentItem = null;
            return;
        }

        UpdateItemPosition(currentItem);
    }

    private bool TryTransfer(ItemInstance item)
    {
        Vector2Int nextCell = AnchorCell + Direction;
        GridBuilding nextBuilding = Grid.GetOccupant(nextCell);
        IItemReceiver receiver = nextBuilding as IItemReceiver;
        return receiver != null && receiver.TryAccept(item, AnchorCell);
    }

    private void UpdateItemPosition(ItemInstance item)
    {
        if (item.Visual == null)
        {
            return;
        }

        Vector3 direction = new Vector3(Direction.x, 0f, Direction.y);
        Vector3 center = Grid.CellToWorld(AnchorCell);
        item.Visual.transform.position = center + direction * Mathf.Lerp(-0.38f, 0.38f, item.Progress) + Vector3.up * 0.28f;
    }
}
