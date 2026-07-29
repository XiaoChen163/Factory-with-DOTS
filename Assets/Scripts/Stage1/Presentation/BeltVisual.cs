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
    private Transform directionTriangle;
    private GameObject eastEdge;
    private GameObject northEdge;
    private GameObject westEdge;
    private GameObject southEdge;

    public void Initialize(BeltLogic beltLogic)
    {
        logic = beltLogic;
        directionTriangle = transform.Find("Direction Triangle");
        eastEdge = FindChildObject("Connection Edge East");
        northEdge = FindChildObject("Connection Edge North");
        westEdge = FindChildObject("Connection Edge West");
        southEdge = FindChildObject("Connection Edge South");
    }

    public void RefreshTopology(
        bool hasInput,
        Vector2Int incomingTravelDirection,
        bool hasOutput)
    {
        SetAllConnectionEdgesVisible(true);

        if (hasInput)
        {
            SetWorldEdgeVisible(-incomingTravelDirection, false);
        }

        if (hasOutput)
        {
            SetWorldEdgeVisible(logic.Direction, false);
        }

        if (directionTriangle == null)
        {
            return;
        }

        Vector2Int displayDirection = logic.Direction;
        if (hasInput && incomingTravelDirection != logic.Direction)
        {
            Vector2Int cornerDirection = incomingTravelDirection + logic.Direction;
            if (cornerDirection != Vector2Int.zero)
            {
                displayDirection = cornerDirection;
            }
        }

        Vector2Int localDirection =
            GridDirection.Rotate(displayDirection, -logic.QuarterTurns);
        float angle = -Mathf.Atan2(localDirection.y, localDirection.x) * Mathf.Rad2Deg;
        directionTriangle.localRotation = Quaternion.Euler(0f, angle, 0f);
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

    private GameObject FindChildObject(string childName)
    {
        Transform child = transform.Find(childName);
        return child == null ? null : child.gameObject;
    }

    private void SetAllConnectionEdgesVisible(bool visible)
    {
        if (eastEdge != null) eastEdge.SetActive(visible);
        if (northEdge != null) northEdge.SetActive(visible);
        if (westEdge != null) westEdge.SetActive(visible);
        if (southEdge != null) southEdge.SetActive(visible);
    }

    private void SetWorldEdgeVisible(Vector2Int worldDirection, bool visible)
    {
        Vector2Int localDirection =
            GridDirection.Rotate(worldDirection, -logic.QuarterTurns);

        if (localDirection == Vector2Int.right && eastEdge != null)
        {
            eastEdge.SetActive(visible);
        }
        else if (localDirection == Vector2Int.up && northEdge != null)
        {
            northEdge.SetActive(visible);
        }
        else if (localDirection == Vector2Int.left && westEdge != null)
        {
            westEdge.SetActive(visible);
        }
        else if (localDirection == Vector2Int.down && southEdge != null)
        {
            southEdge.SetActive(visible);
        }
    }
}
