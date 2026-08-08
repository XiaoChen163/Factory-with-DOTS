using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

public sealed class UiWindowManager : IDisposable
{
    private readonly Dictionary<UiDockRegion, VisualElement> docks = new();
    private readonly Dictionary<UiWindowId, Entry> windows = new();

    public UiWindowManager(
        VisualElement left,
        VisualElement right,
        VisualElement bottom,
        VisualElement overlay)
    {
        docks[UiDockRegion.Left] = left ?? throw new ArgumentNullException(nameof(left));
        docks[UiDockRegion.Right] = right ?? throw new ArgumentNullException(nameof(right));
        docks[UiDockRegion.Bottom] = bottom ?? throw new ArgumentNullException(nameof(bottom));
        docks[UiDockRegion.Overlay] = overlay ?? throw new ArgumentNullException(nameof(overlay));
    }

    public void Register(UiWindowId id, UiDockRegion region, VisualElement view)
    {
        if (view == null)
            throw new ArgumentNullException(nameof(view));
        if (windows.ContainsKey(id))
            throw new InvalidOperationException($"Window '{id}' is already registered.");
        docks[region].Add(view);
        view.style.display = DisplayStyle.None;
        windows.Add(id, new Entry(region, view));
    }

    public bool IsOpen(UiWindowId id) =>
        windows.TryGetValue(id, out Entry entry) && entry.IsOpen;

    public void Open(UiWindowId id, Func<IDisposable> beginObservation = null)
    {
        Entry entry = Get(id);
        if (entry.IsOpen)
            return;
        entry.IsOpen = true;
        entry.View.style.display = DisplayStyle.Flex;
        IDisposable subscription = beginObservation?.Invoke();
        if (entry.IsOpen)
            entry.Subscription = subscription;
        else
            subscription?.Dispose();
    }

    public void Close(UiWindowId id)
    {
        Entry entry = Get(id);
        if (!entry.IsOpen)
            return;
        entry.Subscription?.Dispose();
        entry.Subscription = null;
        entry.IsOpen = false;
        entry.View.style.display = DisplayStyle.None;
    }

    public void Toggle(UiWindowId id, Func<IDisposable> beginObservation = null)
    {
        if (IsOpen(id))
            Close(id);
        else
            Open(id, beginObservation);
    }

    public void Dispose()
    {
        foreach (KeyValuePair<UiWindowId, Entry> pair in windows)
            pair.Value.Subscription?.Dispose();
        windows.Clear();
    }

    private Entry Get(UiWindowId id)
    {
        if (windows.TryGetValue(id, out Entry entry))
            return entry;
        throw new InvalidOperationException($"Window '{id}' is not registered.");
    }

    private sealed class Entry
    {
        public Entry(UiDockRegion region, VisualElement view)
        {
            Region = region;
            View = view;
        }

        public UiDockRegion Region { get; }
        public VisualElement View { get; }
        public IDisposable Subscription { get; set; }
        public bool IsOpen { get; set; }
    }
}
