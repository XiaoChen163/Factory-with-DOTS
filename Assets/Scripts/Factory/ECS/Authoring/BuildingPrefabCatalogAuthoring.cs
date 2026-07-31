using System;
using Unity.Entities;
using UnityEngine;

[Serializable]
public struct ItemPrefabCatalogAuthoringEntry
{
    public GameObject itemType;
    public GameObject itemPrefab;
}

[DisallowMultipleComponent]
public sealed class BuildingPrefabCatalogAuthoring : MonoBehaviour
{
    public GameObject beltPrefab;
    public GameObject minerPrefab;
    public GameObject furnacePrefab;
    public GameObject storagePrefab;
    public GameObject mergerPrefab;
    public GameObject splitterPrefab;
    public ItemPrefabCatalogAuthoringEntry[] itemPrefabs;

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

            DynamicBuffer<ItemPrefabEntry> itemPrefabs =
                AddBuffer<ItemPrefabEntry>(entity);
            if (authoring.itemPrefabs == null)
            {
                return;
            }

            for (int i = 0; i < authoring.itemPrefabs.Length; i++)
            {
                ItemPrefabCatalogAuthoringEntry entry =
                    authoring.itemPrefabs[i];
                itemPrefabs.Add(new ItemPrefabEntry
                {
                    ItemType = entry.itemType == null
                        ? Entity.Null
                        : GetEntity(
                            entry.itemType,
                            TransformUsageFlags.None),
                    Prefab = entry.itemPrefab == null
                        ? Entity.Null
                        : GetEntity(
                            entry.itemPrefab,
                            TransformUsageFlags.Dynamic)
                });
            }
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
