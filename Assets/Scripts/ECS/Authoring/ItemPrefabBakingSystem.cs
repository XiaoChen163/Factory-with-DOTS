using Unity.Collections;
using Unity.Entities;

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(BakingSystemGroup))]
public partial class ItemPrefabBakingSystem : SystemBase
{
    private EntityQuery catalogQuery;

    protected override void OnCreate()
    {
        catalogQuery = GetEntityQuery(
            ComponentType.ReadOnly<BuildingPrefabCatalog>(),
            ComponentType.ReadOnly<ItemPrefabEntry>());
        RequireForUpdate(catalogQuery);
    }

    protected override void OnUpdate()
    {
        using NativeArray<Entity> catalogs =
            catalogQuery.ToEntityArray(Allocator.Temp);
        for (int catalogIndex = 0;
             catalogIndex < catalogs.Length;
             catalogIndex++)
        {
            DynamicBuffer<ItemPrefabEntry> entries =
                EntityManager.GetBuffer<ItemPrefabEntry>(
                    catalogs[catalogIndex],
                    true);
            using NativeList<ItemPrefabEntry> snapshot =
                new NativeList<ItemPrefabEntry>(
                    entries.Length,
                    Allocator.Temp);
            for (int i = 0; i < entries.Length; i++)
            {
                snapshot.Add(entries[i]);
            }

            for (int i = 0; i < snapshot.Length; i++)
            {
                ItemPrefabEntry entry = snapshot[i];
                if (!entry.ItemType.IsValid ||
                    entry.Prefab == Entity.Null ||
                    !EntityManager.Exists(entry.Prefab))
                {
                    continue;
                }

                Item item = new Item
                {
                    ItemType = entry.ItemType,
                    Position = default
                };
                if (EntityManager.HasComponent<Item>(entry.Prefab))
                {
                    EntityManager.SetComponentData(entry.Prefab, item);
                }
                else
                {
                    EntityManager.AddComponentData(entry.Prefab, item);
                }
            }
        }
    }
}
