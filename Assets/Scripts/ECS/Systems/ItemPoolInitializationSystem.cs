using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Creates one ItemPool entity per ItemPrefabEntry. The pool starts empty;
/// the first building outputs still instantiate items, after which consumed
/// items are returned to the pool and reused without structural changes.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class ItemPoolInitializationSystem : SystemBase
{
    private EntityQuery catalogQuery;
    private EntityQuery poolQuery;
    private readonly HashSet<ItemId> knownItemTypes =
        new HashSet<ItemId>();

    protected override void OnCreate()
    {
        catalogQuery = GetEntityQuery(
            ComponentType.ReadOnly<BuildingPrefabCatalog>(),
            ComponentType.ReadOnly<ItemPrefabEntry>());
        poolQuery = GetEntityQuery(
            ComponentType.ReadOnly<ItemPool>(),
            ComponentType.ReadOnly<ItemPoolEntry>());
        RequireForUpdate(catalogQuery);
    }

    protected override void OnUpdate()
    {
        if (catalogQuery.CalculateEntityCount() != 1)
        {
            return;
        }

        knownItemTypes.Clear();
        using NativeArray<ItemPool> pools =
            poolQuery.ToComponentDataArray<ItemPool>(Allocator.Temp);
        for (int i = 0; i < pools.Length; i++)
        {
            knownItemTypes.Add(pools[i].ItemType);
        }

        DynamicBuffer<ItemPrefabEntry> entries =
            EntityManager.GetBuffer<ItemPrefabEntry>(
                catalogQuery.GetSingletonEntity(),
                true);
        for (int i = 0; i < entries.Length; i++)
        {
            ItemPrefabEntry entry = entries[i];
            if (!entry.ItemType.IsValid ||
                entry.Prefab == Entity.Null ||
                knownItemTypes.Contains(entry.ItemType))
            {
                continue;
            }

            Entity pool = EntityManager.CreateEntity(
                typeof(ItemPool),
                typeof(ItemPoolEntry));
            EntityManager.SetComponentData(pool, new ItemPool
            {
                ItemType = entry.ItemType,
                Prefab = entry.Prefab,
                FreeCursor = 0
            });
            knownItemTypes.Add(entry.ItemType);
        }
    }
}
