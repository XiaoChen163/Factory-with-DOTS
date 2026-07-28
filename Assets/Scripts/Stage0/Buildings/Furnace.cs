using UnityEngine;

public sealed class Furnace : PortBuilding, IItemReceiver, IItemTransferSource
{
    private int bufferedInputs;
    private int pendingOutputs;
    private float craftProgress;
    private bool crafting;
    private ItemState pendingOutput;

    public override BuildingKind Kind => BuildingKind.Furnace;
    public RecipeData Recipe { get; set; }
    public int BufferedInputs => bufferedInputs;
    public bool IsCrafting => crafting;
    public bool HasPendingOutput => pendingOutput != null;
    public float CraftProgress => Recipe == null || !crafting ? 0f : Mathf.Clamp01(craftProgress / Recipe.CraftTime);

    public bool CanAccept(ItemState item, Vector2Int sourceCell)
    {
        return Recipe != null &&
               item != null &&
               item.Data == Recipe.InputItem &&
               IsValidInputSource(sourceCell) &&
               bufferedInputs < Recipe.InputCount * 3;
    }

    public void Accept(ItemState item, Vector2Int sourceCell)
    {
        if (CanAccept(item, sourceCell))
        {
            bufferedInputs++;
        }
    }

    protected override void OnInitialized()
    {
        PrototypeVisuals.CreateMachineVisual(transform, Footprint, new Color(0.95f, 0.38f, 0.18f), "F", true, true);
    }

    public void ProcessLogicTick(float deltaTime)
    {
        if (Recipe == null || pendingOutput != null)
        {
            return;
        }

        if (!crafting && bufferedInputs >= Recipe.InputCount)
        {
            bufferedInputs -= Recipe.InputCount;
            crafting = true;
            craftProgress = 0f;
        }

        if (!crafting)
        {
            return;
        }

        craftProgress += deltaTime;
        if (craftProgress < Recipe.CraftTime)
        {
            return;
        }

        crafting = false;
        craftProgress = 0f;
        pendingOutputs = Recipe.OutputCount;
        pendingOutput = new ItemState(Recipe.OutputItem);
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

        pendingOutputs--;
        pendingOutput = pendingOutputs > 0 ? new ItemState(Recipe.OutputItem) : null;
    }
}
