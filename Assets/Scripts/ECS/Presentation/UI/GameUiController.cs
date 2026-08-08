using System;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
[RequireComponent(typeof(UIDocument))]
public sealed class GameUiController : MonoBehaviour
{
    [SerializeField] private VisualTreeAsset rootTree;
    [SerializeField] private FactoryPresentationCatalog presentationCatalog;
    [SerializeField] private uint localPlayerId = 1;

    private UIDocument document;
    private World attachedWorld;
    private PlayerInputModeController inputMode;
    private EcsGridInteractionController gridInteraction;
    private ItemSlotGridView backpackView;
    private BuildingWindowView buildingView;
    private BuildCatalogView buildCatalogView;
    private ItemDragController dragController;
    private PlayerCommandBus commandBus;
    private VisualElement notificationLayer;
    private ulong pendingRecipeRequest;
    private BuildingRuntimeId openBuildingId;
    private bool buildCatalogRendered;

    public UiDataHub DataHub { get; private set; }
    public UiWindowManager Windows { get; private set; }
    public InventorySnapshot LatestInventory { get; private set; }
    public BuildingSnapshot LatestBuilding { get; private set; }
    public BuildCatalogSnapshot LatestBuildCatalog { get; private set; }
    public FactoryPresentationCatalog PresentationCatalog => presentationCatalog;
    public PlayerCommandBus CommandBus => commandBus;
    public BuildingLevelId SelectedCatalogBuildingLevel =>
        buildCatalogView?.SelectedBuildingLevel ?? default;

    private void OnEnable()
    {
        document = GetComponent<UIDocument>();
        presentationCatalog?.BuildIndex();
        VisualElement root = document.rootVisualElement;
        if (root.Q("game-ui-root") == null && rootTree != null)
            rootTree.CloneTree(root);

        VisualElement uiRoot = Require(root, "game-ui-root");
        inputMode = GetComponent<PlayerInputModeController>();
        gridInteraction = FindFirstObjectByType<EcsGridInteractionController>();
        Windows = new UiWindowManager(
            Require(uiRoot, "left-dock"),
            Require(uiRoot, "right-dock"),
            Require(uiRoot, "build-catalog-dock"),
            Require(uiRoot, "center-overlay"));
        Windows.Register(
            UiWindowId.Building,
            UiDockRegion.Left,
            Require(uiRoot, "building-window"));
        Windows.Register(
            UiWindowId.Backpack,
            UiDockRegion.Right,
            Require(uiRoot, "backpack-window"));
        Windows.Register(
            UiWindowId.BuildCatalog,
            UiDockRegion.Bottom,
            Require(uiRoot, "build-catalog-window"));

        backpackView = new ItemSlotGridView(
            Require(uiRoot, "backpack-slot-grid"), presentationCatalog);
        buildingView = new BuildingWindowView(uiRoot, presentationCatalog);
        buildCatalogView = new BuildCatalogView(uiRoot, presentationCatalog);
        buildCatalogView.BuildingSelected += OnBuildingSelected;
        attachedWorld = World.DefaultGameObjectInjectionWorld;
        commandBus = PlayerCommandRuntimeServices.GetOrCreateBus(
            attachedWorld, new PlayerId { Value = localPlayerId });
        commandBus.ResultReceived += OnCommandResult;
        backpackView.SetEndpointFactory(slot => new ItemEndpoint
        {
            OwnerKind = ItemOwnerKind.Player,
            OwnerRuntimeId = localPlayerId,
            Domain = ItemSlotDomain.Inventory,
            SlotIndex = slot.SlotIndex
        });
        notificationLayer = Require(uiRoot, "notification-layer");
        dragController = new ItemDragController(
            Require(uiRoot, "drag-layer"),
            notificationLayer,
            presentationCatalog,
            commandBus,
            inputMode);
        backpackView.PointerDown += dragController.Begin;
        buildingView.SlotPointerDown += dragController.Begin;
        buildingView.RecipeSelected += OnRecipeSelected;
        Require<Button>(uiRoot, "backpack-close").clicked += CloseBackpack;
        Require<Button>(uiRoot, "building-close").clicked += CloseBuilding;
        Require<Button>(uiRoot, "build-catalog-close").clicked += CloseBuildCatalog;

        DataHub = new UiDataHub();
        UiRuntimeServices.Attach(attachedWorld, DataHub);
    }

