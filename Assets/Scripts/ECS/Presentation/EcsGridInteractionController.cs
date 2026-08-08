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
    private static readonly Color InputPortPreviewColor =
        new Color(1f, 0.42f, 0.04f, 0.95f);
    private static readonly Color OutputPortPreviewColor =
        new Color(0.12f, 0.9f, 0.28f, 0.95f);

    private readonly List<int2> previewCells =
        new List<int2>(64);
    private readonly List<GameObject> previewCellObjects =
        new List<GameObject>(64);
    private readonly List<Transform> previewTriangles =
        new List<Transform>(64);
    private readonly List<int2> previewDisplayDirections =
        new List<int2>(64);
    private readonly List<int2> previewPortCells =
        new List<int2>(8);
    private readonly List<int2> previewPortDirections =
        new List<int2>(8);
    private readonly List<BuildingPortType> previewPortTypes =
        new List<BuildingPortType>(8);
    private readonly List<Transform> previewPortTriangles =
        new List<Transform>(8);

    private Camera inputCamera;
    private Transform previewRoot;
    private Material previewMaterial;
    private Material inputPortPreviewMaterial;
    private Material outputPortPreviewMaterial;
    private Mesh previewTriangleMesh;
    [SerializeField] private uint localPlayerId = 1;
    private PlayerCommandBus commandBus;
    private byte quarterTurns;
    private bool beltPathStarted;
    private bool horizontalFirst = true;
    private int2 beltPathStart;
    private bool simulatedHoverActive;
    private int2 simulatedHoverCell;
    private World cachedWorld;
    private EntityQuery gridQuery;
    private EntityQuery databaseQuery;
    private Entity gridEntity = Entity.Null;
    private Entity databaseEntity = Entity.Null;
    private BlobAssetReference<FactoryDatabaseBlob> databaseReference;
    private bool ecsCacheInitialized;
    private int cachedGridCount;
    private PlayerInputModeController inputMode;

    public BuildingKind SelectedKind { get; private set; } =
        BuildingKind.Belt;
    public BuildingLevelId SelectedBuildingLevel { get; private set; }
    public bool IsBeltPathStarted => beltPathStarted;
    public bool UsesHorizontalFirst => horizontalFirst;

    private void Awake()
    {
        inputCamera = GetComponent<Camera>();
        inputMode = FindFirstObjectByType<PlayerInputModeController>();
    }

    private void Update()
    {
        ConsumeBuildResults();
        if (inputMode != null && !inputMode.IsBuildMode)
        {
            beltPathStarted = false;
            HidePlacementPreview();
            return;
        }
        EnsureSelectedBuildingLevel();
        HandleBuildActions();
        if (inputMode != null && inputMode.BlocksWorldInput)
        {
            HidePlacementPreview();
            return;
        }
        UpdatePlacementPreview();

        if (inputMode != null && inputMode.PlacePressedThisFrame)
        {
            HandlePrimaryClick();
        }

        if (inputMode != null && inputMode.RemovePressedThisFrame)
        {
            HandleRemove(inputMode.RemoveBeltLineModifierActive);
        }
    }

    private void OnDisable()
    {
        HidePlacementPreview();
        ReleaseEcsCache();
    }

    private void OnDestroy()
    {
        ReleaseEcsCache();
        if (previewRoot != null)
        {
            Destroy(previewRoot.gameObject);
        }

        if (previewMaterial != null)
        {
            Destroy(previewMaterial);
        }

        if (inputPortPreviewMaterial != null)
        {
            Destroy(inputPortPreviewMaterial);
        }

        if (outputPortPreviewMaterial != null)
        {
            Destroy(outputPortPreviewMaterial);
        }

        if (previewTriangleMesh != null)
        {
            Destroy(previewTriangleMesh);
        }
    }

    private void HandleBuildActions()
    {
        if (inputMode != null && inputMode.RotatePressedThisFrame)
        {
            RotateSelectionOrToggleBeltPathOrder();
        }
    }

    public void RotateSelectionOrToggleBeltPathOrder()
    {
        if (SelectedKind == BuildingKind.Belt && beltPathStarted)
            horizontalFirst = !horizontalFirst;
        else
            quarterTurns = (byte)((quarterTurns + 3) % 4);
    }

    private void UpdatePlacementPreview()
    {
        World world;
        GridDefinition grid;
        int2 hoveredCell;
        if (simulatedHoverActive)
        {
            if (!TryGetGrid(out world, out _, out grid))
            {
                HidePlacementPreview();
                return;
            }

            hoveredCell = simulatedHoverCell;
        }
        else if (!TryRaycastGrid(
                     out world,
                     out grid,
                     out _,
                     out hoveredCell,
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
        previewPortCells.Clear();
        previewPortDirections.Clear();
        previewPortTypes.Clear();
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

        if (!TryGetSelectedBuilding(out FactoryBuildingBlob building))
        {
            previewCells.Add(hoveredCell);
            return;
        }

        int2 footprintSize = new int2(
            building.FootprintWidth,
            building.FootprintHeight);
        for (int y = 0; y < building.FootprintHeight; y++)
        for (int x = 0; x < building.FootprintWidth; x++)
            previewCells.Add(hoveredCell +
                EcsGridUtility.RotateBuildingCellOffset(
                    new int2(x, y),
                    footprintSize,
                    quarterTurns));
        if (SelectedKind == BuildingKind.Belt)
        {
            previewDisplayDirections.Add(
                EcsGridUtility.Rotate(
                    new int2(1, 0),
                    quarterTurns));
        }
        else
        {
            BuildPortPreview(
                hoveredCell,
                footprintSize,
                building);
        }

    }

    private void BuildPortPreview(
        int2 anchorCell,
        int2 footprintSize,
        in FactoryBuildingBlob building)
    {
        if (!TryGetDatabase(
                out BlobAssetReference<FactoryDatabaseBlob> reference))
        {
            return;
        }

        ref FactoryDatabaseBlob database = ref reference.Value;
        for (int i = 0; i < building.PortCount; i++)
        {
            FactoryBuildingPortBlob port =
                database.BuildingPorts[building.PortStart + i];
            previewPortCells.Add(anchorCell +
                EcsGridUtility.RotateBuildingCellOffset(
                    port.CellOffset,
                    footprintSize,
                    quarterTurns));
            previewPortDirections.Add(EcsGridUtility.Rotate(
                port.Direction,
                quarterTurns));
            previewPortTypes.Add(port.Type);
        }
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
        EnsurePreviewPortObjects(previewPortCells.Count);
        if (previewMaterial == null ||
            inputPortPreviewMaterial == null ||
            outputPortPreviewMaterial == null)
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
        for (int i = 0; i < previewPortTriangles.Count; i++)
        {
            Transform triangle = previewPortTriangles[i];
            bool active = i < previewPortCells.Count;
            triangle.gameObject.SetActive(active);
            if (!active)
            {
                continue;
            }

            float3 center = EcsGridUtility.CellToWorldCenter(
                previewPortCells[i],
                grid.Origin.y + 0.115f * cellSize,
                grid);
            triangle.position = new Vector3(
                center.x,
                center.y,
                center.z);
            int2 direction = previewPortDirections[i];
            float angle = -math.atan2(
                direction.y,
                direction.x) * math.TODEGREES;
            triangle.rotation = Quaternion.Euler(0f, angle, 0f);
            triangle.localScale = Vector3.one * cellSize * 1.5f;
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
        if (inputPortPreviewMaterial == null ||
            outputPortPreviewMaterial == null)
        {
            if (inputPortPreviewMaterial == null)
            {
                inputPortPreviewMaterial = CreatePreviewMaterial(
                    "ECS Input Port Preview Material",
                    InputPortPreviewColor);
            }
            if (outputPortPreviewMaterial == null)
            {
                outputPortPreviewMaterial = CreatePreviewMaterial(
                    "ECS Output Port Preview Material",
                    OutputPortPreviewColor);
            }
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

    private void EnsurePreviewPortObjects(int requiredCount)
    {
        if (previewRoot == null)
        {
            EnsurePreviewObjects(0);
        }

        while (previewPortTriangles.Count < requiredCount)
        {
            GameObject triangle = new GameObject(
                "Preview Building Port Triangle " +
                previewPortTriangles.Count);
            triangle.hideFlags = HideFlags.DontSave;
            triangle.layer = 2;
            triangle.transform.SetParent(previewRoot, false);
            triangle.AddComponent<MeshFilter>().sharedMesh =
                GetPreviewTriangleMesh();
            MeshRenderer renderer =
                triangle.AddComponent<MeshRenderer>();
            BuildingPortType type =
                previewPortTypes[previewPortTriangles.Count];
            renderer.sharedMaterial = type == BuildingPortType.Input
                ? inputPortPreviewMaterial
                : outputPortPreviewMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            previewPortTriangles.Add(triangle.transform);
        }

        for (int i = 0; i < requiredCount; i++)
        {
            MeshRenderer renderer =
                previewPortTriangles[i].GetComponent<MeshRenderer>();
            renderer.sharedMaterial =
                previewPortTypes[i] == BuildingPortType.Input
                    ? inputPortPreviewMaterial
                    : outputPortPreviewMaterial;
        }
    }

    private static Material CreatePreviewMaterial(
        string materialName,
        Color color)
    {
        Shader shader = Shader.Find(
                            "Universal Render Pipeline/Unlit") ??
                        Shader.Find("Unlit/Color") ??
                        Shader.Find("Sprites/Default");
        if (shader == null)
        {
            return null;
        }

        Material material = new Material(shader)
        {
            name = materialName,
            hideFlags = HideFlags.DontSave,
            renderQueue = (int)RenderQueue.Transparent
        };
        material.SetOverrideTag("RenderType", "Transparent");
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }
        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        }
        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat(
                "_DstBlend",
                (float)BlendMode.OneMinusSrcAlpha);
        }
        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        return material;
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

        for (int i = 0; i < previewPortTriangles.Count; i++)
        {
            previewPortTriangles[i].gameObject.SetActive(false);
        }
    }

    private void HandlePrimaryClick()
    {
        if (!TryRaycastGrid(
                out World world,
                out _,
                out _,
                out int2 cell,
                out _,
                out _,
                true))
        {
            return;
        }

        HandlePrimaryClickAtCell(cell);
    }

    public void SimulatePrimaryClick(int2 cell)
    {
        HandlePrimaryClickAtCell(cell);
    }

    public bool TryGetHoveredBuilding(out BuildingRuntimeId runtimeId)
    {
        runtimeId = default;
        if (!TryRaycastGrid(out World world, out _, out _, out int2 cell,
                out _, out bool isInside, false) || !isInside)
            return false;
        GridOccupancyIndexSystem occupancy =
            world.GetExistingSystemManaged<GridOccupancyIndexSystem>();
        if (occupancy == null || !occupancy.TryGetOccupant(cell, out Entity entity))
            return false;
        EntityManager manager = world.EntityManager;
        if (!manager.Exists(entity) || !manager.HasComponent<ItemContainerIdentity>(entity))
            return false;
        runtimeId = new BuildingRuntimeId(
            manager.GetComponentData<ItemContainerIdentity>(entity).RuntimeId);
        return runtimeId.IsValid;
    }

    private void HandlePrimaryClickAtCell(int2 cell)
    {
        if (!TryGetGrid(
                out World world,
                out Entity gridEntity,
                out GridDefinition grid))
        {
            return;
        }

        Entity occupant = Entity.Null;
        GridOccupancyIndexSystem occupancySystem =
            world.GetExistingSystemManaged<
                GridOccupancyIndexSystem>();
        bool isInside = EcsGridUtility.Contains(grid, cell);
        bool isOccupied =
            isInside &&
            occupancySystem != null &&
            occupancySystem.TryGetOccupant(
                cell,
                out occupant);

        Debug.Log(
            "[ECS Grid Raycast] Cell=(" + cell.x + ", " + cell.y + ")" +
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
                    RequestId = 0,
                    Type =
                        GridBuildCommandType.PlaceBeltPath,
                    Kind = BuildingKind.Belt,
                    BuildingLevel = SelectedBuildingLevel,
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
                RequestId = 0,
                Type = GridBuildCommandType.Place,
                Kind = SelectedKind,
                BuildingLevel = SelectedBuildingLevel,
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
                out _,
                out int2 cell,
                out _,
                out bool isInside,
                false) ||
            !isInside)
        {
            return;
        }

        HandleRemoveAtCell(cell, removeBeltLine);
    }

    public void SimulateRemove(int2 cell, bool removeBeltLine)
    {
        HandleRemoveAtCell(cell, removeBeltLine);
    }

    private void HandleRemoveAtCell(
        int2 cell,
        bool removeBeltLine)
    {
        if (!TryGetGrid(
                out World world,
                out Entity gridEntity,
                out _))
        {
            return;
        }

        beltPathStarted = false;
        Enqueue(
            world,
            gridEntity,
            new GridBuildCommand
            {
                RequestId = 0,
                Type = removeBeltLine
                    ? GridBuildCommandType.RemoveBeltLine
                    : GridBuildCommandType.Remove,
                Kind = removeBeltLine
                    ? BuildingKind.Belt
                    : SelectedKind,
                BuildingLevel = default,
                StartCell = cell,
                EndCell = cell,
                QuarterTurns = 0,
                HorizontalFirst = 0
            });
    }

    public void SimulateHover(int2 cell)
    {
        simulatedHoverActive = true;
        simulatedHoverCell = cell;
    }

    public void StopSimulatedHover()
    {
        simulatedHoverActive = false;
    }

    public void CancelBeltPath()
    {
        beltPathStarted = false;
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
        world = null;
        grid = default;
        gridEntity = Entity.Null;
        cell = default;
        hitPoint = default;
        isInside = false;
        if (!TryGetGrid(
                out world,
                out gridEntity,
                out grid))
        {
            if (logFailure)
            {
                if (world == null || !world.IsCreated)
                {
                    Debug.LogWarning(
                        "[ECS Grid Raycast] The default ECS World is unavailable.");
                }
                else
                {
                    Debug.LogWarning(
                        "[ECS Grid Raycast] Exactly one GridDefinition is required; found " +
                        cachedGridCount + ".");
                }
            }

            return false;
        }

        Ray ray = inputCamera.ScreenPointToRay(
            Input.mousePosition);

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

    private void EnsureSelectedBuildingLevel()
    {
        if (!SelectedBuildingLevel.IsValid)
            SelectBuildingMenuIndex(0);
    }

    public bool TrySelectBuildingLevel(BuildingLevelId buildingLevel)
    {
        if (!TryGetDatabase(
                out BlobAssetReference<FactoryDatabaseBlob> reference))
        {
            return false;
        }

        ref FactoryDatabaseBlob database = ref reference.Value;
        for (int i = 0; i < database.BuildingLevelMenu.Length; i++)
        {
            if (database.BuildingLevelMenu[i] != buildingLevel)
            {
                continue;
            }

            SelectBuildingMenuIndex(i);
            return SelectedBuildingLevel == buildingLevel;
        }

        return false;
    }

    private void SelectBuildingMenuIndex(int index)
    {
        if (!TryGetDatabase(out BlobAssetReference<FactoryDatabaseBlob> reference))
            return;
        ref FactoryDatabaseBlob database = ref reference.Value;
        if (index < 0 || index >= database.BuildingLevelMenu.Length)
            return;
        BuildingLevelId id = database.BuildingLevelMenu[index];
        if (!FactoryDatabaseUtility.TryGetBuildingLevel(
                ref database,
                id,
                out _,
                out FactoryBuildingBlob building))
            return;
        SelectedBuildingLevel = id;
        SelectedKind = building.Kind;
    }

    private bool TryGetSelectedBuilding(out FactoryBuildingBlob building)
    {
        building = default;
        if (!TryGetDatabase(out BlobAssetReference<FactoryDatabaseBlob> reference))
            return false;
        ref FactoryDatabaseBlob database = ref reference.Value;
        return FactoryDatabaseUtility.TryGetBuildingLevel(
            ref database,
            SelectedBuildingLevel,
            out _,
            out building);
    }

    private bool TryGetDatabase(
        out BlobAssetReference<FactoryDatabaseBlob> reference)
    {
        reference = default;
        if (!EnsureEcsCache())
        {
            return false;
        }

        EntityManager entityManager = cachedWorld.EntityManager;
        if (databaseEntity == Entity.Null ||
            !entityManager.Exists(databaseEntity) ||
            !entityManager.HasComponent<FactoryDatabase>(databaseEntity) ||
            !databaseReference.IsCreated)
        {
            RefreshDatabaseSingleton();
        }

        reference = databaseReference;
        return databaseEntity != Entity.Null && reference.IsCreated;
    }

    private bool TryGetGrid(
        out World world,
        out Entity currentGridEntity,
        out GridDefinition grid)
    {
        world = null;
        currentGridEntity = Entity.Null;
        grid = default;
        if (!EnsureEcsCache())
        {
            return false;
        }

        world = cachedWorld;
        EntityManager entityManager = cachedWorld.EntityManager;
        if (gridEntity == Entity.Null ||
            !entityManager.Exists(gridEntity) ||
            !entityManager.HasComponent<GridDefinition>(gridEntity))
        {
            RefreshGridSingleton();
        }

        if (gridEntity == Entity.Null)
        {
            return false;
        }

        currentGridEntity = gridEntity;
        grid = entityManager.GetComponentData<GridDefinition>(gridEntity);
        return true;
    }

    private bool EnsureEcsCache()
    {
        World currentWorld = World.DefaultGameObjectInjectionWorld;
        if (currentWorld == null || !currentWorld.IsCreated)
        {
            ReleaseEcsCache();
            return false;
        }

        if (ecsCacheInitialized && cachedWorld == currentWorld)
        {
            return true;
        }

        ReleaseEcsCache();
        cachedWorld = currentWorld;
        EntityManager entityManager = currentWorld.EntityManager;
        gridQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<GridDefinition>());
        databaseQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<FactoryDatabase>());
        ecsCacheInitialized = true;
        RefreshGridSingleton();
        RefreshDatabaseSingleton();
        return true;
    }

    private void RefreshGridSingleton()
    {
        gridEntity = Entity.Null;
        cachedGridCount = gridQuery.CalculateEntityCount();
        if (cachedGridCount == 1)
        {
            gridEntity = gridQuery.GetSingletonEntity();
        }
    }

    private void RefreshDatabaseSingleton()
    {
        databaseEntity = Entity.Null;
        databaseReference = default;
        if (databaseQuery.CalculateEntityCount() != 1)
        {
            return;
        }

        databaseEntity = databaseQuery.GetSingletonEntity();
        databaseReference = cachedWorld.EntityManager
            .GetComponentData<FactoryDatabase>(databaseEntity)
            .Value;
    }

    private void ReleaseEcsCache()
    {
        if (ecsCacheInitialized &&
            cachedWorld != null &&
            cachedWorld.IsCreated)
        {
            gridQuery.Dispose();
            databaseQuery.Dispose();
        }

        ecsCacheInitialized = false;
        cachedWorld = null;
        gridEntity = Entity.Null;
        databaseEntity = Entity.Null;
        databaseReference = default;
        cachedGridCount = 0;
    }

    private void Enqueue(
        World world,
        Entity gridEntity,
        GridBuildCommand command)
    {
        commandBus ??= PlayerCommandRuntimeServices.GetOrCreateBus(
            world, new PlayerId { Value = localPlayerId });
        commandBus.Submit(new GridBuildPlayerCommand
        {
            Type = command.Type,
            Kind = command.Kind,
            BuildingLevel = command.BuildingLevel,
            StartCell = command.StartCell,
            EndCell = command.EndCell,
            QuarterTurns = command.QuarterTurns,
            HorizontalFirst = command.HorizontalFirst
        });
    }

    private void ConsumeBuildResults()
    {
        if (!TryGetGrid(
                out World world,
                out Entity currentGridEntity,
                out _))
        {
            return;
        }

        EntityManager entityManager = world.EntityManager;
        if (!entityManager.HasBuffer<GridBuildResult>(currentGridEntity))
        {
            return;
        }

        DynamicBuffer<GridBuildResult> results =
            entityManager.GetBuffer<GridBuildResult>(currentGridEntity);
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
    }
}
