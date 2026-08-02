using Unity.Collections;
using Unity.Entities;

[TemporaryBakingType]
public struct BeltVisualPartsBakingData : IBufferElementData
{
    public Entity Prefab;
    public Entity EastEdge;
    public Entity NorthEdge;
    public Entity WestEdge;
    public Entity SouthEdge;
    public Entity DirectionTriangle;
}

[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
[UpdateInGroup(typeof(BakingSystemGroup))]
public partial class BeltVisualPartsBakingSystem : SystemBase
{
    private EntityQuery mappingQuery;

    protected override void OnCreate()
    {
        mappingQuery = GetEntityQuery(
            ComponentType.ReadOnly<BeltVisualPartsBakingData>());
        RequireForUpdate(mappingQuery);
    }

    protected override void OnUpdate()
    {
        using NativeArray<Entity> mappingEntities =
            mappingQuery.ToEntityArray(Allocator.Temp);
        using NativeList<BeltVisualPartsBakingData> mappings =
            new NativeList<BeltVisualPartsBakingData>(Allocator.Temp);
        for (int entityIndex = 0;
             entityIndex < mappingEntities.Length;
             entityIndex++)
        {
            DynamicBuffer<BeltVisualPartsBakingData> source =
                EntityManager.GetBuffer<BeltVisualPartsBakingData>(
                    mappingEntities[entityIndex],
                    true);
            for (int i = 0; i < source.Length; i++)
            {
                mappings.Add(source[i]);
            }
        }

        for (int i = 0; i < mappings.Length; i++)
        {
            BeltVisualPartsBakingData mapping = mappings[i];
            if (!IsValid(mapping))
            {
                continue;
            }

            BeltVisualParts parts = new BeltVisualParts
            {
                EastEdge = mapping.EastEdge,
                NorthEdge = mapping.NorthEdge,
                WestEdge = mapping.WestEdge,
                SouthEdge = mapping.SouthEdge,
                DirectionTriangle = mapping.DirectionTriangle
            };
            if (EntityManager.HasComponent<BeltVisualParts>(
                    mapping.Prefab))
            {
                EntityManager.SetComponentData(mapping.Prefab, parts);
            }
            else
            {
                EntityManager.AddComponentData(mapping.Prefab, parts);
            }
        }
    }

    private bool IsValid(in BeltVisualPartsBakingData mapping)
    {
        Entity[] entities =
        {
            mapping.Prefab,
            mapping.EastEdge,
            mapping.NorthEdge,
            mapping.WestEdge,
            mapping.SouthEdge,
            mapping.DirectionTriangle
        };
        for (int i = 0; i < entities.Length; i++)
        {
            if (entities[i] == Entity.Null ||
                !EntityManager.Exists(entities[i]))
            {
                return false;
            }
        }

        return true;
    }
}