    private void OnDisable()
    {
        if (buildCatalogView != null)
            buildCatalogView.BuildingSelected -= OnBuildingSelected;
        if (buildingView != null && dragController != null)
        {
            buildingView.SlotPointerDown -= dragController.Begin;
            buildingView.RecipeSelected -= OnRecipeSelected;
        }
        if (backpackView != null && dragController != null)
            backpackView.PointerDown -= dragController.Begin;
        dragController?.Dispose();
        if (commandBus != null)
            commandBus.ResultReceived -= OnCommandResult;
        dragController = null;
        Windows?.Dispose();
        Windows = null;
        UiRuntimeServices.Detach(attachedWorld, DataHub);
        attachedWorld = null;
        DataHub = null;
        commandBus = null;
        notificationLayer = null;
        pendingRecipeRequest = 0;
        buildCatalogRendered = false;
    }

    private void Update()
    {
        commandBus?.PumpResults();
        if (inputMode == null || Windows == null)
            return;
        if (inputMode.BuildCatalogTogglePressedThisFrame)
            HandleBuildCatalogToggle();
        if (inputMode.BackpackTogglePressedThisFrame)
            HandleBackpackToggle();
        if (inputMode.CancelPressedThisFrame)
            HandleCancel();

        if (!inputMode.IsBuildMode &&
            inputMode.PrimaryPointerPressedThisFrame &&
            !inputMode.BlocksWorldInput &&
            gridInteraction != null &&
            gridInteraction.TryGetHoveredBuilding(out BuildingRuntimeId buildingId))
            OpenBuilding(buildingId);
    }

    public void ToggleBackpack(PlayerId playerId) =>
        Windows.Toggle(
            UiWindowId.Backpack,
            () => DataHub.Inventories.Subscribe(playerId, OnInventoryChanged));

    public void OpenBuilding(BuildingRuntimeId buildingId) =>
        OpenBuildingInternal(buildingId);

    public void ToggleBuildCatalog() => HandleBuildCatalogToggle();

    public void SelectCatalogBuildingLevel(BuildingLevelId id) =>
        buildCatalogView?.SetSelected(id);

    public void ConfirmCatalogBuildingSelection() =>
        buildCatalogView?.ConfirmSelection();

    public void HandleBuildCatalogToggle()
    {
        if (inputMode == null)
            return;
        if (!inputMode.IsBuildMode)
        {
            CloseBuilding();
            CloseBackpack();
            inputMode.SetBuildMode(true);
        }
        if (!Windows.IsOpen(UiWindowId.BuildCatalog))
            OpenBuildCatalog();
    }

    public void HandleBackpackToggle()
    {
        if (inputMode != null && inputMode.IsBuildMode)
            return;
        ToggleBackpack(new PlayerId { Value = localPlayerId });
    }

    public void HandleCancel()
    {
        if (inputMode != null && inputMode.IsBuildMode)
        {
            CloseBuildCatalog();
            inputMode.SetBuildMode(false);
            return;
        }
        CloseBuilding();
        CloseBackpack();
    }

    public void OpenBuildCatalog()
    {
        Windows.Open(UiWindowId.BuildCatalog,
            () => DataHub.BuildCatalog.Subscribe(0, OnBuildCatalogChanged));
        inputMode?.SetModalOpen(true);
    }

    public void CloseBuildCatalog()
    {
        Windows.Close(UiWindowId.BuildCatalog);
        inputMode?.SetModalOpen(false);
    }

    public void CloseBackpack()
    {
        dragController?.Cancel();
        Windows?.Close(UiWindowId.Backpack);
    }

