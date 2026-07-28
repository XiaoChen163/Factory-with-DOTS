using System.Collections.Generic;
using FactoryWithDots.Stage0.Core;
using FactoryWithDots.Stage0.Data;
using FactoryWithDots.Stage0.Presentation;
using UnityEngine;

namespace FactoryWithDots.Stage0.Buildings
{
    public sealed class Storage : PortBuilding, IItemReceiver
    {
        private readonly Dictionary<ItemData, int> itemCounts = new Dictionary<ItemData, int>();

        public override BuildingKind Kind => BuildingKind.Storage;
        public int TotalStored { get; private set; }

        public bool TryAccept(ItemInstance item, Vector2Int sourceCell)
        {
            if (item == null || !IsValidInputSource(sourceCell))
            {
                return false;
            }

            int currentCount;
            itemCounts.TryGetValue(item.Data, out currentCount);
            itemCounts[item.Data] = currentCount + 1;
            TotalStored++;
            item.DestroyVisual();
            return true;
        }

        public int GetCount(ItemData itemData)
        {
            int count;
            return itemData != null && itemCounts.TryGetValue(itemData, out count) ? count : 0;
        }

        protected override void OnInitialized()
        {
            PrototypeVisuals.CreateMachineVisual(transform, Footprint, new Color(0.35f, 0.78f, 0.36f), "S", true, false);
        }
    }
}
