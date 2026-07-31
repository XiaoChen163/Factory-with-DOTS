using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
public sealed class EcsGridInteractionController : MonoBehaviour
{
    private static readonly Color ValidPreviewColor =
        new Color(0.08f, 0.42f, 1f, 0.48f);
    private static readonly Color InvalidPreviewColor =
        new Color(1f, 0.08f, 0.08f, 0.55f);

    private readonly List<int2> previewCells =
        new List<int2>(64);
    private readonly List<GameObject> previewCellObjects =
        new List<GameObject>(64);
    private readonly List<Transform> previewTriangles =
        new List<Transform>(64);
    private readonly List<int2> previewDisplayDirections =
        new List<int2>(64);

    private Camera inputCamera;
    private Transform previewRoot;
    private Material previewMaterial;
    private Mesh previewTriangleMesh;
    private uint nextRequestId = 1;
    private byte quarterTurns;
    private bool beltPathStarted;
    private bool horizontalFirst = true;
    private int2 beltPathStart;

    public BuildingKind SelectedKind { get; private set; } =
        BuildingKind.Belt;

    private void Awake()
    {
        inputCamera = GetComponent<Camera>();
    }

    private void Update()
    {
        ConsumeBuildResults();
        HandleSelectionAndRotation();
        UpdatePlacementPreview();

        if (Input.GetMouseButtonDown(0))
        {
            HandlePrimaryClick();
        }

        if (Input.GetKeyDown(KeyCode.F))
        {
            bool removeBeltLine =
                Input.GetKey(KeyCode.LeftControl) ||
                Input.GetKey(KeyCode.RightControl);
            HandleRemove(removeBeltLine);
        }
    }

    private void OnDisable()
    {
        HidePlacementPreview();
    }

    private void OnDestroy()
    {
        if (previewRoot != null)
        {
            Destroy(previewRoot.gameObject);
        }

        if (previewMaterial != null)
        {
            Destroy(previewMaterial);
        }

        if (previewTriangleMesh != null)
        {
            Destroy(previewTriangleMesh);
        }
    }

    private void OnGUI()
    {
        GUI.Box(
            new Rect(12f, 170f, 780f, 140f),
            "ECS Grid Build Controls");
        GUI.Label(
            new Rect(28f, 196f, 740f, 22f),
            "1 Belt  2 Miner  3 Furnace  4 Storage  5 Merger  6 Splitter  |  R Rotate");
        GUI.Label(
            new Rect(28f, 220f, 740f, 22f),
            "F Remove  |  Ctrl+F Remove Connected Belt Line  |  WASD Move  Space Ascend  Shift Descend");
        GUI.Label(
            new Rect(28f, 244f, 740f, 22f),
            "Hold Right Mouse Rotate");
        GUI.Label(
            new Rect(28f, 268f, 740f, 22f),
            "Selected: " + SelectedKind +
            "  Direction: " +
            EcsGridUtility.Rotate(
                new int2(1, 0),
                quarterTurns) +
            (beltPathStarted
                ? "  |  Belt start: " + beltPathStart +
                  "  |  " +
                  (horizontalFirst
                      ? "Horizontal → Vertical"
                      : "Vertical → Horizontal")
                : string.Empty));
    }

    private void HandleSelectionAndRotation()
    {
        BuildingKind previousKind = SelectedKind;
        if (Input.GetKeyDown(KeyCode.Alpha1))
            SelectedKind = BuildingKind.Belt;
        if (Input.GetKeyDown(KeyCode.Alpha2))
            SelectedKind = BuildingKind.Miner;
        if (Input.GetKeyDown(KeyCode.Alpha3))
            SelectedKind = BuildingKind.Furnace;
        if (Input.GetKeyDown(KeyCode.Alpha4))
            SelectedKind = BuildingKind.Storage;
        if (Input.GetKeyDown(KeyCode.Alpha5))
            SelectedKind = BuildingKind.Merger;
        if (Input.GetKeyDown(KeyCode.Alpha6))
            SelectedKind = BuildingKind.Splitter;

        if (SelectedKind != previousKind)
        {
            beltPathStarted = false;
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            if (SelectedKind == BuildingKind.Belt &&
                beltPathStarted)
            {
                horizontalFirst = !horizontalFirst;
            }
            else
            {
                quarterTurns = (byte)((quarterTurns + 3) % 4);
            }
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            beltPathStarted = false;
        }
    }

