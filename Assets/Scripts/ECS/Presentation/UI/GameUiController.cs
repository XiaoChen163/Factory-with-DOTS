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

    private UIDocument document;
    private World attachedWorld;

    public UiDataHub DataHub { get; private set; }
    public UiWindowManager Windows { get; private set; }
    public InventorySnapshot LatestInventory { get; private set; }
    public BuildingSnapshot LatestBuilding { get; private set; }
    public BuildCatalogSnapshot LatestBuildCatalog { get; private set; }
    public FactoryPresentationCatalog PresentationCatalog => presentationCatalog;

    private void OnEnable()
    {
        document = GetComponent<UIDocument>();
        presentationCatalog?.BuildIndex();
        VisualElement root = document.rootVisualElement;
        if (root.Q("game-ui-root") == null && rootTree != null)
            rootTree.CloneTree(root);

        VisualElement uiRoot = Require(root, "game-ui-root");
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

        DataHub = new UiDataHub();
        attachedWorld = World.DefaultGameObjectInjectionWorld;
        UiRuntimeServices.Attach(attachedWorld, DataHub);
    }

    private void OnDisable()
    {
        Windows?.Dispose();
        Windows = null;
        UiRuntimeServices.Detach(attachedWorld, DataHub);
        attachedWorld = null;
        DataHub = null;
    }

    public void ToggleBackpack(PlayerId playerId) =>
        Windows.Toggle(
            UiWindowId.Backpack,
            () => DataHub.Inventories.Subscribe(playerId, OnInventoryChanged));

    public void OpenBuilding(BuildingRuntimeId buildingId) =>
        Windows.Open(
            UiWindowId.Building,
            () => DataHub.Buildings.Subscribe(buildingId, OnBuildingChanged));

    public void ToggleBuildCatalog() =>
        Windows.Toggle(
            UiWindowId.BuildCatalog,
            () => DataHub.BuildCatalog.Subscribe(0, OnBuildCatalogChanged));

    public void CloseTopWindow()
    {
        if (Windows.IsOpen(UiWindowId.BuildCatalog))
            Windows.Close(UiWindowId.BuildCatalog);
        else if (Windows.IsOpen(UiWindowId.Building))
            Windows.Close(UiWindowId.Building);
        else if (Windows.IsOpen(UiWindowId.Backpack))
            Windows.Close(UiWindowId.Backpack);
    }

    private void OnInventoryChanged(InventorySnapshot snapshot)
    {
        LatestInventory = snapshot;
        if (!snapshot.IsAvailable)
            Windows.Close(UiWindowId.Backpack);
    }

    private void OnBuildingChanged(BuildingSnapshot snapshot)
    {
        LatestBuilding = snapshot;
        if (!snapshot.IsAvailable)
            Windows.Close(UiWindowId.Building);
    }

    private void OnBuildCatalogChanged(BuildCatalogSnapshot snapshot) =>
        LatestBuildCatalog = snapshot;

    private static VisualElement Require(VisualElement root, string name) =>
        root.Q(name) ?? throw new InvalidOperationException(
            $"Game UI root is missing required element '{name}'.");
}
