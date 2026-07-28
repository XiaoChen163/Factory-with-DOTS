using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BeltSimulation : MonoBehaviour
{
    private readonly List<BeltLogic> belts = new List<BeltLogic>();
    private readonly List<Miner> miners = new List<Miner>();
    private readonly List<Furnace> furnaces = new List<Furnace>();
    private readonly List<ItemTransferRequest> requests = new List<ItemTransferRequest>();
    private readonly TransferArbiter arbiter = new TransferArbiter();

    private GameManager gameManager;
    private GridMap grid;

    public int BeltCount => belts.Count;
    public int LastRequestCount { get; private set; }
    public int LastAcceptedCount { get; private set; }
    public event Action<IReadOnlyList<ItemTransferRequest>> TransfersCommitted;

    public void Initialize(GameManager tickManager, GridMap targetGrid)
    {
        if (gameManager != null)
        {
            gameManager.LogicTick -= ProcessTick;
        }

        gameManager = tickManager;
        grid = targetGrid;

        if (gameManager != null)
        {
            gameManager.LogicTick += ProcessTick;
        }
    }

    private void OnDestroy()
    {
        if (gameManager != null)
        {
            gameManager.LogicTick -= ProcessTick;
        }
    }

    private void ProcessTick(float deltaTime)
    {
        if (grid == null)
        {
            return;
        }

        CaptureBuildingSnapshot();

        for (int i = 0; i < belts.Count; i++)
        {
            belts[i].PrepareNextState();
        }

        for (int i = 0; i < miners.Count; i++)
        {
            miners[i].ProcessLogicTick(deltaTime);
        }

        for (int i = 0; i < furnaces.Count; i++)
        {
            furnaces[i].ProcessLogicTick(deltaTime);
        }

        requests.Clear();
        for (int i = 0; i < belts.Count; i++)
        {
            BeltLogic belt = belts[i];
            belt.Advance(deltaTime);
            AddRequestFrom(belt);
        }

        for (int i = 0; i < miners.Count; i++)
        {
            AddRequestFrom(miners[i]);
        }

        for (int i = 0; i < furnaces.Count; i++)
        {
            AddRequestFrom(furnaces[i]);
        }

        IReadOnlyList<ItemTransferRequest> accepted = arbiter.Resolve(requests);
        LastRequestCount = requests.Count;
        LastAcceptedCount = accepted.Count;

        // Phase one clears every accepted source before phase two fills targets.
        // This makes A -> B -> C independent of iteration order.
        for (int i = 0; i < accepted.Count; i++)
        {
            ItemTransferRequest request = accepted[i];
            request.Source.StageTransferOut(request.Item);
        }

        for (int i = 0; i < accepted.Count; i++)
        {
            ItemTransferRequest request = accepted[i];
            if (request.TargetBuilding is BeltLogic targetBelt)
            {
                targetBelt.StageTransferIn(request.Item);
            }
            else if (request.TargetBuilding is IItemReceiver receiver)
            {
                receiver.Accept(request.Item, request.SourceCell);
            }
        }

        for (int i = 0; i < belts.Count; i++)
        {
            belts[i].CommitNextState();
        }

        if (accepted.Count > 0)
        {
            TransfersCommitted?.Invoke(accepted);
        }
    }

    private void CaptureBuildingSnapshot()
    {
        belts.Clear();
        miners.Clear();
        furnaces.Clear();

        IReadOnlyList<GridBuilding> buildings = grid.Buildings;
        for (int i = 0; i < buildings.Count; i++)
        {
            GridBuilding building = buildings[i];
            if (building is BeltLogic belt)
            {
                belts.Add(belt);
            }
            else if (building is Miner miner)
            {
                miners.Add(miner);
            }
            else if (building is Furnace furnace)
            {
                furnaces.Add(furnace);
            }
        }

        belts.Sort(CompareBuildings);
        miners.Sort(CompareBuildings);
        furnaces.Sort(CompareBuildings);
    }

    private void AddRequestFrom(IItemTransferSource source)
    {
        if (source.TryCreateTransferRequest(out ItemTransferRequest request))
        {
            requests.Add(request);
        }
    }

    private static int CompareBuildings(GridBuilding left, GridBuilding right)
    {
        int x = left.AnchorCell.x.CompareTo(right.AnchorCell.x);
        return x != 0 ? x : left.AnchorCell.y.CompareTo(right.AnchorCell.y);
    }
}