    private void UpdatePlacementPreview()
    {
        if (!TryRaycastGrid(
                out World world,
                out GridDefinition grid,
                out _,
                out int2 hoveredCell,
                out _,
                out _,
                false))
        {
            HidePlacementPreview();
            return;
        }

        BuildPreviewCells(hoveredCell);
        GridOccupancyIndexSystem occupancySystem =
            world.GetExistingSystemManaged<
                GridOccupancyIndexSystem>();
        bool canPlace =
            occupancySystem != null &&
            occupancySystem.IsReady &&
            occupancySystem.ConflictCount == 0;

        for (int i = 0; i < previewCells.Count; i++)
        {
            int2 cell = previewCells[i];
            if (!EcsGridUtility.Contains(grid, cell) ||
                occupancySystem == null ||
                occupancySystem.TryGetOccupant(cell, out _))
            {
                canPlace = false;
            }
        }

        ShowPlacementPreview(grid, canPlace);
    }

    private void BuildPreviewCells(int2 hoveredCell)
    {
        previewCells.Clear();
        previewDisplayDirections.Clear();
        if (SelectedKind == BuildingKind.Belt &&
            beltPathStarted)
        {
            int2 corner = horizontalFirst
                ? new int2(hoveredCell.x, beltPathStart.y)
                : new int2(beltPathStart.x, hoveredCell.y);
            AppendPreviewSegment(
                beltPathStart,
                corner,
                true);
            AppendPreviewSegment(
                corner,
                hoveredCell,
                false);
            BuildBeltPreviewDirections(
                EcsGridUtility.Rotate(
                    new int2(1, 0),
                    quarterTurns));
            return;
        }

        previewCells.Add(hoveredCell);
        if (SelectedKind == BuildingKind.Belt)
        {
            previewDisplayDirections.Add(
                EcsGridUtility.Rotate(
                    new int2(1, 0),
                    quarterTurns));
        }

        if (!IsTwoByTwoBuilding(SelectedKind))
        {
            return;
        }

        previewCells.Add(
            hoveredCell +
            EcsGridUtility.Rotate(
                new int2(0, 1),
                quarterTurns));
        previewCells.Add(
            hoveredCell +
            EcsGridUtility.Rotate(
                new int2(1, 0),
                quarterTurns));
        previewCells.Add(
            hoveredCell +
            EcsGridUtility.Rotate(
                new int2(1, 1),
                quarterTurns));
    }

    private void AppendPreviewSegment(
        int2 from,
        int2 to,
        bool includeStart)
    {
        int2 delta = to - from;
        int2 step = new int2(
            delta.x == 0 ? 0 : delta.x > 0 ? 1 : -1,
            delta.y == 0 ? 0 : delta.y > 0 ? 1 : -1);
        int length = math.abs(delta.x) + math.abs(delta.y);
        int first = includeStart ? 0 : 1;
        for (int i = first; i <= length; i++)
        {
            previewCells.Add(from + step * i);
        }
    }

    private void BuildBeltPreviewDirections(
        int2 initialDirection)
    {
        int2 fallbackDirection =
            EcsGridUtility.SanitizeDirection(initialDirection);
        for (int i = 0; i < previewCells.Count; i++)
        {
            int2 outputDirection;
            if (i < previewCells.Count - 1)
            {
                outputDirection =
                    previewCells[i + 1] - previewCells[i];
                fallbackDirection = outputDirection;
            }
            else
            {
                outputDirection = fallbackDirection;
            }

            int2 displayDirection = outputDirection;
            if (i > 0)
            {
                int2 incomingDirection =
                    previewCells[i] - previewCells[i - 1];
                if (!math.all(
                        incomingDirection == outputDirection))
                {
                    int2 cornerDirection =
                        incomingDirection + outputDirection;
                    if (!math.all(
                            cornerDirection == int2.zero))
                    {
                        displayDirection = cornerDirection;
                    }
                }
            }

            previewDisplayDirections.Add(displayDirection);
        }
    }

