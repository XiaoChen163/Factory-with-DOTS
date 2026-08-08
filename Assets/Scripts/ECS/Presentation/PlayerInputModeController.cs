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

    public bool IsBuildMode { get; private set; }
    public bool IsModalOpen { get; private set; }
    public bool IsDragging { get; private set; }
    public event Action CancelRequested;

    public bool BlocksWorldInput =>
        IsDragging || IsModalOpen || IsPointerOverInteractiveUi();

    private void OnEnable()
    {
        if (actions == null)
            return;
        runtimeActions = Instantiate(actions);
        gameplay = runtimeActions.FindActionMap("Gameplay", false) ??
                   runtimeActions.FindActionMap("Player", false);
        EnsureGameplayActions(gameplay);
        build = runtimeActions.FindActionMap("Build", false) ??
                CreateBuildMap(runtimeActions);
        ui = runtimeActions.FindActionMap("UI", false);
        InputAction cancel = ui?.FindAction("Cancel", false);
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
                .AddBinding("<Keyboard>/b");
    }

    private static InputActionMap CreateBuildMap(InputActionAsset asset)
    {
        InputActionMap map = asset.AddActionMap("Build");
        map.AddAction("Place", InputActionType.Button)
            .AddBinding("<Mouse>/leftButton");
        map.AddAction("Rotate", InputActionType.Button)
            .AddBinding("<Keyboard>/r");
        map.AddAction("Cancel", InputActionType.Button)
            .AddBinding("<Keyboard>/escape");
        map.AddAction("Remove", InputActionType.Button)
            .AddBinding("<Keyboard>/delete");
        map.AddAction("TogglePathOrder", InputActionType.Button)
            .AddBinding("<Keyboard>/t");
        return map;
    }
}
