using System.Collections.Generic;
using FactoryWithDots.Stage0.Buildings;
using UnityEngine;

namespace FactoryWithDots.Stage0.Core
{
    public sealed class GridMap
    {
        private readonly GridBuilding[,] occupants;

        public GridMap(int width, int height, Vector3 origin)
        {
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            Origin = origin;
            occupants = new GridBuilding[Width, Height];
        }

        public int Width { get; }
        public int Height { get; }
        public Vector3 Origin { get; }

        public bool Contains(Vector2Int cell)
        {
            return cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height;
        }

        public Vector3 CellToWorld(Vector2Int cell)
        {
            return Origin + new Vector3(cell.x + 0.5f, 0f, cell.y + 0.5f);
        }

        public bool TryWorldToCell(Vector3 worldPosition, out Vector2Int cell)
        {
            Vector3 local = worldPosition - Origin;
            cell = new Vector2Int(Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.z));
            return Contains(cell);
        }

        public GridBuilding GetOccupant(Vector2Int cell)
        {
            return Contains(cell) ? occupants[cell.x, cell.y] : null;
        }

        public bool CanOccupy(IReadOnlyList<Vector2Int> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                Vector2Int cell = cells[i];
                if (!Contains(cell) || occupants[cell.x, cell.y] != null)
                {
                    return false;
                }
            }

            return true;
        }

        public bool TryRegister(GridBuilding building)
        {
            IReadOnlyList<Vector2Int> cells = building.OccupiedCells;
            if (!CanOccupy(cells))
            {
                return false;
            }

            for (int i = 0; i < cells.Count; i++)
            {
                Vector2Int cell = cells[i];
                occupants[cell.x, cell.y] = building;
            }

            return true;
        }

        public void Unregister(GridBuilding building)
        {
            IReadOnlyList<Vector2Int> cells = building.OccupiedCells;
            for (int i = 0; i < cells.Count; i++)
            {
                Vector2Int cell = cells[i];
                if (Contains(cell) && occupants[cell.x, cell.y] == building)
                {
                    occupants[cell.x, cell.y] = null;
                }
            }
        }
    }
}