    private void ShowPlacementPreview(
        in GridDefinition grid,
        bool canPlace)
    {
        EnsurePreviewObjects(previewCells.Count);
        if (previewMaterial == null)
        {
            HidePlacementPreview();
            return;
        }

        Color color = canPlace
            ? ValidPreviewColor
            : InvalidPreviewColor;
        if (previewMaterial.HasProperty("_BaseColor"))
        {
            previewMaterial.SetColor("_BaseColor", color);
        }

        if (previewMaterial.HasProperty("_Color"))
        {
            previewMaterial.SetColor("_Color", color);
        }

        float cellSize = math.max(
            math.EPSILON,
            grid.CellSize);
        for (int i = 0; i < previewCellObjects.Count; i++)
        {
            GameObject previewCell = previewCellObjects[i];
            bool active = i < previewCells.Count;
            previewCell.SetActive(active);
            if (!active)
            {
                continue;
            }

            float3 center = EcsGridUtility.CellToWorldCenter(
                previewCells[i],
                grid.Origin.y + 0.055f,
                grid);
            previewCell.transform.position = new Vector3(
                center.x,
                center.y,
                center.z);
            previewCell.transform.rotation = Quaternion.identity;
            previewCell.transform.localScale = new Vector3(
                cellSize * 0.9f,
                0.1f,
                cellSize * 0.9f);

            Transform triangle = previewTriangles[i];
            bool showTriangle =
                SelectedKind == BuildingKind.Belt &&
                i < previewDisplayDirections.Count;
            triangle.gameObject.SetActive(showTriangle);
            if (showTriangle)
            {
                int2 displayDirection =
                    previewDisplayDirections[i];
                float angle = -math.atan2(
                    displayDirection.y,
                    displayDirection.x) * math.TODEGREES;
                triangle.localRotation = Quaternion.Euler(
                    0f,
                    angle,
                    0f);
            }
        }
    }

    private void EnsurePreviewObjects(int requiredCount)
    {
        if (previewRoot == null)
        {
            GameObject root = new GameObject(
                "ECS Building Placement Preview");
            root.hideFlags = HideFlags.DontSave;
            root.layer = 2;
            previewRoot = root.transform;
        }

        if (previewMaterial == null)
        {
            Shader shader = Shader.Find(
                                "Universal Render Pipeline/Unlit") ??
                            Shader.Find("Unlit/Color") ??
                            Shader.Find("Sprites/Default");
            if (shader == null)
            {
                return;
            }

            previewMaterial = new Material(shader)
            {
                name = "ECS Building Preview Material",
                hideFlags = HideFlags.DontSave,
                renderQueue = (int)RenderQueue.Transparent
            };
            previewMaterial.SetOverrideTag(
                "RenderType",
                "Transparent");
            if (previewMaterial.HasProperty("_Surface"))
            {
                previewMaterial.SetFloat("_Surface", 1f);
            }

            if (previewMaterial.HasProperty("_SrcBlend"))
            {
                previewMaterial.SetFloat(
                    "_SrcBlend",
                    (float)BlendMode.SrcAlpha);
            }

            if (previewMaterial.HasProperty("_DstBlend"))
            {
                previewMaterial.SetFloat(
                    "_DstBlend",
                    (float)BlendMode.OneMinusSrcAlpha);
            }

            if (previewMaterial.HasProperty("_ZWrite"))
            {
                previewMaterial.SetFloat("_ZWrite", 0f);
            }

            previewMaterial.EnableKeyword(
                "_SURFACE_TYPE_TRANSPARENT");
        }

        while (previewCellObjects.Count < requiredCount)
        {
            GameObject previewCell = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            previewCell.name =
                "Placement Preview Cell " +
                previewCellObjects.Count;
            previewCell.hideFlags = HideFlags.DontSave;
            previewCell.layer = 2;
            previewCell.transform.SetParent(previewRoot, false);

            Collider previewCollider =
                previewCell.GetComponent<Collider>();
            if (previewCollider != null)
            {
                Destroy(previewCollider);
            }

            MeshRenderer meshRenderer =
                previewCell.GetComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = previewMaterial;
            meshRenderer.shadowCastingMode =
                ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            previewCellObjects.Add(previewCell);

            GameObject triangle = new GameObject(
                "Preview Direction Triangle");
            triangle.hideFlags = HideFlags.DontSave;
            triangle.layer = 2;
            triangle.transform.SetParent(
                previewCell.transform,
                false);
            triangle.transform.localPosition =
                new Vector3(0f, 0.65f, 0f);
            triangle.transform.localScale = Vector3.one;
            triangle.AddComponent<MeshFilter>().sharedMesh =
                GetPreviewTriangleMesh();
            MeshRenderer triangleRenderer =
                triangle.AddComponent<MeshRenderer>();
            triangleRenderer.sharedMaterial = previewMaterial;
            triangleRenderer.shadowCastingMode =
                ShadowCastingMode.Off;
            triangleRenderer.receiveShadows = false;
            previewTriangles.Add(triangle.transform);
        }
    }

