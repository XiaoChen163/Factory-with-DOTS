using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class GridDefinitionAuthoring : MonoBehaviour
{
    [SerializeField] private Vector2Int size = new Vector2Int(32, 32);
    [SerializeField] private Vector3 origin = Vector3.zero;
    [SerializeField, Min(0.01f)] private float layerHeight =
        EcsGridUtility.DefaultLayerHeight;

    private void OnValidate()
    {
        size.x = Mathf.Max(1, size.x);
        size.y = Mathf.Max(1, size.y);

        // Integer X/Z origins keep the runtime grid on Unity's editor grid
        // lines when one grid cell is exactly one Unity unit.
        origin.x = Mathf.Round(origin.x);
        origin.z = Mathf.Round(origin.z);
        layerHeight = Mathf.Max(0.01f, layerHeight);
    }

    private sealed class GridDefinitionBaker
        : Baker<GridDefinitionAuthoring>
    {
        public override void Bake(GridDefinitionAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            Vector3 configuredOrigin = authoring.origin;

            AddComponent(entity, new GridDefinition
            {
                Size = new int2(
                    Mathf.Max(1, authoring.size.x),
                    Mathf.Max(1, authoring.size.y)),
                Origin = new float3(
                    Mathf.Round(configuredOrigin.x),
                    configuredOrigin.y,
                    Mathf.Round(configuredOrigin.z)),
                CellSize = EcsGridUtility.DefaultCellSize,
                Revision = 1
            });
            AddComponent(entity, new WorldGridConfig
            {
                CellSize = EcsGridUtility.DefaultCellSize,
                LayerHeight = Mathf.Max(0.01f, authoring.layerHeight),
                Origin = new float3(
                    Mathf.Round(configuredOrigin.x),
                    configuredOrigin.y,
                    Mathf.Round(configuredOrigin.z))
            });
            AddComponent(entity, new BuildingRuntimeIdAllocator
            {
                NextValue = 0x8000000000000000UL
            });
            AddBuffer<GridBuildCommand>(entity);
            AddBuffer<PlayerGridCommandPending>(entity);
            AddComponent(entity, new PlayerGridCommandAdapterState
            {
                NextGridRequestId = 1
            });
            AddBuffer<GridBuildResult>(entity);
        }
    }
}
