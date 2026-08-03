using UnityEngine;

public interface IItemReceiver
{
    bool CanAccept(ItemState item, Vector2Int sourceCell);
    void Accept(ItemState item, Vector2Int sourceCell);
}