    private Mesh GetPreviewTriangleMesh()
    {
        if (previewTriangleMesh != null)
        {
            return previewTriangleMesh;
        }

        previewTriangleMesh = new Mesh
        {
            name = "ECS Preview Direction Triangle",
            hideFlags = HideFlags.DontSave,
            vertices = new[]
            {
                new Vector3(0.30f, 0f, 0f),
                new Vector3(-0.22f, 0f, -0.19f),
                new Vector3(-0.22f, 0f, 0.19f)
            },
            triangles = new[] { 0, 1, 2 }
        };
        previewTriangleMesh.RecalculateNormals();
        previewTriangleMesh.RecalculateBounds();
        return previewTriangleMesh;
    }

    private void HidePlacementPreview()
    {
        for (int i = 0; i < previewCellObjects.Count; i++)
        {
            previewCellObjects[i].SetActive(false);
        }
    }

    private void HandlePrimaryClick()
    {
        if (!TryRaycastGrid(
                out World world,
                out _,
                out Entity gridEntity,
                out int2 cell,
                out Vector3 hitPoint,
                out bool isInside,
                true))
        {
            return;
        }

        Entity occupant = Entity.Null;
        GridOccupancyIndexSystem occupancySystem =
            world.GetExistingSystemManaged<
                GridOccupancyIndexSystem>();
        bool isOccupied =
            isInside &&
            occupancySystem != null &&
            occupancySystem.TryGetOccupant(
                cell,
                out occupant);

        Debug.Log(
            "[ECS Grid Raycast] Hit=" + hitPoint +
            ", Cell=(" + cell.x + ", " + cell.y + ")" +
            ", Inside=" + isInside +
            ", Occupied=" + isOccupied +
            (isOccupied ? ", Entity=" + occupant : string.Empty) +
            ".");

        if (!isInside)
        {
            return;
        }

        if (SelectedKind == BuildingKind.Belt)
        {
            if (!beltPathStarted)
            {
                if (isOccupied)
                {
                    Debug.LogWarning(
                        "[ECS Grid Build] Belt path start " +
                        cell + " is occupied.");
                    return;
                }

                beltPathStart = cell;
                beltPathStarted = true;
                return;
            }

            Enqueue(
                world,
                gridEntity,
                new GridBuildCommand
                {
                    RequestId = nextRequestId++,
                    Type =
                        GridBuildCommandType.PlaceBeltPath,
                    Kind = BuildingKind.Belt,
                    StartCell = beltPathStart,
                    EndCell = cell,
                    QuarterTurns = quarterTurns,
                    HorizontalFirst =
                        horizontalFirst ? (byte)1 : (byte)0
                });
            beltPathStarted = false;
            return;
        }

        Enqueue(
            world,
            gridEntity,
            new GridBuildCommand
            {
                RequestId = nextRequestId++,
                Type = GridBuildCommandType.Place,
                Kind = SelectedKind,
                StartCell = cell,
                EndCell = cell,
                QuarterTurns = quarterTurns,
                HorizontalFirst = 0
            });
    }

    private void HandleRemove(bool removeBeltLine)
    {
        if (!TryRaycastGrid(
                out World world,
                out _,
                out Entity gridEntity,
                out int2 cell,
                out _,
                out bool isInside,
                false) ||
            !isInside)
        {
            return;
        }

        beltPathStarted = false;

        Enqueue(
            world,
            gridEntity,
            new GridBuildCommand
            {
                RequestId = nextRequestId++,
                Type = removeBeltLine
                    ? GridBuildCommandType.RemoveBeltLine
                    : GridBuildCommandType.Remove,
                Kind = removeBeltLine
                    ? BuildingKind.Belt
                    : SelectedKind,
                StartCell = cell,
                EndCell = cell,
                QuarterTurns = 0,
                HorizontalFirst = 0
            });
    }

