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
        private bool beltPathStarted;
        private bool horizontalFirst = true;

        private struct BeltPathCell
        {
            public BeltPathCell(Vector2Int cell, Vector2Int direction)
            {
                Cell = cell;
                Direction = direction;
            }

            public Vector2Int Cell { get; }
            public Vector2Int Direction { get; }
        }

        public BuildingKind SelectedKind { get; private set; } = BuildingKind.Belt;
        public Vector2Int HoveredCell { get; private set; }
        public bool HasHoveredCell { get; private set; }
        public bool IsBeltPathStarted => beltPathStarted;
        public Vector2Int BeltPathStart { get; private set; }
        public string BeltPathMode => horizontalFirst ? "Horizontal → Vertical" : "Vertical → Horizontal";
        public Vector2Int BeltInitialDirection => GridDirection.FromQuarterTurns(quarterTurns);
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
            List<Vector2Int> occupiedCells = GetOccupiedCells(kind, anchor, rotation);
            if (!TransportTopology.CanPlace(
                    grid,
                    kind,
                    anchor,
                    rotation,
                    occupiedCells,
                    out string failureReason))
            {
                Debug.LogWarning(
                    "[Stage 2] Cannot build " + kind + " at " + anchor + ": " + failureReason + ".");
                return null;
            }

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
                case BuildingKind.Merger:
                    building = buildingObject.AddComponent<MergerLogic>();
                    break;
                case BuildingKind.Splitter:
                    building = buildingObject.AddComponent<SplitterLogic>();
                    break;
                default:
                    building = buildingObject.AddComponent<BeltLogic>();
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

            if (Input.GetKeyDown(KeyCode.R))
            {
                if (SelectedKind == BuildingKind.Belt)
                {
                    if (beltPathStarted)
                    {
                        horizontalFirst = !horizontalFirst;
                    }
                    else
                    {
                        quarterTurns = (quarterTurns - 1) % 4;
                    }
                }
                else
                {
                    quarterTurns = (quarterTurns - 1) % 4;
                }
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                beltPathStarted = false;
            }

            UpdatePlacementPreview();

            if (!HasHoveredCell)
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                Debug.Log("[Stage 0] Clicked grid cell " + HoveredCell);
                if (SelectedKind == BuildingKind.Belt)
                {
                    HandleBeltPathClick();
                }
                else
                {
                    GridBuilding building = TryBuild(SelectedKind, HoveredCell, quarterTurns);
                    if (building == null)
                    {
                        Debug.LogWarning("[Stage 0] Cannot build " + SelectedKind + " at " + HoveredCell + ".");
                    }
                }
            }

            if (Input.GetKeyDown(KeyCode.F))
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
            BuildingKind previousKind = SelectedKind;
            if (Input.GetKeyDown(KeyCode.Alpha1)) SelectedKind = BuildingKind.Belt;
            if (Input.GetKeyDown(KeyCode.Alpha2)) SelectedKind = BuildingKind.Miner;
            if (Input.GetKeyDown(KeyCode.Alpha3)) SelectedKind = BuildingKind.Furnace;
            if (Input.GetKeyDown(KeyCode.Alpha4)) SelectedKind = BuildingKind.Storage;
            if (Input.GetKeyDown(KeyCode.Alpha5)) SelectedKind = BuildingKind.Merger;
            if (Input.GetKeyDown(KeyCode.Alpha6)) SelectedKind = BuildingKind.Splitter;

            if (SelectedKind != previousKind)
            {
                beltPathStarted = false;
            }
        }

        private void UpdateHoveredCell()
        {
            HasHoveredCell = false;
            if (inputCamera == null || grid == null)
            {
                return;
            }

            Ray ray = inputCamera.ScreenPointToRay(Input.mousePosition);
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

            if (!HasHoveredCell)
            {
                previewRoot.SetActive(false);
                return;
            }

            previewRoot.SetActive(true);
            if (SelectedKind == BuildingKind.Belt)
            {
                List<BeltPathCell> path = beltPathStarted
                    ? BuildBeltPath(
                        BeltPathStart,
                        HoveredCell,
                        horizontalFirst,
                        BeltInitialDirection)
                    : BuildBeltPath(
                        HoveredCell,
                        HoveredCell,
                        horizontalFirst,
                        BeltInitialDirection);
                EnsurePreviewCellCount(path.Count, true);

                for (int i = 0; i < path.Count; i++)
                {
                    BeltPathCell pathCell = path[i];
                    GameObject preview = previewCells[i];
                    bool isBlocked =
                        !grid.Contains(pathCell.Cell) ||
                        grid.GetOccupant(pathCell.Cell) != null;
                    preview.transform.position =
                        grid.CellToWorld(pathCell.Cell) +
                        Vector3.up * (isBlocked ? 0.85f : 0.22f);
                    preview.transform.rotation = Quaternion.Euler(
                        0f,
                        -DirectionToQuarterTurns(pathCell.Direction) * 90f,
                        0f);
                    PrototypeVisuals.SetPreviewCellBlocked(preview, isBlocked);
                }

                return;
            }

            List<Vector2Int> cells = GetOccupiedCells(SelectedKind, HoveredCell, quarterTurns);
            EnsurePreviewCellCount(cells.Count, IsSingleCellTransport(SelectedKind));
            for (int i = 0; i < previewCells.Count; i++)
            {
                Vector3 position = grid.CellToWorld(cells[i]);
                bool isTransportCell = IsSingleCellTransport(SelectedKind);
                float height = isTransportCell ? 0.10f : 0.34f;
                previewCells[i].transform.position = position + Vector3.up * height;
                previewCells[i].transform.rotation = isTransportCell
                    ? Quaternion.Euler(0f, -quarterTurns * 90f, 0f)
                    : Quaternion.identity;
                PrototypeVisuals.SetPreviewCellBlocked(
                    previewCells[i],
                    !grid.Contains(cells[i]) || grid.GetOccupant(cells[i]) != null);
            }
        }

        private void EnsurePreviewCellCount(int requiredCount, bool isTransport)
        {
            if (previewKind != SelectedKind)
            {
                for (int i = 0; i < previewCells.Count; i++)
                {
                    previewCells[i].SetActive(false);
                    Destroy(previewCells[i]);
                }

                previewCells.Clear();
            }

            previewKind = SelectedKind;

            while (previewCells.Count > requiredCount)
            {
                int lastIndex = previewCells.Count - 1;
                GameObject extraPreview = previewCells[lastIndex];
                previewCells.RemoveAt(lastIndex);
                extraPreview.SetActive(false);
                Destroy(extraPreview);
            }

            while (previewCells.Count < requiredCount)
            {
                previewCells.Add(PrototypeVisuals.CreatePreviewCell(
                    previewRoot.transform,
                    isTransport));
            }
        }

        private void HandleBeltPathClick()
        {
            if (!beltPathStarted)
            {
                if (grid.GetOccupant(HoveredCell) != null)
                {
                    Debug.LogWarning(
                        "[Stage 2] Belt path start cell is already occupied: " + HoveredCell + ".");
                    return;
                }

                BeltPathStart = HoveredCell;
                beltPathStarted = true;
                return;
            }

            List<BeltPathCell> path =
                BuildBeltPath(
                    BeltPathStart,
                    HoveredCell,
                    horizontalFirst,
                    BeltInitialDirection);
            for (int i = 0; i < path.Count; i++)
            {
                Vector2Int cell = path[i].Cell;
                if (!grid.Contains(cell) || grid.GetOccupant(cell) != null)
                {
                    Debug.LogWarning(
                        "[Stage 2] Belt path overlaps an occupied or invalid cell at " + cell + ".");
                    return;
                }
            }

            List<GridBuilding> placedBelts = new List<GridBuilding>(path.Count);
            for (int i = 0; i < path.Count; i++)
            {
                BeltPathCell pathCell = path[i];
                GridBuilding belt = TryBuild(
                    BuildingKind.Belt,
                    pathCell.Cell,
                    DirectionToQuarterTurns(pathCell.Direction));
                if (belt == null)
                {
                    for (int placedIndex = 0; placedIndex < placedBelts.Count; placedIndex++)
                    {
                        placedBelts[placedIndex].RemoveFromGrid();
                    }

                    BuildingsChanged?.Invoke();
                    Debug.LogWarning("[Stage 2] Belt path placement was cancelled atomically.");
                    return;
                }

                placedBelts.Add(belt);
            }

            beltPathStarted = false;
        }

        private static List<BeltPathCell> BuildBeltPath(
            Vector2Int start,
            Vector2Int end,
            bool useHorizontalFirst,
            Vector2Int initialDirection)
        {
            List<Vector2Int> cells = new List<Vector2Int>();
            Vector2Int corner = useHorizontalFirst
                ? new Vector2Int(end.x, start.y)
                : new Vector2Int(start.x, end.y);

            AppendSegment(cells, start, corner, true);
            AppendSegment(cells, corner, end, false);

            List<BeltPathCell> path = new List<BeltPathCell>(cells.Count);
            Vector2Int fallbackDirection = initialDirection;
            for (int i = 0; i < cells.Count; i++)
            {
                Vector2Int direction;
                if (i < cells.Count - 1)
                {
                    direction = cells[i + 1] - cells[i];
                    fallbackDirection = direction;
                }
                else
                {
                    direction = fallbackDirection;
                }

                path.Add(new BeltPathCell(cells[i], direction));
            }

            return path;
        }

        private static void AppendSegment(
            List<Vector2Int> cells,
            Vector2Int from,
            Vector2Int to,
            bool includeStart)
        {
            Vector2Int delta = to - from;
            Vector2Int step = new Vector2Int(
                delta.x == 0 ? 0 : delta.x > 0 ? 1 : -1,
                delta.y == 0 ? 0 : delta.y > 0 ? 1 : -1);
            int length = Mathf.Abs(delta.x) + Mathf.Abs(delta.y);
            int first = includeStart ? 0 : 1;
            for (int i = first; i <= length; i++)
            {
                cells.Add(from + step * i);
            }
        }

        private static int DirectionToQuarterTurns(Vector2Int direction)
        {
            if (direction == Vector2Int.up) return 1;
            if (direction == Vector2Int.left) return 2;
            if (direction == Vector2Int.down) return 3;
            return 0;
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
            return IsSingleCellTransport(kind) ? Vector2Int.one : new Vector2Int(2, 2);
        }

        private static bool IsSingleCellTransport(BuildingKind kind)
        {
            return kind == BuildingKind.Belt ||
                   kind == BuildingKind.Merger ||
                   kind == BuildingKind.Splitter;
        }
}
