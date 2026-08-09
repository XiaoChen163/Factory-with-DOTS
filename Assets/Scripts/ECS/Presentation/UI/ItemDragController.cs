using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class ItemDragController : IDisposable
{
    private const float DragThreshold = 6f;
    private readonly VisualElement dragLayer;
    private readonly VisualElement notificationLayer;
    private readonly FactoryPresentationCatalog catalog;
    private readonly PlayerCommandBus commandBus;
    private readonly PlayerInputModeController inputMode;
    private readonly Dictionary<ulong, PendingMove> pending = new();
    private readonly VisualElement dragIcon;
    private ItemSlotBinding source;
    private VisualElement captureTarget;
    private int pointerId = -1;
    private Vector2 pressPosition;
    private bool dragging;

    public ItemDragController(
        VisualElement dragLayer,
        VisualElement notificationLayer,
        FactoryPresentationCatalog catalog,
        PlayerCommandBus commandBus,
        PlayerInputModeController inputMode)
    {
        this.dragLayer = dragLayer ?? throw new ArgumentNullException(nameof(dragLayer));
        this.notificationLayer = notificationLayer ?? throw new ArgumentNullException(nameof(notificationLayer));
        this.catalog = catalog;
        this.commandBus = commandBus ?? throw new ArgumentNullException(nameof(commandBus));
        this.inputMode = inputMode;
        dragIcon = new VisualElement { name = "item-drag-icon", pickingMode = PickingMode.Ignore };
        dragIcon.AddToClassList("item-drag-icon");
        dragIcon.style.display = DisplayStyle.None;
        dragLayer.Add(dragIcon);
        commandBus.ResultReceived += OnCommandResult;
    }

    public void Begin(ItemSlotBinding binding, PointerDownEvent evt)
    {
        if (evt.button != 0 || binding == null || !CanExtract(binding.Snapshot) ||
            pending.Count > 0)
            return;
        Cancel();
        source = binding;
        captureTarget = binding.Element;
        pointerId = evt.pointerId;
        pressPosition = new Vector2(evt.position.x, evt.position.y);
        captureTarget.RegisterCallback<PointerMoveEvent>(OnPointerMove);
        captureTarget.RegisterCallback<PointerUpEvent>(OnPointerUp);
        captureTarget.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
        evt.StopPropagation();
    }

    public void Cancel()
    {
        if (captureTarget != null)
        {
            if (pointerId >= 0 && captureTarget.HasPointerCapture(pointerId))
                captureTarget.ReleasePointer(pointerId);
            captureTarget.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            captureTarget.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            captureTarget.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
        }
        source?.Element?.RemoveFromClassList("item-slot--drag-source");
        captureTarget = null;
        source = null;
        pointerId = -1;
        dragging = false;
        dragIcon.style.display = DisplayStyle.None;
        inputMode?.SetDragging(false);
        ClearTargetHighlights();
    }

    public void Dispose()
    {
        Cancel();
        commandBus.ResultReceived -= OnCommandResult;
        dragIcon.RemoveFromHierarchy();
    }

    public static bool CanDrop(ItemSlotSnapshot source, ItemSlotSnapshot destination)
    {
        if (!CanExtract(source) ||
            (destination.Access != UiSlotAccess.Insert &&
             destination.Access != UiSlotAccess.InsertAndExtract))
            return false;
        if (destination.AcceptedItemId.IsValid && destination.AcceptedItemId != source.ItemId)
            return false;
        if (destination.ItemId.IsValid && destination.ItemId != source.ItemId)
            return false;
        return destination.Capacity == 0 ||
               destination.Count + source.Count <= destination.Capacity;
    }

    private static bool CanExtract(ItemSlotSnapshot value) =>
        value.ItemId.IsValid && value.Count > 0 &&
        (value.Access == UiSlotAccess.Extract ||
         value.Access == UiSlotAccess.InsertAndExtract);

    private void OnPointerMove(PointerMoveEvent evt)
    {
        if (evt.pointerId != pointerId || source == null)
            return;
        Vector2 position = new(evt.position.x, evt.position.y);
        if (!dragging && Vector2.Distance(pressPosition, position) < DragThreshold)
            return;
        if (!dragging)
        {
            dragging = true;
            captureTarget.CapturePointer(pointerId);
            source.Element.AddToClassList("item-slot--drag-source");
            dragIcon.style.backgroundImage = new StyleBackground(
                catalog?.GetItemIcon(source.Snapshot.ItemId));
            dragIcon.style.display = DisplayStyle.Flex;
            inputMode?.SetDragging(true);
        }
        dragIcon.style.left = position.x - 24f;
        dragIcon.style.top = position.y - 24f;
        HighlightTarget(PickBinding(position));
        evt.StopPropagation();
    }

    private void OnPointerUp(PointerUpEvent evt)
    {
        if (evt.pointerId != pointerId)
            return;
        ItemSlotBinding destination = dragging
            ? PickBinding(new Vector2(evt.position.x, evt.position.y))
            : null;
        ItemSlotBinding origin = source;
        bool submit = destination != null && destination != origin &&
                      CanDrop(origin.Snapshot, destination.Snapshot);
        Cancel();
        if (!submit)
            return;
        ulong request = commandBus.Submit(new MoveItemPlayerCommand
        {
            Source = origin.Endpoint,
            Destination = destination.Endpoint,
            ExpectedItemType = origin.Snapshot.ItemId,
            Amount = origin.Snapshot.Count
        });
        origin.Element.AddToClassList("item-slot--pending");
        destination.Element.AddToClassList("item-slot--pending");
        pending.Add(request, new PendingMove(origin.Element, destination.Element));
        evt.StopPropagation();
    }

    private void OnPointerCancel(PointerCancelEvent evt) => Cancel();

    private ItemSlotBinding PickBinding(Vector2 position)
    {
        VisualElement picked = dragLayer.panel?.Pick(position);
        while (picked != null)
        {
            if (picked.userData is ItemSlotBinding binding)
                return binding;
            picked = picked.parent;
        }
        return null;
    }

    private void HighlightTarget(ItemSlotBinding target)
    {
        ClearTargetHighlights();
        if (target == null || target == source)
            return;
        target.Element.AddToClassList(CanDrop(source.Snapshot, target.Snapshot)
            ? "item-slot--drop-valid"
            : "item-slot--drop-invalid");
    }

    private void ClearTargetHighlights()
    {
        VisualElement root = dragLayer.panel?.visualTree;
        if (root == null) return;
        root.Query<VisualElement>(className: "item-slot--drop-valid").ForEach(
            element => element.RemoveFromClassList("item-slot--drop-valid"));
        root.Query<VisualElement>(className: "item-slot--drop-invalid").ForEach(
            element => element.RemoveFromClassList("item-slot--drop-invalid"));
    }

    private void OnCommandResult(PlayerCommandResult result)
    {
        if (result.Kind != PlayerCommandKind.MoveItem ||
            !pending.TryGetValue(result.Header.RequestId, out PendingMove move))
            return;
        move.Source.RemoveFromClassList("item-slot--pending");
        move.Destination.RemoveFromClassList("item-slot--pending");
        pending.Remove(result.Header.RequestId);
        if (!result.Success)
            ShowError(ErrorText(result.FailureReason));
    }

    private void ShowError(string message)
    {
        Label label = new(message) { pickingMode = PickingMode.Ignore };
        label.AddToClassList("command-error-toast");
        notificationLayer.Add(label);
        label.schedule.Execute(label.RemoveFromHierarchy).StartingIn(2500);
    }

    private static string ErrorText(PlayerCommandFailureReason reason) => reason switch
    {
        PlayerCommandFailureReason.StaleSnapshot => "物品状态已变化，请重试",
        PlayerCommandFailureReason.CapacityExceeded => "目标槽位容量不足",
        PlayerCommandFailureReason.DestinationRejected => "目标槽位不接受该物品",
        PlayerCommandFailureReason.EmptySource => "源槽位已为空",
        PlayerCommandFailureReason.OwnerNotFound => "容器已不可用",
        _ => "操作被拒绝"
    };

    private readonly struct PendingMove
    {
        public PendingMove(VisualElement source, VisualElement destination)
        { Source = source; Destination = destination; }
        public VisualElement Source { get; }
        public VisualElement Destination { get; }
    }
}
