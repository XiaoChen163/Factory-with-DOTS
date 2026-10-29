using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public sealed partial class EcsGridInteractionController
{
    private bool verticalPathStarted;
    private bool verticalConfirmPending;
    private GridCell verticalPathStart;
    private BuildingDefinitionId verticalEntryLevel;
    private ulong verticalEntryRequest;
    private ulong verticalPlacementToken;
    private ulong verticalCompletionRequest;
    private bool simulatedVerticalPointer;
    private Vector2 simulatedVerticalScreenPosition;
    private readonly Dictionary<GridCell, GridPlacement> verticalPreviewStaged = new();

    public bool IsVerticalBeltPathStarted => verticalPathStarted;
    public GridCell VerticalPreviewExitCell { get; private set; }
    public bool VerticalPreviewValid { get; private set; }
    public int2 VerticalPreviewExitDirection => EcsGridUtility.Rotate(new int2(1,0), quarterTurns);

    public void SimulateVerticalPointer(Vector2 screenPosition)
    {
        simulatedVerticalPointer = true;
        simulatedVerticalScreenPosition = screenPosition;
        simulatedHoverActive = true;
    }

    private void LateUpdate()
    {
        UpdateOutputPortLabel();
        if (!verticalPathStarted) return;
        if (!simulatedHoverActive && inputMode != null &&
            (!inputMode.IsBuildMode || inputMode.IsDemolitionMode || inputMode.BlocksWorldInput))
        {
            verticalConfirmPending = false;
            HidePlacementPreview();
            return;
        }
        // Camera navigation runs in Update. Read its final pose for both preview and confirmation.
        UpdateVerticalPathPreview();
        if (verticalConfirmPending)
        {
            verticalConfirmPending = false;
            ConfirmVerticalPath();
        }
    }

    private void BeginVerticalPath(World world, Entity grid, GridCell cell)
    {
        if (cell.Level < 0 || TryGetBuilding(world, cell, out _)) return;
        verticalPathStart = cell;
        verticalEntryLevel = SelectedBuildingDefinition;
        verticalPathStarted = true;
        verticalEntryRequest = Enqueue(world, grid, new GridBuildCommand
        {
            Type = GridBuildCommandType.Place,
            Kind = BuildingKind.VerticalBeltEntry,
            BuildingDefinition = verticalEntryLevel,
            StartCell = cell,
            EndCell = cell,
            QuarterTurns = quarterTurns
        });
        verticalPlacementToken = verticalEntryRequest;
    }

    private bool TryGetVerticalExitCell(out GridCell exit)
    {
        exit = verticalPathStart;
        if (simulatedHoverActive && !simulatedVerticalPointer)
        {
            exit.Level = simulatedHoverCell.Level;
            return true;
        }
        Ray ray = simulatedVerticalPointer
            ? inputCamera.ScreenPointToRay(simulatedVerticalScreenPosition)
            : CreateInteractionRay();
        return VerticalBeltPlacementUtility.TryResolveExit(ray, inputCamera.transform.forward,
            verticalPathStart, GetWorldGridConfig(), out exit,
            simulatedVerticalPointer ? float.PositiveInfinity : MaxInteractionDistance);
    }

    private bool ValidateVerticalPreview(World world, GridCell exit)
    {
        long distance = System.Math.Abs((long)exit.Level - verticalPathStart.Level);
        if (exit.Level < 0 || distance < 2 || distance > VerticalBeltUtility.MaxPathNodes ||
            !TryGetBuilding(world, verticalPathStart, out Entity entry) ||
            !world.EntityManager.HasComponent<BuildingIdentity>(entry) ||
            world.EntityManager.GetComponentData<BuildingIdentity>(entry).BuildingDefinition != verticalEntryLevel)
            return false;
        var ramps = world.GetExistingSystemManaged<RampRegistrySystem>();
        int step = exit.Level > verticalPathStart.Level ? 1 : -1;
        verticalPreviewStaged.Clear();
        for (int i = 1; i <= distance; i++)
        {
            GridCell cell = verticalPathStart;
            cell.Level += step * i;
            var kind = i == distance ? BuildingKind.VerticalBeltExit : BuildingKind.VerticalBeltSegment;
            byte turns = i == distance ? quarterTurns : step > 0 ? (byte)0 : (byte)2;
            if (TryGetBuilding(world, cell, out _) || ramps != null && ramps.ContainsCell(cell) ||
                !VerticalBeltUtility.Validate(kind, cell, turns,
                    address => verticalPreviewStaged.TryGetValue(address, out var staged)
                        ? staged : GetPlacement(world, address), out _))
                return false;
            verticalPreviewStaged[cell] = new GridPlacement
            { AnchorCell = cell, Kind = kind, QuarterTurns = turns, FootprintSize = new int2(1,1) };
        }
        return true;
    }

    private void UpdateVerticalPathPreview()
    {
        if (!TryGetGrid(out World world, out _, out WorldGridConfig grid) ||
            !TryGetDatabase(out var reference))
        {
            HidePlacementPreview();
            return;
        }
        if (!TryGetVerticalExitCell(out GridCell exit))
        {
            VerticalPreviewValid = false;
            HidePlacementPreview();
            return;
        }
        VerticalPreviewExitCell = exit;
        VerticalPreviewValid = ValidateVerticalPreview(world, VerticalPreviewExitCell);
        previewCells.Clear();
        previewDisplayDirections.Clear();
        previewPortCells.Clear();
        previewPortDirections.Clear();
        previewPortTypes.Clear();
        int difference = VerticalPreviewExitCell.Level - verticalPathStart.Level;
        int distance = math.min(math.abs(difference), VerticalBeltUtility.MaxPathNodes);
        int step = difference >= 0 ? 1 : -1;
        for (int i = 1; i <= distance; i++)
        {
            GridCell cell = verticalPathStart;
            cell.Level += step * i;
            previewCells.Add(cell);
        }
        ref FactoryDatabaseBlob database = ref reference.Value;
        if (VerticalBeltUtility.TryGetPathDefinitions(ref database, verticalEntryLevel, out _, out var exitId) &&
            FactoryDatabaseUtility.TryGetBuildingDefinition(ref database, exitId, out var building))
            BuildPortPreview(VerticalPreviewExitCell, new int2(1,1), building);
        ShowPlacementPreview(grid, VerticalPreviewValid);
    }

    private void ConfirmVerticalPath(int? explicitLevel = null)
    {
        if (verticalCompletionRequest != 0 || !TryGetGrid(out World world, out Entity grid, out _)) return;
        GridCell exit;
        if (explicitLevel.HasValue)
        {
            exit = verticalPathStart;
            exit.Level = explicitLevel.Value;
        }
        else if (!TryGetVerticalExitCell(out exit)) return;
        if (!ValidateVerticalPreview(world, exit)) return;
        verticalCompletionRequest = Enqueue(world, grid, new GridBuildCommand
        {
            Type = GridBuildCommandType.PlaceVerticalBeltPath,
            Kind = BuildingKind.VerticalBeltEntry,
            BuildingDefinition = verticalEntryLevel,
            StartCell = verticalPathStart,
            EndCell = exit,
            QuarterTurns = quarterTurns
        });
    }

    private void HandleVerticalBuildResult(PlayerCommandResult result)
    {
        if (result.Header.RequestId == verticalEntryRequest)
        {
            verticalEntryRequest = 0;
            if (!result.Success) CancelPlacementGesture(false);
        }
        else if (result.Header.RequestId == verticalCompletionRequest)
        {
            verticalCompletionRequest = 0;
            if (result.Success) CancelPlacementGesture(false);
        }
    }

    private void RollBackVerticalPlacement()
    {
        if (!verticalPathStarted || verticalPlacementToken == 0 ||
            !TryGetGrid(out World world, out Entity grid, out _)) return;
        Enqueue(world, grid, new GridBuildCommand
        {
            Type = GridBuildCommandType.CancelVerticalBeltPlacement,
            Kind = BuildingKind.VerticalBeltEntry,
            BuildingDefinition = verticalEntryLevel,
            PlacementRequestId = verticalPlacementToken,
            StartCell = verticalPathStart
        });
    }

    private void DisconnectVerticalCommandBus()
    {
        if (commandBus != null) commandBus.ResultReceived -= HandleVerticalBuildResult;
        commandBus = null;
    }
}
