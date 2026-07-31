using Unity.Entities;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BuildingPrefabCatalogAuthoring : MonoBehaviour
{
    public GameObject beltPrefab;
    public GameObject minerPrefab;
    public GameObject furnacePrefab;
    public GameObject storagePrefab;
    public GameObject mergerPrefab;
    public GameObject splitterPrefab;

    private sealed class BuildingPrefabCatalogBaker
        : Baker<BuildingPrefabCatalogAuthoring>
    {
        public override void Bake(
            BuildingPrefabCatalogAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new BuildingPrefabCatalog
            {
                Belt = GetPrefabEntity(authoring.beltPrefab),
                Miner = GetPrefabEntity(authoring.minerPrefab),
                Furnace = GetPrefabEntity(authoring.furnacePrefab),
                Storage = GetPrefabEntity(authoring.storagePrefab),
                Merger = GetPrefabEntity(authoring.mergerPrefab),
                Splitter = GetPrefabEntity(authoring.splitterPrefab)
            });
        }

        private Entity GetPrefabEntity(GameObject prefab)
        {
            return prefab == null
                ? Entity.Null
                : GetEntity(
                    prefab,
                    TransformUsageFlags.Dynamic);
        }
    }
}
