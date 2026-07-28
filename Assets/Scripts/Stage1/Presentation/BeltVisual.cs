using UnityEngine;

public sealed class BeltVisual : MonoBehaviour
{
    private const float ItemHeight = 0.3f;

    private BeltLogic logic;
    private ItemState displayedItem;
    private GameObject itemVisual;
    private bool isTransferAnimating;
    private bool isHoldingAtTransferEnd;
    private Vector3 transferStart;
    private Vector3 transferEnd;
    private float transferDuration;
    private float transferElapsed;

    public void Initialize(BeltLogic beltLogic)
    {
        logic = beltLogic;
    }

    private void LateUpdate()
    {
        if (logic == null || logic.CurrentItem == null)
        {
            ClearItemVisual();
            return;
        }

        if (!ReferenceEquals(displayedItem, logic.CurrentItem))
        {
            ClearItemVisual();
            displayedItem = logic.CurrentItem;
            itemVisual = PrototypeVisuals.CreateItemVisual(displayedItem.Data);
            itemVisual.transform.SetParent(transform, true);
        }

        float alpha = GameManager.Instance == null ? 1f : GameManager.Instance.InterpolationAlpha;
        float progress = logic.GetRenderProgress(alpha);

        if (isTransferAnimating)
        {
            transferElapsed += Time.deltaTime;
            float transferProgress = transferDuration <= 0f
                ? 1f
                : Mathf.Clamp01(transferElapsed / transferDuration);
            itemVisual.transform.position = Vector3.Lerp(transferStart, transferEnd, transferProgress);

            if (transferProgress >= 1f)
            {
                isTransferAnimating = false;
                isHoldingAtTransferEnd = true;
            }

            return;
        }

        // The normal path crosses the target belt's centre at progress 0.5.
        // Hold the accelerated hand-off there until fixed-tick progress catches up,
        // producing a short, intentional stop without any spatial snap.
        if (isHoldingAtTransferEnd)
        {
            if (progress < 0.5f)
            {
                itemVisual.transform.position = transferEnd;
                return;
            }

            isHoldingAtTransferEnd = false;
        }

        Vector3 direction = new Vector3(logic.Direction.x, 0f, logic.Direction.y);
        Vector3 center = logic.Grid.CellToWorld(logic.AnchorCell);
        itemVisual.transform.position =
            center + direction * Mathf.Lerp(-0.5f, 0.5f, progress) + Vector3.up * ItemHeight;
    }

    public GameObject DetachForTransfer(ItemState item, out Vector3 worldPosition)
    {
        worldPosition = GetTransferFallbackPosition();
        if (!ReferenceEquals(displayedItem, item) || itemVisual == null)
        {
            return null;
        }

        GameObject detachedVisual = itemVisual;
        worldPosition = detachedVisual.transform.position;
        displayedItem = null;
        itemVisual = null;
        isTransferAnimating = false;
        isHoldingAtTransferEnd = false;
        return detachedVisual;
    }

    public void BeginIncomingTransfer(
        ItemState item,
        GameObject transferredVisual,
        Vector3 startPosition,
        float speedMultiplier)
    {
        ClearItemVisual();

        displayedItem = item;
        itemVisual = transferredVisual != null
            ? transferredVisual
            : PrototypeVisuals.CreateItemVisual(item.Data);
        itemVisual.transform.SetParent(transform, true);

        transferStart = startPosition;
        transferEnd = logic.Grid.CellToWorld(logic.AnchorCell) + Vector3.up * ItemHeight;
        float worldSpeed = Mathf.Max(0.01f, logic.Speed * Mathf.Max(1f, speedMultiplier));
        transferDuration = Vector3.Distance(transferStart, transferEnd) / worldSpeed;
        transferElapsed = 0f;
        isTransferAnimating = transferDuration > 0.0001f;
        isHoldingAtTransferEnd = !isTransferAnimating;
        itemVisual.transform.position = isTransferAnimating ? transferStart : transferEnd;
    }

    public Vector3 GetTransferFallbackPosition()
    {
        if (logic == null || logic.Grid == null)
        {
            return transform.position + Vector3.up * ItemHeight;
        }

        Vector3 direction = new Vector3(logic.Direction.x, 0f, logic.Direction.y);
        return logic.Grid.CellToWorld(logic.AnchorCell) +
               direction * 0.5f +
               Vector3.up * ItemHeight;
    }

    private void OnDestroy()
    {
        ClearItemVisual();
    }

    private void ClearItemVisual()
    {
        displayedItem = null;
        isTransferAnimating = false;
        isHoldingAtTransferEnd = false;
        if (itemVisual != null)
        {
            Destroy(itemVisual);
            itemVisual = null;
        }
    }
}
