using FactoryWithDots.Stage0.Core;
using UnityEngine;

namespace FactoryWithDots.Stage0.Buildings
{
    public abstract class PortBuilding : GridBuilding
    {
        public override Vector2Int Footprint => new Vector2Int(2, 2);
        public Vector2Int InputPortCell => RotateLocalCell(new Vector2Int(-1, 0));
        public Vector2Int OutputPortCell => RotateLocalCell(new Vector2Int(2, 0));

        protected bool IsValidInputSource(Vector2Int sourceCell)
        {
            return sourceCell == InputPortCell;
        }

        protected bool TrySend(ItemInstance item)
        {
            GridBuilding outputBuilding = Grid.GetOccupant(OutputPortCell);
            IItemReceiver receiver = outputBuilding as IItemReceiver;
            return receiver != null && receiver.TryAccept(item, AnchorCell);
        }
    }
}
