using System.Collections.Generic;
using UnityEngine;

public abstract class GridBuilding : MonoBehaviour
{
    private readonly List<Vector2Int> occupiedCells = new List<Vector2Int>();

    public GridMap Grid { get; private set; }
    public Vector2Int AnchorCell { get; private set; }
    public int QuarterTurns { get; private set; }
    public abstract BuildingKind Kind { get; }
    public virtual Vector2Int Footprint => Vector2Int.one;
    public IReadOnlyList<Vector2Int> OccupiedCells => occupiedCells;
    public Vector2Int Direction => GridDirection.FromQuarterTurns(QuarterTurns);

    public bool Initialize(GridMap grid, Vector2Int anchorCell, int quarterTurns)
    {
        Grid = grid;
        AnchorCell = anchorCell;
        QuarterTurns = GridDirection.Normalize(quarterTurns);
        RebuildOccupiedCells();

        if (!grid.TryRegister(this))
        {
            return false;
        }

        transform.position = grid.CellToWorld(anchorCell);
        transform.rotation = Quaternion.Euler(0f, -QuarterTurns * 90f, 0f);
        OnInitialized();
        return true;
    }

    public void RemoveFromGrid()
    {
        if (Grid != null)
        {
            Grid.Unregister(this);
        }

        OnRemoved();
        Destroy(gameObject);
    }

    protected virtual void OnInitialized()
    {
    }

    protected virtual void OnRemoved()
    {
    }

    protected Vector2Int RotateLocalCell(Vector2Int localCell)
    {
        return AnchorCell + GridDirection.Rotate(localCell, QuarterTurns);
    }

    protected virtual void OnDrawGizmosSelected()
    {
        if (Grid == null)
        {
            return;
        }

        Gizmos.color = Color.yellow;
        for (int i = 0; i < occupiedCells.Count; i++)
        {
            Gizmos.DrawWireCube(Grid.CellToWorld(occupiedCells[i]), new Vector3(0.95f, 0.1f, 0.95f));
        }
    }

    private void RebuildOccupiedCells()
    {
        occupiedCells.Clear();
        Vector2Int footprint = Footprint;
        for (int x = 0; x < footprint.x; x++)
        {
            for (int y = 0; y < footprint.y; y++)
            {
                occupiedCells.Add(RotateLocalCell(new Vector2Int(x, y)));
            }
        }
    }
}
