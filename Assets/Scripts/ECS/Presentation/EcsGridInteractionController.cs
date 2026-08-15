using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
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

    private readonly List<GridCell> previewCells =
        new List<GridCell>(64);
    private readonly List<GameObject> previewCellObjects =
        new List<GameObject>(64);
    private readonly List<Transform> previewTriangles =
        new List<Transform>(64);
    private readonly List<int2> previewDisplayDirections =
        new List<int2>(64);
    private readonly List<int> previewRampStartHeightUnits =
        new List<int>(64);
    private readonly List<RampRegistrySystem.Record> previewRampBeltPath =
        new List<RampRegistrySystem.Record>(64);
    private readonly List<GridCell> previewPortCells =
        new List<GridCell>(8);
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
    private Mesh previewRampMesh;
    private Mesh previewCubeMesh;
    [SerializeField] private uint localPlayerId = 1;
    private PlayerCommandBus commandBus;
    private byte quarterTurns;
    private bool beltPathStarted;
    private bool rampBeltPathStarted;
    private bool foundationAreaStarted;
    private bool rampLineStarted;
    private bool horizontalFirst = true;
    private GridCell beltPathStart;
    private GridCell rampBeltPathStart;
    private GridCell foundationAreaStart;
    private GridCell rampLineStart;
    private bool simulatedHoverActive;
    private GridCell simulatedHoverCell;
    private World cachedWorld;
    private EntityQuery gridQuery;
    private EntityQuery databaseQuery;
    private EntityQuery physicsWorldQuery;
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
    public bool IsRampBeltPathStarted => rampBeltPathStarted;
    public bool IsFoundationAreaStarted => foundationAreaStarted;
    public bool IsRampLineStarted => rampLineStarted;
    public bool UsesHorizontalFirst => horizontalFirst;

    public bool TrySubmitRampCommand(
        GridBuildCommandType type,
        GridCell cell,
        int startHeightUnits,
        int riseHeightUnits,
        int2 direction,
        BuildingLevelId beltLevel = default)
    {
        if ((type == GridBuildCommandType.PlaceRampFoundation &&
             !RampUtility.IsAllowedRise(riseHeightUnits)) ||
            riseHeightUnits < sbyte.MinValue || riseHeightUnits > sbyte.MaxValue ||
            !TryGetGrid(out World world, out Entity currentGrid, out _))
            return false;

        Enqueue(world, currentGrid, new GridBuildCommand
        {
            Type = type,
            Kind = type == GridBuildCommandType.PlaceRampBelt ||
                   type == GridBuildCommandType.RemoveRampBelt
                ? BuildingKind.Belt
                : BuildingKind.Foundation,
            BuildingLevel = beltLevel,
            StartCell = cell,
            EndCell = cell,
            QuarterTurns = EcsGridUtility.QuarterTurnsFromDirection(direction),
            RampStartHeightUnits = startHeightUnits,
            RampRiseHeightUnits = (sbyte)riseHeightUnits
        });
        return true;
    }

    private void Awake()
    {
        inputCamera = GetComponent<Camera>();
        inputMode = FindFirstObjectByType<PlayerInputModeController>();
    }

    private void Update()
    {
        ConsumeBuildResults();
        bool usePlayerInput = !simulatedHoverActive;
        if (usePlayerInput &&
            inputMode != null &&
            inputMode.IsDemolitionMode)
        {
            CancelPlacementGesture();
            HidePlacementPreview();
            if (!inputMode.BlocksWorldInput &&
                inputMode.PrimaryPointerPressedThisFrame)
            {
                HandleRemove(inputMode.RemoveBeltLineModifierActive);
            }
            return;
        }
        if (usePlayerInput &&
            inputMode != null &&
            !inputMode.IsBuildMode)
        {
            CancelPlacementGesture();
            HidePlacementPreview();
            return;
        }
        EnsureSelectedBuildingLevel();
        if (usePlayerInput)
            HandleBuildActions();
        if (usePlayerInput &&
            inputMode != null &&
            inputMode.BlocksWorldInput)
        {
            HidePlacementPreview();
            return;
        }
        UpdatePlacementPreview();

        if (usePlayerInput &&
            inputMode != null &&
            inputMode.PlacePressedThisFrame)
        {
            HandlePrimaryClick();
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
        if (previewRampMesh != null)
        {
            Destroy(previewRampMesh);
        }
    }

    private void HandleBuildActions()
    {
        if (inputMode != null && inputMode.CancelPressedThisFrame)
        {
            CancelPlacementGesture();
            return;
        }
        if (inputMode != null && inputMode.RotatePressedThisFrame)
        {
            RotateSelectionOrToggleBeltPathOrder();
        }
    }

    public void RotateSelectionOrToggleBeltPathOrder()
    {
        if (SelectedKind == BuildingKind.Foundation)
            return;
        if (SelectedKind == BuildingKind.Belt && beltPathStarted)
            horizontalFirst = !horizontalFirst;
        else
            quarterTurns = (byte)((quarterTurns + 3) % 4);
    }

    private void UpdatePlacementPreview()
    {
        World world;
        GridDefinition grid;
        GridCell hoveredCell;
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
                     IsSurfacePlacementKind(),
                     false))
        {
            HidePlacementPreview();
            return;
        }

        if (SelectedKind == BuildingKind.Foundation &&
            foundationAreaStarted)
        {
            hoveredCell.Level = foundationAreaStart.Level;
        }
        if (SelectedKind == BuildingKind.RampFoundation &&
            rampLineStarted)
        {
            hoveredCell.Level = rampLineStart.Level;
        }
        BuildPreviewCells(hoveredCell);
        GridOccupancyIndexSystem occupancySystem =
            world.GetExistingSystemManaged<
                GridOccupancyIndexSystem>();
        SurfaceRegistrySystem surfaceRegistry =
            world.GetExistingSystemManaged<SurfaceRegistrySystem>();
        RampRegistrySystem rampRegistry =
            world.GetExistingSystemManaged<RampRegistrySystem>();
        bool canPlace = SelectedKind == BuildingKind.Foundation
            ? surfaceRegistry != null && surfaceRegistry.IsReady
            : SelectedKind == BuildingKind.RampFoundation
                ? rampRegistry != null && rampRegistry.IsReady &&
                  occupancySystem != null && occupancySystem.IsReady &&
                  occupancySystem.ConflictCount == 0
                : occupancySystem != null && occupancySystem.IsReady &&
                  occupancySystem.ConflictCount == 0;
        for (int i = 0; i < previewCells.Count; i++)
        {
            GridCell cell = previewCells[i];
            if (SelectedKind == BuildingKind.Foundation)
            {
                if (cell.Level < 0 || surfaceRegistry == null ||
                    !surfaceRegistry.IsReady ||
                    surfaceRegistry.HasFoundationVoxel(cell))
                    canPlace = false;
            }
            else if (SelectedKind == BuildingKind.RampFoundation)
            {
                if (rampRegistry == null || rampRegistry.ContainsCell(cell) ||
                    occupancySystem == null ||
                    occupancySystem.TryGetOccupant(cell, out _))
                    canPlace = false;
            }
            else if (SelectedKind == BuildingKind.Belt &&
                     TryGetRamp(world, cell, out RampRegistrySystem.Record ramp))
            {
                int2 travel = i < previewDisplayDirections.Count
                    ? previewDisplayDirections[i]
                    : EcsGridUtility.Rotate(new int2(1, 0), quarterTurns);
                if (ramp.BeltEntity != Entity.Null ||
                    !RampUtility.IsTravelDirectionAllowed(
                        ramp.Connector, travel))
                {
                    canPlace = false;
                }
            }
            else if (!HasSurface(world, grid, cell) ||
                occupancySystem == null ||
                occupancySystem.TryGetOccupant(cell, out _))
            {
                canPlace = false;
            }
        }

        ShowPlacementPreview(grid, canPlace);
    }

    private void BuildPreviewCells(GridCell hoveredCell)
    {
        previewCells.Clear();
        previewDisplayDirections.Clear();
        previewRampStartHeightUnits.Clear();
        previewPortCells.Clear();
        previewPortDirections.Clear();
        previewPortTypes.Clear();
        if (SelectedKind == BuildingKind.Foundation)
        {
            GridCell start = foundationAreaStarted
                ? foundationAreaStart
                : hoveredCell;
            int minX = math.min(start.X, hoveredCell.X);
            int maxX = math.max(start.X, hoveredCell.X);
            int minZ = math.min(start.Z, hoveredCell.Z);
            int maxZ = math.max(start.Z, hoveredCell.Z);
            long count = ((long)maxX - minX + 1L) *
                         ((long)maxZ - minZ + 1L);
            if (count > GridBuildCommandSystem.MaxFoundationAreaCells)
                return;
            for (long z = minZ; z <= maxZ; z++)
            for (long x = minX; x <= maxX; x++)
                previewCells.Add(new GridCell(
                    (int)x,
                    start.Level,
                    (int)z));
            return;
        }
        if (SelectedKind == BuildingKind.RampFoundation)
        {
            if (!TryGetSelectedBuildingLevel(out FactoryBuildingLevelBlob rampLevel))
                return;
            BuildRampLine(
                rampLineStarted ? rampLineStart : hoveredCell,
                hoveredCell,
                rampLineStarted,
                EcsGridUtility.Rotate(new int2(1, 0), quarterTurns),
                rampLevel.RampRiseHeightUnits,
                previewCells,
                previewDisplayDirections,
                previewRampStartHeightUnits);
            return;
        }
        if (SelectedKind == BuildingKind.Belt && rampBeltPathStarted)
        {
            if (!TryResolveRampPathDirection(
                    rampBeltPathStart, hoveredCell, out int2 direction) ||
                cachedWorld == null || !cachedWorld.IsCreated)
                return;
            RampRegistrySystem registry = cachedWorld
                .GetExistingSystemManaged<RampRegistrySystem>();
            if (registry == null || !registry.TryBuildBeltPath(
                    rampBeltPathStart,
                    hoveredCell,
                    direction,
                    previewRampBeltPath,
                    out _))
                return;
            for (int i = 0; i < previewRampBeltPath.Count; i++)
            {
                previewCells.Add(previewRampBeltPath[i].Connector.Cell);
                previewDisplayDirections.Add(direction);
            }
            return;
        }
        if (SelectedKind == BuildingKind.Belt &&
            TryGetRamp(cachedWorld, hoveredCell, out _))
        {
            previewCells.Add(hoveredCell);
            previewDisplayDirections.Add(EcsGridUtility.Rotate(
                new int2(1, 0), quarterTurns));
            return;
        }
        if (SelectedKind == BuildingKind.Belt &&
            beltPathStarted)
        {
            GridCell corner = horizontalFirst
                ? new GridCell(hoveredCell.X, hoveredCell.Level, beltPathStart.Z)
                : new GridCell(beltPathStart.X, hoveredCell.Level, hoveredCell.Z);
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
        GridCell anchorCell,
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
        GridCell from,
        GridCell to,
        bool includeStart)
    {
        int2 delta = to.Horizontal - from.Horizontal;
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

    public static void BuildRampLine(
        GridCell start,
        GridCell end,
        bool useEndDirection,
        int2 fallbackDirection,
        int riseHeightUnits,
        List<GridCell> cells,
        List<int2> directions,
        List<int> startHeightUnits)
    {
        cells.Clear();
        directions.Clear();
        startHeightUnits.Clear();
        if (!RampUtility.IsAllowedRise(riseHeightUnits))
            return;

        int2 delta = end.Horizontal - start.Horizontal;
        int2 direction;
        int length;
        if (!useEndDirection || math.all(delta == int2.zero))
        {
            direction = EcsGridUtility.SanitizeDirection(fallbackDirection);
            length = 0;
        }
        else if (math.abs(delta.x) >= math.abs(delta.y))
        {
            direction = new int2(delta.x >= 0 ? 1 : -1, 0);
            length = math.abs(delta.x);
        }
        else
        {
            direction = new int2(0, delta.y >= 0 ? 1 : -1);
            length = math.abs(delta.y);
        }

        int baseHeight = checked(start.Level * GridHeight.UnitsPerLayer);
        for (int i = 0; i <= length; i++)
        {
            int height = checked(baseHeight + i * riseHeightUnits);
            GridCell cell = start + direction * i;
            cell.Level = SurfaceChunkUtility.FloorDiv(
                height,
                GridHeight.UnitsPerLayer);
            cells.Add(cell);
            directions.Add(direction);
            startHeightUnits.Add(height);
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
                    previewCells[i + 1].Horizontal -
                    previewCells[i].Horizontal;
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
                    previewCells[i].Horizontal -
                    previewCells[i - 1].Horizontal;
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
        WorldGridConfig worldGrid = GetWorldGridConfig();
        bool isFoundationPreview =
            SelectedKind == BuildingKind.Foundation;
        bool isRampPreview =
            SelectedKind == BuildingKind.RampFoundation;
        float layerHeight = math.max(
            math.EPSILON,
            worldGrid.LayerHeight);
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
                worldGrid);
            MeshFilter meshFilter = previewCell.GetComponent<MeshFilter>();
            meshFilter.sharedMesh = isRampPreview
                ? GetPreviewRampMesh()
                : previewCubeMesh;
            if (isRampPreview)
            {
                int2 direction = previewDisplayDirections[i];
                TryGetSelectedBuildingLevel(out FactoryBuildingLevelBlob rampLevel);
                center.y = new GridHeight(
                    previewRampStartHeightUnits[i]).ToWorldY(worldGrid);
                previewCell.transform.position = new Vector3(
                    center.x, center.y, center.z);
                previewCell.transform.rotation = Quaternion.Euler(
                    0f,
                    -math.atan2(direction.y, direction.x) * math.TODEGREES,
                    0f);
                previewCell.transform.localScale = new Vector3(
                    cellSize,
                    math.abs(rampLevel.RampRiseHeightUnits) /
                    (float)GridHeight.UnitsPerLayer * layerHeight,
                    cellSize);
            }
            else if (SelectedKind == BuildingKind.Belt &&
                     TryGetRamp(cachedWorld, previewCells[i],
                         out RampRegistrySystem.Record ramp))
            {
                int2 travel = previewDisplayDirections[i];
                bool uphill = math.all(
                    travel == ramp.Connector.UphillDirection);
                float signedRise = (uphill ? 1f : -1f) *
                    (ramp.Connector.HighHeight.ToWorldY(worldGrid) -
                     ramp.Connector.LowHeight.ToWorldY(worldGrid));
                float angle = math.atan2(
                    signedRise,
                    math.max(math.EPSILON, cellSize));
                quaternion rampBeltRotation = math.mul(
                    EcsGridUtility.RotationFromQuarterTurns(
                        EcsGridUtility.QuarterTurnsFromDirection(travel)),
                    quaternion.RotateZ(angle));
                center = RampUtility.GetSurfaceCenter(
                    ramp.Connector, worldGrid);
                previewCell.transform.position = new Vector3(
                    center.x, center.y, center.z);
                previewCell.transform.rotation = new Quaternion(
                    rampBeltRotation.value.x,
                    rampBeltRotation.value.y,
                    rampBeltRotation.value.z,
                    rampBeltRotation.value.w);
                previewCell.transform.localScale = new Vector3(
                    RampUtility.GetSlopeLength(ramp.Connector, worldGrid) * 0.9f,
                    0.1f,
                    cellSize * 0.9f);
            }
            else
            {
                center.y += isFoundationPreview
                    ? -layerHeight * 0.5f
                    : 0.055f;
                previewCell.transform.position = new Vector3(
                    center.x,
                    center.y,
                    center.z);
                previewCell.transform.rotation = Quaternion.identity;
                previewCell.transform.localScale = isFoundationPreview
                    ? new Vector3(cellSize, layerHeight, cellSize)
                    : new Vector3(
                        cellSize * 0.9f,
                        0.1f,
                        cellSize * 0.9f);
            }

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
                GetWorldGridConfig());
            center.y += 0.115f * cellSize;
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
            if (previewCubeMesh == null)
                previewCubeMesh = previewCell.GetComponent<MeshFilter>().sharedMesh;
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

    private Mesh GetPreviewRampMesh()
    {
        if (previewRampMesh != null)
            return previewRampMesh;
        previewRampMesh = new Mesh
        {
            name = "ECS Preview Ramp",
            hideFlags = HideFlags.DontSave,
            vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f),
                new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f),
                new Vector3(0.5f, 1f, -0.5f),
                new Vector3(0.5f, 1f, 0.5f)
            },
            triangles = new[]
            {
                0, 2, 3, 0, 3, 1,
                2, 4, 5, 2, 5, 3,
                0, 1, 5, 0, 5, 4,
                0, 4, 2,
                1, 3, 5
            }
        };
        previewRampMesh.RecalculateNormals();
        previewRampMesh.RecalculateBounds();
        return previewRampMesh;
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
                out GridCell cell,
                out _,
                out _,
                IsSurfacePlacementKind(),
                true))
        {
            return;
        }

        HandlePrimaryClickAtCell(cell);
    }

    public void SimulatePrimaryClick(int2 cell)
    {
        SimulatePrimaryClick(GridCell.LevelZero(cell));
    }

    public void SimulatePrimaryClick(GridCell cell) =>
        HandlePrimaryClickAtCell(cell);

    public bool TryGetHoveredBuilding(out BuildingRuntimeId runtimeId)
    {
        runtimeId = default;
        if (!TryRaycastGrid(out World world, out _, out _, out GridCell cell,
                out _, out bool isInside, false, false) || !isInside)
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

    private void HandlePrimaryClickAtCell(GridCell cell)
    {
        if (SelectedKind == BuildingKind.Foundation &&
            foundationAreaStarted)
        {
            cell.Level = foundationAreaStart.Level;
        }
        if (SelectedKind == BuildingKind.RampFoundation &&
            rampLineStarted)
        {
            cell.Level = rampLineStart.Level;
        }
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
        bool isInside = SelectedKind == BuildingKind.Foundation
            || SelectedKind == BuildingKind.RampFoundation
            ? cell.Level >= 0
            : HasSurface(world, grid, cell) ||
              SelectedKind == BuildingKind.Belt &&
              TryGetRamp(world, cell, out _);
        bool isOccupied =
            isInside &&
            occupancySystem != null &&
            occupancySystem.TryGetOccupant(
                cell,
                out occupant);

        Debug.Log(
            "[ECS Grid Raycast] Cell=(" + cell.X + ", L" + cell.Level +
            ", " + cell.Z + ")" +
            ", Inside=" + isInside +
            ", Occupied=" + isOccupied +
            (isOccupied ? ", Entity=" + occupant : string.Empty) +
            ".");

        if (!isInside)
        {
            return;
        }

        if (SelectedKind == BuildingKind.Foundation)
        {
            if (!foundationAreaStarted)
            {
                foundationAreaStart = cell;
                foundationAreaStarted = true;
                return;
            }
            Enqueue(
                world,
                gridEntity,
                new GridBuildCommand
                {
                    RequestId = 0,
                    Type = GridBuildCommandType.PlaceFoundationArea,
                    Kind = BuildingKind.Foundation,
                    BuildingLevel = SelectedBuildingLevel,
                    StartCell = foundationAreaStart,
                    EndCell = cell
                });
            foundationAreaStarted = false;
            return;
        }

        if (SelectedKind == BuildingKind.RampFoundation)
        {
            if (!TryGetSelectedBuildingLevel(out FactoryBuildingLevelBlob rampLevel) ||
                !RampUtility.IsAllowedRise(rampLevel.RampRiseHeightUnits))
                return;
            if (!rampLineStarted)
            {
                rampLineStart = cell;
                rampLineStarted = true;
                return;
            }

            cell.Level = rampLineStart.Level;
            BuildRampLine(
                rampLineStart,
                cell,
                true,
                EcsGridUtility.Rotate(new int2(1, 0), quarterTurns),
                rampLevel.RampRiseHeightUnits,
                previewCells,
                previewDisplayDirections,
                previewRampStartHeightUnits);
            if (previewCells.Count == 0 ||
                previewCells.Count > GridBuildCommandSystem.MaxFoundationAreaCells)
            {
                rampLineStarted = false;
                return;
            }
            for (int i = 0; i < previewCells.Count; i++)
            {
                Enqueue(
                    world,
                    gridEntity,
                    new GridBuildCommand
                    {
                        Type = GridBuildCommandType.PlaceRampFoundation,
                        Kind = BuildingKind.RampFoundation,
                        BuildingLevel = SelectedBuildingLevel,
                        StartCell = previewCells[i],
                        EndCell = previewCells[i],
                        QuarterTurns = EcsGridUtility.QuarterTurnsFromDirection(
                            previewDisplayDirections[i]),
                        RampStartHeightUnits = previewRampStartHeightUnits[i],
                        RampRiseHeightUnits = rampLevel.RampRiseHeightUnits
                    });
            }
            rampLineStarted = false;
            return;
        }

        if (SelectedKind == BuildingKind.Belt)
        {
            bool targetIsRamp = TryGetRamp(
                world, cell, out RampRegistrySystem.Record ramp);
            if (beltPathStarted && targetIsRamp)
            {
                beltPathStarted = false;
                Debug.LogWarning(
                    "[ECS Grid Build] A belt path cannot mix planar and " +
                    "ramp cells.");
                return;
            }
            if (rampBeltPathStarted)
            {
                if (!targetIsRamp)
                {
                    Debug.LogWarning(
                        "[ECS Grid Build] A belt path cannot mix ramp and " +
                        "planar cells.");
                    rampBeltPathStarted = false;
                    return;
                }
                if (!TryResolveRampPathDirection(
                        rampBeltPathStart, cell, out int2 direction))
                {
                    Debug.LogWarning(
                        "[ECS Grid Build] Ramp belt endpoints must be " +
                        "axis-aligned.");
                    rampBeltPathStarted = false;
                    return;
                }
                RampRegistrySystem registry = world
                    .GetExistingSystemManaged<RampRegistrySystem>();
                GridBuildFailureReason failure =
                    GridBuildFailureReason.RampPathMustStayOnRamp;
                if (registry == null || !registry.TryBuildBeltPath(
                        rampBeltPathStart,
                        cell,
                        direction,
                        previewRampBeltPath,
                        out failure))
                {
                    Debug.LogWarning(
                        "[ECS Grid Build] Invalid ramp belt path: " + failure + ".");
                    rampBeltPathStarted = false;
                    return;
                }
                Enqueue(
                    world,
                    gridEntity,
                    new GridBuildCommand
                    {
                        Type = GridBuildCommandType.PlaceRampBeltPath,
                        Kind = BuildingKind.Belt,
                        BuildingLevel = SelectedBuildingLevel,
                        StartCell = rampBeltPathStart,
                        EndCell = cell,
                        QuarterTurns = EcsGridUtility.QuarterTurnsFromDirection(
                            direction)
                    });
                rampBeltPathStarted = false;
                return;
            }
            if (targetIsRamp)
            {
                if (ramp.BeltEntity != Entity.Null)
                {
                    Debug.LogWarning(
                        "[ECS Grid Build] The ramp already has a belt.");
                    return;
                }
                rampBeltPathStart = cell;
                rampBeltPathStarted = true;
                beltPathStarted = false;
                return;
            }

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
                out GridCell cell,
                out _,
                out bool isInside,
                false,
                false) ||
            !isInside)
        {
            return;
        }

        HandleRemoveAtCell(cell, removeBeltLine);
    }

    public void SimulateRemove(int2 cell, bool removeBeltLine)
    {
        SimulateRemove(GridCell.LevelZero(cell), removeBeltLine);
    }

    public void SimulateRemove(GridCell cell, bool removeBeltLine) =>
        HandleRemoveAtCell(cell, removeBeltLine);

    private void HandleRemoveAtCell(
        GridCell cell,
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
        rampBeltPathStarted = false;
        bool removesRampBelt = SelectedKind == BuildingKind.Belt &&
                               TryGetRamp(world, cell, out _);
        Enqueue(
            world,
            gridEntity,
            new GridBuildCommand
            {
                RequestId = 0,
                Type = removesRampBelt
                    ? GridBuildCommandType.RemoveRampBelt
                    : removeBeltLine
                    ? GridBuildCommandType.RemoveBeltLine
                    : SelectedKind == BuildingKind.Foundation
                        ? GridBuildCommandType.RemoveFoundation
                        : SelectedKind == BuildingKind.RampFoundation
                            ? GridBuildCommandType.RemoveRampFoundation
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
        SimulateHover(GridCell.LevelZero(cell));
    }

    public void SimulateHover(GridCell cell)
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
        rampBeltPathStarted = false;
    }

    public void CancelPlacementGesture()
    {
        beltPathStarted = false;
        rampBeltPathStarted = false;
        foundationAreaStarted = false;
        rampLineStarted = false;
    }

    private bool TryResolveRampPathDirection(
        GridCell start,
        GridCell end,
        out int2 direction)
    {
        int2 delta = end.Horizontal - start.Horizontal;
        if (delta.x == 0 && delta.y == 0)
        {
            direction = EcsGridUtility.Rotate(new int2(1, 0), quarterTurns);
            return true;
        }
        if (delta.x != 0 && delta.y == 0)
        {
            direction = new int2(delta.x > 0 ? 1 : -1, 0);
            return true;
        }
        if (delta.y != 0 && delta.x == 0)
        {
            direction = new int2(0, delta.y > 0 ? 1 : -1);
            return true;
        }
        direction = default;
        return false;
    }

    private bool IsSurfacePlacementKind() =>
        SelectedKind == BuildingKind.Foundation ||
        SelectedKind == BuildingKind.RampFoundation;

    private bool TryRaycastGrid(
        out World world,
        out GridDefinition grid,
        out Entity gridEntity,
        out GridCell cell,
        out Vector3 hitPoint,
        out bool isInside,
        bool foundationPlacement,
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

        if (foundationPlacement &&
            (foundationAreaStarted || rampLineStarted))
        {
            WorldGridConfig foundationGrid = GetWorldGridConfig();
            int foundationLevel = foundationAreaStarted
                ? foundationAreaStart.Level
                : rampLineStart.Level;
            Plane foundationPlane = new Plane(
                Vector3.up,
                new Vector3(
                    0f,
                    EcsGridUtility.LevelToWorldY(
                        foundationLevel,
                        foundationGrid),
                    0f));
            if (!foundationPlane.Raycast(ray, out float planeDistance))
                return false;
            hitPoint = ray.GetPoint(planeDistance);
            cell = EcsGridUtility.WorldToCell(
                new float3(hitPoint.x, hitPoint.y, hitPoint.z),
                foundationGrid);
            cell.Level = foundationLevel;
            isInside = true;
            return true;
        }

        GridLayerViewState layerView = GetLayerViewState();
        if (layerView.PickingMode == GridLayerPickingMode.SelectedLevel)
        {
            WorldGridConfig worldGrid = GetWorldGridConfig();
            Plane selectedPlane = new Plane(
                Vector3.up,
                new Vector3(
                    0f,
                    EcsGridUtility.LevelToWorldY(
                        layerView.SelectedLevel,
                        worldGrid),
                    0f));
            if (!selectedPlane.Raycast(ray, out float selectedDistance))
                return false;
            hitPoint = ray.GetPoint(selectedDistance);
            cell = EcsGridUtility.WorldToCell(
                new float3(hitPoint.x, hitPoint.y, hitPoint.z),
                worldGrid);
            cell.Level = layerView.SelectedLevel;
            isInside = foundationPlacement
                ? cell.Level >= 0
                : HasSurface(world, grid, cell) ||
                  SelectedKind == BuildingKind.Belt &&
                  TryGetRamp(world, cell, out _);
            return true;
        }

        if (TryRaycastFoundation(
                world,
                ray,
                out GridCell foundationCell,
                out hitPoint,
                out float3 surfaceNormal))
        {
            bool placeFlatFoundationAbove =
                foundationPlacement &&
                SelectedKind == BuildingKind.Foundation &&
                surfaceNormal.y > 0.5f;
            cell = placeFlatFoundationAbove
                ? new GridCell(
                    foundationCell.X,
                    foundationCell.Level + 1,
                    foundationCell.Z)
                : foundationCell;
            isInside = foundationPlacement || HasSurface(world, grid, cell) ||
                       SelectedKind == BuildingKind.Belt &&
                       TryGetRamp(world, cell, out _);
            SetSelectedGridLevel(cell.Level);
            return true;
        }

        Plane gridPlane = new Plane(
            Vector3.up,
            Vector3.zero);
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
        isInside = HasSurface(world, grid, cell) ||
                   SelectedKind == BuildingKind.Belt &&
                   TryGetRamp(world, cell, out _);
        if (foundationPlacement)
        {
            cell.Level = 0;
            isInside = true;
        }
        return true;
    }

    private bool TryRaycastFoundation(
        World world,
        Ray ray,
        out GridCell cell,
        out Vector3 hitPoint,
        out float3 surfaceNormal)
    {
        cell = default;
        hitPoint = default;
        surfaceNormal = default;
        if (!ecsCacheInitialized ||
            physicsWorldQuery.CalculateEntityCount() != 1)
        {
            return false;
        }

        EntityManager manager = world.EntityManager;
        manager.CompleteDependencyBeforeRO<Unity.Physics.PhysicsWorldSingleton>();
        Unity.Physics.PhysicsWorldSingleton physicsWorld =
            physicsWorldQuery.GetSingleton<Unity.Physics.PhysicsWorldSingleton>();
        float maxDistance = math.max(1f, inputCamera.farClipPlane);
        Unity.Physics.RaycastInput input = new Unity.Physics.RaycastInput
        {
            Start = ray.origin,
            End = ray.origin + ray.direction * maxDistance,
            Filter = FoundationCollisionCategories.FoundationQueryFilter
        };
        if (!physicsWorld.CastRay(input, out Unity.Physics.RaycastHit hit))
        {
            return false;
        }

        if (TryResolveRampPhysicsHit(
                manager,
                hit.Entity,
                out RampConnector ramp))
        {
            cell = ramp.Cell;
            hitPoint = hit.Position;
            surfaceNormal = hit.SurfaceNormal;
            return true;
        }

        SurfaceRegistrySystem registry =
            world.GetExistingSystemManaged<SurfaceRegistrySystem>();
        if (!FoundationQueryUtility.TryResolveFoundation(
                hit.Position,
                hit.SurfaceNormal,
                GetWorldGridConfig(),
                registry,
                out cell,
                out _))
        {
            return false;
        }

        hitPoint = hit.Position;
        surfaceNormal = hit.SurfaceNormal;
        return true;
    }

    public static bool TryResolveRampPhysicsHit(
        EntityManager manager,
        Entity hitEntity,
        out RampConnector ramp)
    {
        ramp = default;
        Entity current = hitEntity;
        for (int depth = 0;
             depth < 8 && current != Entity.Null && manager.Exists(current);
             depth++)
        {
            if (manager.HasComponent<RampConnector>(current))
            {
                ramp = manager.GetComponentData<RampConnector>(current);
                return true;
            }
            if (!manager.HasComponent<Parent>(current))
                break;
            current = manager.GetComponentData<Parent>(current).Value;
        }
        return false;
    }

    private WorldGridConfig GetWorldGridConfig()
    {
        if (cachedWorld != null && cachedWorld.IsCreated &&
            gridEntity != Entity.Null &&
            cachedWorld.EntityManager.Exists(gridEntity) &&
            cachedWorld.EntityManager.HasComponent<WorldGridConfig>(gridEntity))
        {
            return cachedWorld.EntityManager.GetComponentData<WorldGridConfig>(
                gridEntity);
        }

        GridDefinition grid = cachedWorld.EntityManager.GetComponentData<
            GridDefinition>(gridEntity);
        return new WorldGridConfig
        {
            CellSize = grid.CellSize,
            LayerHeight = EcsGridUtility.DefaultLayerHeight,
            Origin = grid.Origin
        };
    }

    public int SelectedGridLevel => GetLayerViewState().SelectedLevel;

    public GridLayerPickingMode LayerPickingMode =>
        GetLayerViewState().PickingMode;

    public GridLayerVisibilityMode LayerVisibilityMode =>
        GetLayerViewState().VisibilityMode;

    public void SelectGridLevel(int level)
    {
        GridLayerViewState state = GetLayerViewState();
        state.SelectedLevel = level;
        state.PickingMode = GridLayerPickingMode.SelectedLevel;
        SetLayerViewState(state);
    }

    public void UseFirstHitLayer()
    {
        GridLayerViewState state = GetLayerViewState();
        state.PickingMode = GridLayerPickingMode.FirstHit;
        SetLayerViewState(state);
    }

    public void SetLayerVisibility(GridLayerVisibilityMode mode)
    {
        GridLayerViewState state = GetLayerViewState();
        state.VisibilityMode = mode;
        SetLayerViewState(state);
    }

    private GridLayerViewState GetLayerViewState()
    {
        if (TryGetGrid(out World world, out Entity entity, out _) &&
            world.EntityManager.HasComponent<GridLayerViewState>(entity))
        {
            return world.EntityManager.GetComponentData<GridLayerViewState>(
                entity);
        }
        return new GridLayerViewState
        {
            SelectedLevel = 0,
            PickingMode = GridLayerPickingMode.FirstHit,
            VisibilityMode = GridLayerVisibilityMode.All,
            VisibleLevelRadius = 2,
            Revision = 1
        };
    }

    private void SetSelectedGridLevel(int level)
    {
        GridLayerViewState state = GetLayerViewState();
        if (state.SelectedLevel == level)
            return;
        state.SelectedLevel = level;
        SetLayerViewState(state);
    }

    private void SetLayerViewState(GridLayerViewState state)
    {
        if (!TryGetGrid(out World world, out Entity entity, out _))
            return;
        state.Revision++;
        if (world.EntityManager.HasComponent<GridLayerViewState>(entity))
            world.EntityManager.SetComponentData(entity, state);
        else
            world.EntityManager.AddComponentData(entity, state);
    }

    private static bool HasSurface(
        World world,
        in GridDefinition grid,
        GridCell cell)
    {
        SurfaceRegistrySystem registry =
            world?.GetExistingSystemManaged<SurfaceRegistrySystem>();
        return registry != null && registry.IsReady
            ? registry.HasSurface(cell)
            : EcsGridUtility.Contains(grid, cell);
    }

    private static bool TryGetRamp(
        World world,
        GridCell cell,
        out RampRegistrySystem.Record ramp)
    {
        ramp = null;
        if (world == null || !world.IsCreated)
            return false;
        RampRegistrySystem registry =
            world.GetExistingSystemManaged<RampRegistrySystem>();
        return registry != null && registry.IsReady &&
               registry.TryGet(cell, out ramp);
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
        CancelPlacementGesture();
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

    private bool TryGetSelectedBuildingLevel(
        out FactoryBuildingLevelBlob level)
    {
        level = default;
        if (!TryGetDatabase(
                out BlobAssetReference<FactoryDatabaseBlob> reference))
            return false;
        ref FactoryDatabaseBlob database = ref reference.Value;
        return FactoryDatabaseUtility.TryGetBuildingLevel(
            ref database,
            SelectedBuildingLevel,
            out level,
            out _);
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
        physicsWorldQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Unity.Physics.PhysicsWorldSingleton>());
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
            physicsWorldQuery.Dispose();
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
            HorizontalFirst = command.HorizontalFirst,
            VisualMaterialId = command.VisualMaterialId,
            RampStartHeightUnits = command.RampStartHeightUnits,
            RampRiseHeightUnits = command.RampRiseHeightUnits
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
