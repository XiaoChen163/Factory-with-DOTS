using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

[DisallowMultipleComponent]
public sealed class PlayerInputModeController : MonoBehaviour
{
    [SerializeField] private InputActionAsset actions;
    [SerializeField] private UIDocument uiDocument;

    private InputActionAsset runtimeActions;
    private InputActionMap gameplay;
    private InputActionMap build;
    private InputActionMap ui;
    private InputAction toggleBackpack;
    private InputAction gameplayToggleBuildCatalog;
    private InputAction buildToggleBuildCatalog;
    private InputAction place;
    private InputAction rotate;
    private InputAction remove;
    private InputAction cancel;

    public bool IsBuildMode { get; private set; }
    public bool IsModalOpen { get; private set; }
    public bool IsDragging { get; private set; }
    public event Action CancelRequested;

    public bool BlocksWorldInput =>
        IsDragging || IsModalOpen || IsPointerOverInteractiveUi();

    public bool IsGameplayActionMapEnabled => gameplay?.enabled == true;
    public bool IsBuildActionMapEnabled => build?.enabled == true;
    public bool BackpackTogglePressedThisFrame => WasPressed(toggleBackpack);
    public bool BuildCatalogTogglePressedThisFrame =>
        WasPressed(gameplayToggleBuildCatalog) ||
        WasPressed(buildToggleBuildCatalog);
    public bool IsBuildCatalogToggleActionEnabled =>
        gameplayToggleBuildCatalog?.enabled == true ||
        buildToggleBuildCatalog?.enabled == true;
    public bool PlacePressedThisFrame => WasPressed(place);
    public bool PrimaryPointerPressedThisFrame =>
        Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    public bool RotatePressedThisFrame => WasPressed(rotate);
    public bool RemovePressedThisFrame => WasPressed(remove);
    public bool CancelPressedThisFrame => WasPressed(cancel);
    public bool RemoveBeltLineModifierActive =>
        Keyboard.current != null &&
        (Keyboard.current.leftCtrlKey.isPressed ||
         Keyboard.current.rightCtrlKey.isPressed);

    private void OnEnable()
    {
        if (actions == null)
            return;
        runtimeActions = Instantiate(actions);
        gameplay = runtimeActions.FindActionMap("Gameplay", false) ??
                   runtimeActions.FindActionMap("Player", false);
        EnsureGameplayActions(gameplay);
        build = runtimeActions.FindActionMap("Build", false) ??
                runtimeActions.AddActionMap("Build");
        EnsureBuildActions(build);
        ui = runtimeActions.FindActionMap("UI", false);
        toggleBackpack = gameplay?.FindAction("ToggleBackpack", false);
        gameplayToggleBuildCatalog =
            gameplay?.FindAction("ToggleBuildCatalog", false);
        buildToggleBuildCatalog =
            build?.FindAction("ToggleBuildCatalog", false);
        place = build?.FindAction("Place", false);
        rotate = build?.FindAction("Rotate", false);
        remove = build?.FindAction("Remove", false);
        cancel = ui?.FindAction("Cancel", false);
        if (cancel != null)
            cancel.performed += OnCancel;
        ApplyMode();
    }

    private void OnDisable()
    {
        InputAction cancel = ui?.FindAction("Cancel", false);
        if (cancel != null)
            cancel.performed -= OnCancel;
        runtimeActions?.Disable();
        if (runtimeActions != null)
            Destroy(runtimeActions);
        runtimeActions = null;
        gameplay = null;
        build = null;
        ui = null;
        toggleBackpack = null;
        gameplayToggleBuildCatalog = null;
        buildToggleBuildCatalog = null;
        place = null;
        rotate = null;
        remove = null;
        cancel = null;
    }

    public void SetBuildMode(bool value)
    {
        IsBuildMode = value;
        ApplyMode();
    }

    public void SetModalOpen(bool value)
    {
        IsModalOpen = value;
        ApplyMode();
    }

    public void SetDragging(bool value)
    {
        IsDragging = value;
        ApplyMode();
    }

    public bool IsPointerOverInteractiveUi()
    {
        if (uiDocument == null || Mouse.current == null)
            return false;
        VisualElement root = uiDocument.rootVisualElement;
        IPanel panel = root?.panel;
        if (panel == null)
            return false;
        Vector2 point = RuntimePanelUtils.ScreenToPanel(
            panel, Mouse.current.position.ReadValue());
        VisualElement picked = panel.Pick(point);
        return picked != null && picked != root &&
               picked.pickingMode == PickingMode.Position;
    }

    private void ApplyMode()
    {
        if (runtimeActions == null)
            return;
        ui?.Enable();
        bool exclusiveUi = IsDragging || IsModalOpen;
        SetEnabled(gameplay, !exclusiveUi && !IsBuildMode);
        SetEnabled(build, !exclusiveUi && IsBuildMode);
    }

    private void OnCancel(InputAction.CallbackContext context) =>
        CancelRequested?.Invoke();

    private static bool WasPressed(InputAction action) =>
        action != null && action.WasPressedThisFrame();

    private static void SetEnabled(InputActionMap map, bool enabled)
    {
        if (map == null)
            return;
        if (enabled)
            map.Enable();
        else
            map.Disable();
    }

    private static void EnsureGameplayActions(InputActionMap map)
    {
        if (map == null)
            return;
        if (map.FindAction("ToggleBackpack", false) == null)
            map.AddAction("ToggleBackpack", InputActionType.Button)
                .AddBinding("<Keyboard>/tab");
        if (map.FindAction("ToggleBuildCatalog", false) == null)
            map.AddAction("ToggleBuildCatalog", InputActionType.Button)
                .AddBinding("<Keyboard>/q");
    }

    private static void EnsureBuildActions(InputActionMap map)
    {
        EnsureButton(map, "ToggleBuildCatalog", "<Keyboard>/q");
        EnsureButton(map, "Place", "<Mouse>/leftButton");
        EnsureButton(map, "Rotate", "<Keyboard>/r");
        EnsureButton(map, "Cancel", "<Keyboard>/escape");
        EnsureButton(map, "Remove", "<Keyboard>/delete");
    }

    private static void EnsureButton(
        InputActionMap map,
        string actionName,
        string binding)
    {
        if (map.FindAction(actionName, false) != null)
            return;
        map.AddAction(actionName, InputActionType.Button)
            .AddBinding(binding);
    }
}
