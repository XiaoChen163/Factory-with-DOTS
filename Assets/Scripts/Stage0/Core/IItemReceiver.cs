using UnityEngine;

public interface IItemReceiver
{
    bool TryAccept(ItemInstance item, Vector2Int sourceCell);
}