    private bool TryRaycastGrid(
        out World world,
        out GridDefinition grid,
        out Entity gridEntity,
        out int2 cell,
        out Vector3 hitPoint,
        out bool isInside,
        bool logFailure)
    {
        world = World.DefaultGameObjectInjectionWorld;
        grid = default;
        gridEntity = Entity.Null;
        cell = default;
        hitPoint = default;
        isInside = false;
        Ray ray = inputCamera.ScreenPointToRay(
            Input.mousePosition);
        if (world == null || !world.IsCreated)
        {
            if (logFailure)
            {
                Debug.LogWarning(
                    "[ECS Grid Raycast] The default ECS World is unavailable.");
            }

            return false;
        }

        EntityManager entityManager = world.EntityManager;
        EntityQuery query = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<GridDefinition>());
        int gridCount = query.CalculateEntityCount();
        bool found = gridCount == 1;
        if (found)
        {
            gridEntity = query.GetSingletonEntity();
            grid = query.GetSingleton<GridDefinition>();
        }

        query.Dispose();
        if (!found)
        {
            if (logFailure)
            {
                Debug.LogWarning(
                    "[ECS Grid Raycast] Exactly one GridDefinition is required; found " +
                    gridCount + ".");
            }

            return false;
        }

        Plane gridPlane = new Plane(
            Vector3.up,
            new Vector3(
                grid.Origin.x,
                grid.Origin.y,
                grid.Origin.z));
        if (!gridPlane.Raycast(ray, out float distance))
        {
            if (logFailure)
            {
                Debug.Log(
                    "[ECS Grid Raycast] Miss. Origin=" +
                    ray.origin +
                    ", Direction=" + ray.direction + ".");
            }

            return false;
        }

        hitPoint = ray.GetPoint(distance);
        cell = EcsGridUtility.WorldToCell(
            new float3(
                hitPoint.x,
                hitPoint.y,
                hitPoint.z),
            grid);
        isInside = EcsGridUtility.Contains(grid, cell);
        return true;
    }

    private static bool IsTwoByTwoBuilding(BuildingKind kind)
    {
        return kind == BuildingKind.Miner ||
               kind == BuildingKind.Furnace ||
               kind == BuildingKind.Storage;
    }

    private static void Enqueue(
        World world,
        Entity gridEntity,
        GridBuildCommand command)
    {
        EntityManager entityManager = world.EntityManager;
        if (!entityManager.HasBuffer<GridBuildCommand>(gridEntity))
        {
            Debug.LogWarning(
                "[ECS Grid Build] Grid command buffer is unavailable.");
            return;
        }

        entityManager
            .GetBuffer<GridBuildCommand>(gridEntity)
            .Add(command);
    }

    private static void ConsumeBuildResults()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            return;
        }

        EntityManager entityManager = world.EntityManager;
        EntityQuery query = entityManager.CreateEntityQuery(
            ComponentType.ReadWrite<GridBuildResult>());
        if (query.CalculateEntityCount() != 1)
        {
            query.Dispose();
            return;
        }

        Entity gridEntity = query.GetSingletonEntity();
        DynamicBuffer<GridBuildResult> results =
            entityManager.GetBuffer<GridBuildResult>(gridEntity);
        for (int i = 0; i < results.Length; i++)
        {
            GridBuildResult result = results[i];
            if (result.Success != 0)
            {
                Debug.Log(
                    "[ECS Grid Build] " + result.Type +
                    " succeeded for " + result.Kind +
                    " at " + result.Cell +
                    (result.AffectedCount > 1
                        ? ", affected " +
                          result.AffectedCount + " buildings"
                        : string.Empty) +
                    ".");
            }
            else
            {
                Debug.LogWarning(
                    "[ECS Grid Build] " + result.Type +
                    " failed for " + result.Kind +
                    " at " + result.Cell +
                    ": " + result.FailureReason + ".");
            }
        }

        results.Clear();
        query.Dispose();
    }
}
