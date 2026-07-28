using UnityEngine;

public sealed class BeltLogic : GridBuilding, IItemReceiver, IItemTransferSource
{
    [SerializeField, Min(0f)] private float speed = 2f;

    private ItemState currentItem;
    private float progress;
    private float previousProgress;

    [System.NonSerialized] private ItemState nextItem;
    [System.NonSerialized] private float nextProgress;

    public override BuildingKind Kind => BuildingKind.Belt;
    public float Speed
    {
        get => speed;
        set => speed = Mathf.Max(0f, value);
    }

    public ItemState CurrentItem => currentItem;
    public float Progress => progress;
    public float PreviousProgress => previousProgress;
    public Vector2Int NextCell => AnchorCell + Direction;
    public int ItemCount => currentItem == null ? 0 : 1;
    public BeltVisual Visual { get; private set; }

    public bool CanAccept(ItemState item, Vector2Int sourceCell)
    {
        return item != null && currentItem == null;
    }

    public void Accept(ItemState item, Vector2Int sourceCell)
    {
        StageTransferIn(item);
    }

    protected override void OnInitialized()
    {
        PrototypeVisuals.CreateBeltVisual(transform);
        Visual = gameObject.AddComponent<BeltVisual>();
        Visual.Initialize(this);
    }

    protected override void OnRemoved()
    {
        currentItem = null;
        nextItem = null;
    }

    public void PrepareNextState()
    {
        previousProgress = progress;
        nextItem = currentItem;
        nextProgress = progress;
    }

    public void Advance(float deltaTime)
    {
        if (currentItem == null)
        {
            nextProgress = 0f;
            return;
        }

        nextProgress = Mathf.Min(progress + speed * deltaTime, 1f);
    }

    public bool TryCreateTransferRequest(out ItemTransferRequest request)
    {
        if (currentItem == null || nextProgress < 1f)
        {
            request = null;
            return false;
        }

        request = new ItemTransferRequest(
            this,
            currentItem,
            AnchorCell,
            NextCell,
            Grid.GetOccupant(NextCell));
        return true;
    }

    public void StageTransferOut(ItemState item)
    {
        if (!ReferenceEquals(item, currentItem))
        {
            return;
        }

        nextItem = null;
        nextProgress = 0f;
    }

    public void StageTransferIn(ItemState item)
    {
        if (item == null)
        {
            return;
        }

        nextItem = item;
        nextProgress = 0f;
    }

    public void CommitNextState()
    {
        currentItem = nextItem;
        progress = currentItem == null ? 0f : nextProgress;
    }

    public float GetRenderProgress(float interpolationAlpha)
    {
        if (currentItem == null)
        {
            return 0f;
        }

        if (previousProgress > progress)
        {
            return progress;
        }

        return Mathf.Lerp(previousProgress, progress, interpolationAlpha);
    }
}
