using UnityEngine;

public interface IGridOutputProvider
{
    int OutputCount { get; }
    Vector2Int GetOutputCell(int index);
    Vector2Int GetOutputDirection(int index);
}
