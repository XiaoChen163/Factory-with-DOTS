using UnityEngine;

namespace FactoryWithDots.Stage0.Core
{
    public interface IItemReceiver
    {
        bool TryAccept(ItemInstance item, Vector2Int sourceCell);
    }
}
