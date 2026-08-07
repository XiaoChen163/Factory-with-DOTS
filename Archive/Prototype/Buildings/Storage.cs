using System.Collections.Generic;
using UnityEngine;

public sealed class Storage : PortBuilding, IItemReceiver
{
    private readonly Dictionary<ItemData, int> itemCounts = new Dictionary<ItemData, int>();

    public override BuildingKind Kind => BuildingKind.Storage;
    public int TotalStored { get; private set; }

    public bool CanAccept(ItemState item, Vector2Int sourceCell)
    {
        return item != null && IsValidInputSource(sourceCell);
    }

    public void Accept(ItemState item, Vector2Int sourceCell)
    {
        if (!CanAccept(item, sourceCell))
        {
            return;
        }

        itemCounts.TryGetValue(item.Data, out int currentCount);
        itemCounts[item.Data] = currentCount + 1;
        TotalStored++;
    }

    public int GetCount(ItemData itemData)
    {
        return itemData != null && itemCounts.TryGetValue(itemData, out int count) ? count : 0;
    }

    protected override void OnInitialized()
    {
        PrototypeVisuals.CreateMachineVisual(transform, Footprint, new Color(0.35f, 0.78f, 0.36f), "S", true, false);
    }
}
