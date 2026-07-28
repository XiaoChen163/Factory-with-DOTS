using System;
using System.Collections.Generic;
using UnityEngine;

    public sealed class BuildController : MonoBehaviour
    {
        private Camera inputCamera;
        private GridMap grid;
        private ItemData ore;
        private RecipeData smeltingRecipe;
        private int quarterTurns;
        private readonly List<GameObject> previewCells = new List<GameObject>();
        private GameObject previewRoot;
        private BuildingKind previewKind;
        private int previewQuarterTurns = -1;

        public BuildingKind SelectedKind { get; private set; } = BuildingKind.Belt;
        public Vector2Int HoveredCell { get; private set; }
        public bool HasHoveredCell { get; private set; }
        public event Action BuildingsChanged;

        public void Initialize(Camera cameraToUse, GridMap targetGrid, ItemData oreItem, RecipeData recipe)
        {
            inputCamera = cameraToUse;
            grid = targetGrid;
            ore = oreItem;
            smeltingRecipe = recipe;
            previewRoot = new GameObject("Placement Preview");
        }

        public GridBuilding TryBuild(BuildingKind kind, Vector2Int anchor, int rotation)
        {
            GameObject buildingObject = new GameObject(kind + " " + anchor.x + "," + anchor.y);
            GridBuilding building;

            switch (kind)
            {
                case BuildingKind.Miner:
                    Miner miner = buildingObject.AddComponent<Miner>();
                    miner.OutputItem = ore;
                    building = miner;
                    break;
                case BuildingKind.Furnace:
                    Furnace furnace = buildingObject.AddComponent<Furnace>();
                    furnace.Recipe = smeltingRecipe;
                    building = furnace;
                    break;
                case BuildingKind.Storage:
                    building = buildingObject.AddComponent<Storage>();
                    break;
                default:
                    building = buildingObject.AddComponent<Belt>();
                    break;
            }

            if (!building.Initialize(grid, anchor, rotation))
            {
                Destroy(buildingObject);
                return null;
            }

            BuildingsChanged?.Invoke();
            return building;
        }

        private void Update()
        {
            HandleSelection();
            UpdateHoveredCell();

            if (UnityEngine.Input.GetKeyDown(KeyCode.R))
            {
                quarterTurns = (quarterTurns + 1) % 4;
            }

            UpdatePlacementPreview();

            if (!HasHoveredCell)
            {
                return;
            }

            if (UnityEngine.Input.GetMouseButtonDown(0))
            {
                Debug.Log("[Stage 0] Clicked grid cell " + HoveredCell);
                GridBuilding building = TryBuild(SelectedKind, HoveredCell, quarterTurns);
                if (building == null)
                {
                    Debug.LogWarning("[Stage 0] Cannot build " + SelectedKind + " at " + HoveredCell + ".");
                }
            }

            if (UnityEngine.Input.GetKeyDown(KeyCode.F))
            {
                GridBuilding occupant = grid.GetOccupant(HoveredCell);
                if (occupant != null)
                {
                    Debug.Log("[Stage 0] Removed " + occupant.Kind + " at grid cell " + HoveredCell);
                    occupant.RemoveFromGrid();
                    BuildingsChanged?.Invoke();
                }
            }
        }

        private void HandleSelection()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha1)) SelectedKind = BuildingKind.Belt;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha2)) SelectedKind = BuildingKind.Miner;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha3)) SelectedKind = BuildingKind.Furnace;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha4)) SelectedKind = BuildingKind.Storage;
        }

        private void UpdateHoveredCell()
        {
            HasHoveredCell = false;
            if (inputCamera == null || grid == null)
            {
                return;
            }

            Ray ray = inputCamera.ScreenPointToRay(UnityEngine.Input.mousePosition);
            Plane plane = new Plane(Vector3.up, grid.Origin);
            float distance;
            if (!plane.Raycast(ray, out distance))
            {
                return;
            }

            HasHoveredCell = grid.TryWorldToCell(ray.GetPoint(distance), out Vector2Int hoveredCell);
            HoveredCell = hoveredCell;
        }

        private void UpdatePlacementPreview()
        {
            if (previewRoot == null)
            {
                return;
            }

            if (previewKind != SelectedKind || previewQuarterTurns != quarterTurns)
            {
                RebuildPreviewShape();
            }

            List<Vector2Int> cells = GetOccupiedCells(SelectedKind, HoveredCell, quarterTurns);
            bool canPreview = HasHoveredCell && grid.CanOccupy(cells);
            previewRoot.SetActive(canPreview);
            if (!canPreview)
            {
                return;
            }

            for (int i = 0; i < previewCells.Count; i++)
            {
                Vector3 position = grid.CellToWorld(cells[i]);
                float height = SelectedKind == BuildingKind.Belt ? 0.10f : 0.34f;
                previewCells[i].transform.position = position + Vector3.up * height;
                previewCells[i].transform.rotation = SelectedKind == BuildingKind.Belt
                    ? Quaternion.Euler(0f, -quarterTurns * 90f, 0f)
                    : Quaternion.identity;
            }
        }

        private void RebuildPreviewShape()
        {
            for (int i = 0; i < previewCells.Count; i++)
            {
                Destroy(previewCells[i]);
            }

            previewCells.Clear();
            previewKind = SelectedKind;
            previewQuarterTurns = quarterTurns;

            Vector2Int footprint = GetFootprint(SelectedKind);
            int cellCount = footprint.x * footprint.y;
            for (int i = 0; i < cellCount; i++)
            {
                previewCells.Add(PrototypeVisuals.CreatePreviewCell(
                    previewRoot.transform,
                    SelectedKind == BuildingKind.Belt));
            }
        }

        private static List<Vector2Int> GetOccupiedCells(
            BuildingKind kind,
            Vector2Int anchor,
            int rotation)
        {
            Vector2Int footprint = GetFootprint(kind);
            List<Vector2Int> cells = new List<Vector2Int>(footprint.x * footprint.y);
            for (int x = 0; x < footprint.x; x++)
            {
                for (int y = 0; y < footprint.y; y++)
                {
                    Vector2Int localCell = new Vector2Int(x, y);
                    cells.Add(anchor + GridDirection.Rotate(localCell, rotation));
                }
            }

            return cells;
        }

        private static Vector2Int GetFootprint(BuildingKind kind)
        {
            return kind == BuildingKind.Belt ? Vector2Int.one : new Vector2Int(2, 2);
        }
}
