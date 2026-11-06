using Unity.Collections;
using Unity.Entities;

[BakingType]
public struct ItemModelSourceBakingData : IBufferElementData
{
    public ItemId ItemType;
    public Entity Source;
}

[BakingType]
public struct GeneratedItemTemplate : IComponentData { }

// Clone the baked model graph per item identity. Mesh/material assets remain shared;
// Item IDs and pool instances never mutate a shared FBX source template.
[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(PostBakingSystemGroup))]
public partial class ItemPrefabBakingSystem : SystemBase
{
    private EntityQuery catalogs, generated;
    protected override void OnCreate()
    {
        catalogs = GetEntityQuery(typeof(ItemModelSourceBakingData), typeof(ItemPrefabEntry));
        generated = GetEntityQuery(new EntityQueryDesc
        {
            All = new[] { ComponentType.ReadOnly<GeneratedItemTemplate>() },
            Options = EntityQueryOptions.IncludePrefab | EntityQueryOptions.IncludeDisabledEntities
        });
    }

    protected override void OnUpdate()
    {
        // Baking systems may run repeatedly during incremental baking.
        using (var previous = generated.ToEntityArray(Allocator.Temp))
            foreach (var root in previous) EntityManager.DestroyEntity(root);
        using var owners = catalogs.ToEntityArray(Allocator.Temp);
        foreach (var owner in owners)
        {
            using var sources = EntityManager.GetBuffer<ItemModelSourceBakingData>(owner, true).ToNativeArray(Allocator.Temp);
            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                Entity clone = EntityManager.Instantiate(source.Source);
                EntityManager.AddComponent<GeneratedItemTemplate>(clone);
                EntityManager.AddComponentData(clone, new Item { ItemType = source.ItemType });
                EntityManager.AddComponentData(clone, new ItemVisualState());
                using var members = new NativeList<Entity>(Allocator.Temp);
                if (EntityManager.HasBuffer<LinkedEntityGroup>(clone))
                {
                    var linked = EntityManager.GetBuffer<LinkedEntityGroup>(clone);
                    foreach (var member in linked) members.Add(member.Value);
                }
                else members.Add(clone);
                foreach (var member in members)
                {
                    if (!EntityManager.HasComponent<Prefab>(member)) EntityManager.AddComponent<Prefab>(member);
                    if (EntityManager.HasComponent<EntityGuid>(member))
                    {
                        var guid = EntityManager.GetComponentData<EntityGuid>(member);
                        // Stable across incremental updates; separate from FBX source GUIDs.
                        guid.b = ((ulong)(0xFA000000u | source.ItemType.Value) << 32) | guid.Serial;
                        EntityManager.SetComponentData(member, guid);
                    }
                }
                var entries = EntityManager.GetBuffer<ItemPrefabEntry>(owner);
                entries[i] = new ItemPrefabEntry { ItemType = source.ItemType, Prefab = clone };
            }
        }
    }
}
