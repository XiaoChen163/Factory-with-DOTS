using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Hybrid.Baking;
using Unity.Rendering;

[TemporaryBakingType]
public struct BeltVisualPartsBakingData : IBufferElementData
{
    public Entity Prefab;
    public Entity EastEdge;
    public Entity NorthEdge;
    public Entity WestEdge;
    public Entity SouthEdge;
    public Entity DirectionTriangle;
    public Unity.Mathematics.quaternion TriangleBindRotation;
    public Unity.Mathematics.quaternion TriangleParentRotation;
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
                DirectionTriangle = mapping.DirectionTriangle,
                TriangleBindRotation = mapping.TriangleBindRotation,
                TriangleParentRotation = mapping.TriangleParentRotation
            };
            MapEdgeRenderEntities(mapping.EastEdge);
            MapEdgeRenderEntities(mapping.NorthEdge);
            MapEdgeRenderEntities(mapping.WestEdge);
            MapEdgeRenderEntities(mapping.SouthEdge);
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

    private void MapEdgeRenderEntities(Entity edge)
    {
        // Snapshot before AddBuffer, which invalidates live buffer references.
        using var additional = EntityManager.HasBuffer<AdditionalEntitiesBakingData>(edge)
            ? EntityManager.GetBuffer<AdditionalEntitiesBakingData>(edge, true).ToNativeArray(Allocator.Temp)
            : new NativeArray<AdditionalEntitiesBakingData>(0, Allocator.Temp);
        var renderers = EntityManager.HasBuffer<BeltEdgeRenderEntity>(edge)
            ? EntityManager.GetBuffer<BeltEdgeRenderEntity>(edge)
            : EntityManager.AddBuffer<BeltEdgeRenderEntity>(edge);
        renderers.Clear();
        foreach (var entry in additional)
            if (entry.Value != edge && EntityManager.HasComponent<RenderBounds>(entry.Value))
                renderers.Add(new BeltEdgeRenderEntity { Value = entry.Value });
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
