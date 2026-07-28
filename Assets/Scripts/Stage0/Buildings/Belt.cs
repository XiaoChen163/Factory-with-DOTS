using System.Collections.Generic;
using FactoryWithDots.Stage0.Core;
using FactoryWithDots.Stage0.Presentation;
using UnityEngine;

namespace FactoryWithDots.Stage0.Buildings
{
    public sealed class Belt : GridBuilding, IItemReceiver
    {
        private readonly List<ItemInstance> items = new List<ItemInstance>();

        public override BuildingKind Kind => BuildingKind.Belt;
        public float Speed { get; set; } = 1.6f;
        public int ItemCount => items.Count;

        public bool TryAccept(ItemInstance item, Vector2Int sourceCell)
        {
            if (item == null || items.Count >= 1)
            {
                return false;
            }

            item.Progress = 0f;
            if (item.Visual != null)
            {
                item.Visual.transform.SetParent(transform, true);
            }

            items.Add(item);
            UpdateItemPosition(item);
            return true;
        }

        protected override void OnInitialized()
        {
            PrototypeVisuals.CreateBeltVisual(transform);
        }

        protected override void OnRemoved()
        {
            for (int i = 0; i < items.Count; i++)
            {
                items[i].DestroyVisual();
            }

            items.Clear();
        }

        private void Update()
        {
            for (int i = items.Count - 1; i >= 0; i--)
            {
                ItemInstance item = items[i];
                item.Progress = Mathf.Min(item.Progress + Speed * Time.deltaTime, 1f);

                if (item.Progress >= 1f && TryTransfer(item))
                {
                    items.RemoveAt(i);
                    continue;
                }

                UpdateItemPosition(item);
            }
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
}
