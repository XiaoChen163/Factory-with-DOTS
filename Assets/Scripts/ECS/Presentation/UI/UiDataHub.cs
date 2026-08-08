using System;
using System.Collections.Generic;
using Unity.Entities;

public interface IUiDataSource<TKey, TSnapshot>
{
    IDisposable Subscribe(TKey key, Action<TSnapshot> onChanged);
    bool TryGetLatest(TKey key, out TSnapshot snapshot);
}

public sealed class UiObservationRegistry
{
    private readonly Dictionary<PlayerId, int> players = new();
    private readonly Dictionary<BuildingRuntimeId, int> buildings = new();
    private int buildCatalogObservers;

    public IEnumerable<PlayerId> Players => players.Keys;
    public IEnumerable<BuildingRuntimeId> Buildings => buildings.Keys;
    public bool ObserveBuildCatalog => buildCatalogObservers > 0;
    public int PlayerCount => players.Count;
    public int BuildingCount => buildings.Count;

    internal void AddPlayer(PlayerId id) => Add(players, id);
    internal void RemovePlayer(PlayerId id) => Remove(players, id);
    internal void AddBuilding(BuildingRuntimeId id) => Add(buildings, id);
    internal void RemoveBuilding(BuildingRuntimeId id) => Remove(buildings, id);
    internal void AddBuildCatalog() => buildCatalogObservers++;
    internal void RemoveBuildCatalog() =>
        buildCatalogObservers = Math.Max(0, buildCatalogObservers - 1);

    private static void Add<TKey>(Dictionary<TKey, int> values, TKey key)
    {
        values.TryGetValue(key, out int count);
        values[key] = count + 1;
    }

    private static void Remove<TKey>(Dictionary<TKey, int> values, TKey key)
    {
        if (!values.TryGetValue(key, out int count))
            return;
        if (count <= 1)
            values.Remove(key);
        else
            values[key] = count - 1;
    }
}

public sealed class UiDataHub
{
    private readonly UiDataSource<PlayerId, InventorySnapshot> inventories;
    private readonly UiDataSource<BuildingRuntimeId, BuildingSnapshot> buildings;
    private readonly UiDataSource<byte, BuildCatalogSnapshot> buildCatalog;

    public UiDataHub()
    {
        Observations = new UiObservationRegistry();
        inventories = new UiDataSource<PlayerId, InventorySnapshot>(
            Observations.AddPlayer, Observations.RemovePlayer);
        buildings = new UiDataSource<BuildingRuntimeId, BuildingSnapshot>(
            Observations.AddBuilding, Observations.RemoveBuilding);
        buildCatalog = new UiDataSource<byte, BuildCatalogSnapshot>(
            _ => Observations.AddBuildCatalog(),
            _ => Observations.RemoveBuildCatalog());
    }

    public UiObservationRegistry Observations { get; }
    public IUiDataSource<PlayerId, InventorySnapshot> Inventories => inventories;
    public IUiDataSource<BuildingRuntimeId, BuildingSnapshot> Buildings => buildings;
    public IUiDataSource<byte, BuildCatalogSnapshot> BuildCatalog => buildCatalog;

    public void Publish(PlayerId key, InventorySnapshot value) =>
        inventories.Publish(key, value);
    public void Publish(BuildingRuntimeId key, BuildingSnapshot value) =>
        buildings.Publish(key, value);
    public void PublishBuildCatalog(BuildCatalogSnapshot value) =>
        buildCatalog.Publish(0, value);
}

internal sealed class UiDataSource<TKey, TSnapshot>
    : IUiDataSource<TKey, TSnapshot>
{
    private readonly Dictionary<TKey, List<Action<TSnapshot>>> callbacks = new();
    private readonly Dictionary<TKey, TSnapshot> latest = new();
    private readonly Action<TKey> onFirstSubscriber;
    private readonly Action<TKey> onLastSubscriber;

    public UiDataSource(Action<TKey> onFirstSubscriber, Action<TKey> onLastSubscriber)
    {
        this.onFirstSubscriber = onFirstSubscriber;
        this.onLastSubscriber = onLastSubscriber;
    }

    public IDisposable Subscribe(TKey key, Action<TSnapshot> onChanged)
    {
        if (onChanged == null)
            throw new ArgumentNullException(nameof(onChanged));
        if (!callbacks.TryGetValue(key, out List<Action<TSnapshot>> list))
        {
            list = new List<Action<TSnapshot>>();
            callbacks.Add(key, list);
            onFirstSubscriber?.Invoke(key);
        }
        list.Add(onChanged);
        if (latest.TryGetValue(key, out TSnapshot snapshot))
            onChanged(snapshot);
        return new Subscription(this, key, onChanged);
    }

    public bool TryGetLatest(TKey key, out TSnapshot snapshot) =>
        latest.TryGetValue(key, out snapshot);

    public void Publish(TKey key, TSnapshot snapshot)
    {
        latest[key] = snapshot;
        if (!callbacks.TryGetValue(key, out List<Action<TSnapshot>> list))
            return;
        Action<TSnapshot>[] stable = list.ToArray();
        for (int i = 0; i < stable.Length; i++)
            stable[i](snapshot);
    }

    private void Unsubscribe(TKey key, Action<TSnapshot> callback)
    {
        if (!callbacks.TryGetValue(key, out List<Action<TSnapshot>> list))
            return;
        list.Remove(callback);
        if (list.Count != 0)
            return;
        callbacks.Remove(key);
        onLastSubscriber?.Invoke(key);
    }

    private sealed class Subscription : IDisposable
    {
        private UiDataSource<TKey, TSnapshot> owner;
        private readonly TKey key;
        private readonly Action<TSnapshot> callback;

        public Subscription(
            UiDataSource<TKey, TSnapshot> owner,
            TKey key,
            Action<TSnapshot> callback)
        {
            this.owner = owner;
            this.key = key;
            this.callback = callback;
        }

        public void Dispose()
        {
            UiDataSource<TKey, TSnapshot> current = owner;
            owner = null;
            current?.Unsubscribe(key, callback);
        }
    }
}

public static class UiRuntimeServices
{
    private static readonly Dictionary<World, UiDataHub> hubs = new();

    public static void Attach(World world, UiDataHub hub)
    {
        if (world != null && hub != null)
            hubs[world] = hub;
    }

    public static void Detach(World world, UiDataHub hub)
    {
        if (world != null && hubs.TryGetValue(world, out UiDataHub current) &&
            ReferenceEquals(current, hub))
            hubs.Remove(world);
    }

    public static bool TryGet(World world, out UiDataHub hub)
    {
        if (world != null)
            return hubs.TryGetValue(world, out hub);
        hub = null;
        return false;
    }
}
