using System.Collections.Generic;
using UnityEngine;

public abstract class TransportJunctionLogic : GridBuilding, ITransportNode
{
    [SerializeField, Min(0f)] private float speed = 1f;

    private readonly List<TransportTopology.IncomingConnection> incomingConnections =
        new List<TransportTopology.IncomingConnection>(4);

    private ItemState currentItem;
    private float progress;
    private float previousProgress;
    private ItemState nextItem;
    private float nextProgress;
    private int selectedOutputIndex = -1;

    public float Speed
    {
        get => speed;
        set => speed = Mathf.Max(0f, value);
    }

    public ItemState CurrentItem => currentItem;
    public float Progress => progress;
    public float PreviousProgress => previousProgress;
    public int ItemCount => currentItem == null ? 0 : 1;
    public abstract int OutputCount { get; }

    public abstract Vector2Int GetOutputCell(int index);
    public abstract Vector2Int GetOutputDirection(int index);

    public bool CanAccept(ItemState item, Vector2Int sourceCell)
    {
        return item != null &&
               currentItem == null &&
               IsValidInputSource(sourceCell, out _);
    }

    public void Accept(ItemState item, Vector2Int sourceCell)
    {
        if (item == null || !IsValidInputSource(sourceCell, out int inputIndex))
        {
            return;
        }

        StageTransferIn(item);
        OnInputAccepted(inputIndex);
    }

    public void PrepareNextState()
    {
        previousProgress = progress;
        nextItem = currentItem;
        nextProgress = progress;
        selectedOutputIndex = -1;
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
        if (currentItem == null ||
            nextProgress < 1f ||
            !TrySelectOutput(out selectedOutputIndex))
        {
            request = null;
            return false;
        }

        Vector2Int targetCell = GetOutputCell(selectedOutputIndex);
        request = new ItemTransferRequest(
            this,
            currentItem,
            AnchorCell,
            targetCell,
            Grid.GetOccupant(targetCell));
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
        if (selectedOutputIndex >= 0)
        {
            OnOutputTransferred(selectedOutputIndex);
        }
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

    protected override void OnInitialized()
    {
        PrototypeVisuals.CreateJunctionVisual(
            transform,
            this is MergerLogic ? "3→1" : "1→3",
            this is MergerLogic
                ? new Color(0.25f, 0.55f, 0.95f)
                : new Color(0.72f, 0.35f, 0.92f));
    }

    protected override void OnRemoved()
    {
        currentItem = null;
        nextItem = null;
    }

    protected virtual void OnInputAccepted(int inputIndex)
    {
    }

    protected virtual void OnOutputTransferred(int outputIndex)
    {
    }

    protected abstract bool IsInputDirectionAllowed(Vector2Int travelDirection);

    protected virtual bool TrySelectOutput(out int outputIndex)
    {
        outputIndex = 0;
        return OutputCount > 0;
    }

    protected bool IsValidInputSource(Vector2Int sourceCell, out int inputIndex)
    {
        inputIndex = -1;
        TransportTopology.FindIncoming(Grid, AnchorCell, incomingConnections);
        for (int i = 0; i < incomingConnections.Count; i++)
        {
            TransportTopology.IncomingConnection connection = incomingConnections[i];
            if (connection.Source.AnchorCell != sourceCell ||
                !IsInputDirectionAllowed(connection.TravelDirection))
            {
                continue;
            }

            inputIndex = GetInputIndex(connection.TravelDirection);
            return inputIndex >= 0;
        }

        return false;
    }

    protected abstract int GetInputIndex(Vector2Int travelDirection);
}
