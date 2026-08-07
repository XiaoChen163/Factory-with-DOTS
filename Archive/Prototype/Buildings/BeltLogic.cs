using System.Collections.Generic;
using UnityEngine;

public sealed class BeltLogic : GridBuilding, ITransportNode
{
    [SerializeField, Min(0f)] private float speed = 1f;

    private readonly List<TransportTopology.IncomingConnection> incomingConnections =
        new List<TransportTopology.IncomingConnection>(2);

    private GridBuilding selectedInputSource;
    private int selectedInputOutputIndex = -1;

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
    public int OutputCount => 1;
    public bool IsInLoop { get; internal set; }
    public BeltVisual Visual { get; private set; }

    public bool CanAccept(ItemState item, Vector2Int sourceCell)
    {
        return item != null &&
               currentItem == null &&
               TryGetIncomingConnection(out TransportTopology.IncomingConnection connection) &&
               connection.Source.AnchorCell == sourceCell;
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
        Grid.Changed += HandleGridChanged;
        RefreshTopologyVisual();
    }

    protected override void OnRemoved()
    {
        if (Grid != null)
        {
            Grid.Changed -= HandleGridChanged;
        }

        currentItem = null;
        nextItem = null;
        IsInLoop = false;
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

    public Vector2Int GetOutputCell(int index)
    {
        return NextCell;
    }

    public Vector2Int GetOutputDirection(int index)
    {
        return Direction;
    }

    public bool TryGetIncomingConnection(
        out TransportTopology.IncomingConnection connection)
    {
        TransportTopology.FindIncoming(Grid, AnchorCell, incomingConnections);
        if (selectedInputSource != null)
        {
            for (int i = 0; i < incomingConnections.Count; i++)
            {
                TransportTopology.IncomingConnection candidate = incomingConnections[i];
                if (ReferenceEquals(candidate.Source, selectedInputSource) &&
                    candidate.OutputIndex == selectedInputOutputIndex)
                {
                    connection = candidate;
                    return true;
                }
            }
        }

        selectedInputSource = null;
        selectedInputOutputIndex = -1;
        if (incomingConnections.Count > 0)
        {
            connection = incomingConnections[0];
            selectedInputSource = connection.Source;
            selectedInputOutputIndex = connection.OutputIndex;
            return true;
        }

        connection = default;
        return false;
    }

    public bool AcceptsInputFrom(GridBuilding source)
    {
        return source != null &&
               TryGetIncomingConnection(out TransportTopology.IncomingConnection connection) &&
               ReferenceEquals(connection.Source, source);
    }

    internal void RefreshTopologyVisual()
    {
        if (Visual == null || Grid == null)
        {
            return;
        }

        bool hasInput = TryGetIncomingConnection(
            out TransportTopology.IncomingConnection incoming);
        GridBuilding outputTarget = Grid.GetOccupant(NextCell);
        bool hasOutput = outputTarget is IItemReceiver &&
                         (!(outputTarget is BeltLogic targetBelt) ||
                          targetBelt.AcceptsInputFrom(this));
        Visual.RefreshTopology(
            hasInput,
            hasInput ? incoming.TravelDirection : Direction,
            hasOutput);
    }

    internal void SetItemForPrototype(ItemState item, float itemProgress = 0f)
    {
        currentItem = item;
        progress = item == null ? 0f : Mathf.Clamp01(itemProgress);
        previousProgress = progress;
        nextItem = currentItem;
        nextProgress = progress;
    }

    private void HandleGridChanged()
    {
        RefreshTopologyVisual();
    }
}