    public void CloseBuilding()
    {
        dragController?.Cancel();
        Windows?.Close(UiWindowId.Building);
        openBuildingId = default;
    }

    private void OnRecipeSelected(RecipeId recipe)
    {
        if (!openBuildingId.IsValid || LatestBuilding == null ||
            !LatestBuilding.IsAvailable ||
            !LatestBuilding.RuntimeId.Equals(openBuildingId))
            return;
        pendingRecipeRequest = commandBus?.Submit(new RecipeSelectionCommand
        {
            BuildingCell = LatestBuilding.GridCell,
            Recipe = recipe
        }) ?? 0;
        if (pendingRecipeRequest != 0)
            buildingView.SetRecipePending(true);
    }

    private void OnCommandResult(PlayerCommandResult result)
    {
        if (result.Kind != PlayerCommandKind.SelectRecipe ||
            result.Header.RequestId != pendingRecipeRequest)
            return;
        pendingRecipeRequest = 0;
        buildingView.SetRecipePending(false, result.Success);
        if (!result.Success)
            ShowCommandError(result.FailureReason == PlayerCommandFailureReason.RecipeBusy
                ? "建筑正在生产或槽内仍有物品，无法更换配方"
                : "配方选择失败");
    }

    private void ShowCommandError(string message)
    {
        if (notificationLayer == null) return;
        Label label = new(message) { pickingMode = PickingMode.Ignore };
        label.AddToClassList("command-error-toast");
        notificationLayer.Add(label);
        label.schedule.Execute(label.RemoveFromHierarchy).StartingIn(2500);
    }

    public void CloseTopWindow()
    {
        if (Windows.IsOpen(UiWindowId.BuildCatalog))
            CloseBuildCatalog();
        else if (Windows.IsOpen(UiWindowId.Building))
            CloseBuilding();
        else if (Windows.IsOpen(UiWindowId.Backpack))
            CloseBackpack();
    }

    private void OnInventoryChanged(InventorySnapshot snapshot)
    {
        LatestInventory = snapshot;
        if (!snapshot.IsAvailable)
            Windows.Close(UiWindowId.Backpack);
        else
            backpackView.Render(snapshot.Slots);
    }

    private void OnBuildingChanged(BuildingSnapshot snapshot)
    {
        LatestBuilding = snapshot;
        if (!snapshot.IsAvailable)
            Windows.Close(UiWindowId.Building);
        else
            buildingView.Render(snapshot);
    }

    private void OnBuildCatalogChanged(BuildCatalogSnapshot snapshot)
    {
        LatestBuildCatalog = snapshot;
        if (buildCatalogRendered)
            return;
        buildCatalogView.Render(snapshot);
        if (gridInteraction != null)
            buildCatalogView.SetSelected(gridInteraction.SelectedBuildingLevel);
        buildCatalogRendered = true;
    }

    private void OpenBuildingInternal(BuildingRuntimeId buildingId)
    {
        if (!buildingId.IsValid || (inputMode != null && inputMode.IsBuildMode))
            return;
        if (Windows.IsOpen(UiWindowId.Building) && !openBuildingId.Equals(buildingId))
            Windows.Close(UiWindowId.Building);
        openBuildingId = buildingId;
        Windows.Open(UiWindowId.Building,
            () => DataHub.Buildings.Subscribe(buildingId, OnBuildingChanged));
    }

    private void OnBuildingSelected(BuildingLevelId id)
    {
        if (gridInteraction == null || !gridInteraction.TrySelectBuildingLevel(id))
            return;
        buildCatalogView.SetSelected(id);
        CloseBuildCatalog();
        inputMode?.SetBuildMode(true);
    }

    private static VisualElement Require(VisualElement root, string name) =>
        root.Q(name) ?? throw new InvalidOperationException(
            $"Game UI root is missing required element '{name}'.");

    private static T Require<T>(VisualElement root, string name)
        where T : VisualElement =>
        root.Q<T>(name) ?? throw new InvalidOperationException(
            $"Game UI root is missing required element '{name}'.");
}
