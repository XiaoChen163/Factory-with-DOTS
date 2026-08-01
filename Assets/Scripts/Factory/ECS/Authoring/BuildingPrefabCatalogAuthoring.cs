using Unity.Entities;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BuildingPrefabCatalogAuthoring : MonoBehaviour
{
    public FactoryDatabaseAsset database;
    public GameObject beltPrefab;
    public GameObject minerPrefab;
    public GameObject furnacePrefab;
    public GameObject storagePrefab;
    public GameObject mergerPrefab;
    public GameObject splitterPrefab;
    public GameObject inputPortVisualPrefab;
    public GameObject outputPortVisualPrefab;

    private sealed class BuildingPrefabCatalogBaker
        : Baker<BuildingPrefabCatalogAuthoring>
    {
        public override void Bake(
            BuildingPrefabCatalogAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            if (authoring.database == null)
            {
                Debug.LogError("Factory database asset is missing.", authoring);
                return;
            }
            if (authoring.inputPortVisualPrefab == null ||
                authoring.outputPortVisualPrefab == null)
            {
                Debug.LogError(
                    "Input and output port visual prefabs are required.",
                    authoring);
                return;
            }

            DependsOn(authoring.database);
            DependsOn(authoring.inputPortVisualPrefab);
            DependsOn(authoring.outputPortVisualPrefab);
            if (!ValidateItemPrefabBindings(authoring))
            {
                return;
            }

            if (!FactoryDatabaseBakingUtility.TryBuild(
                    authoring.database,
                    authoring,
                    out BlobAssetReference<FactoryDatabaseBlob> database))
            {
                return;
            }

            AddBlobAsset(ref database, out _);
            AddComponent(entity, new FactoryDatabase
            {
                Value = database
            });
            AddComponent(entity, new BuildingPrefabCatalog
            {
                Belt = GetPrefabEntity(authoring.beltPrefab),
                Miner = GetPrefabEntity(authoring.minerPrefab),
                Furnace = GetPrefabEntity(authoring.furnacePrefab),
                Storage = GetPrefabEntity(authoring.storagePrefab),
                Merger = GetPrefabEntity(authoring.mergerPrefab),
                Splitter = GetPrefabEntity(authoring.splitterPrefab),
                InputPortVisual = GetPrefabEntity(
                    authoring.inputPortVisualPrefab),
                OutputPortVisual = GetPrefabEntity(
                    authoring.outputPortVisualPrefab)
            });

            DynamicBuffer<ItemPrefabEntry> itemPrefabs =
                AddBuffer<ItemPrefabEntry>(entity);
            for (int i = 0; i < authoring.database.items.Length; i++)
            {
                FactoryItemTableRow item = authoring.database.items[i];
                itemPrefabs.Add(new ItemPrefabEntry
                {
                    ItemType = new ItemId { Value = item.id },
                    Prefab = GetEntity(
                        item.prefab,
                        TransformUsageFlags.Dynamic)
                });
            }
        }

        private bool ValidateItemPrefabBindings(
            BuildingPrefabCatalogAuthoring authoring)
        {
            if (authoring.database == null ||
                authoring.database.items == null)
            {
                Debug.LogError(
                    "Factory database or generated item rows are missing.",
                    authoring);
                return false;
            }

            bool valid = true;
            System.Collections.Generic.HashSet<ushort> itemIds =
                new System.Collections.Generic.HashSet<ushort>();
            for (int i = 0; i < authoring.database.items.Length; i++)
            {
                FactoryItemTableRow item = authoring.database.items[i];
                if (item.prefab != null)
                {
                    DependsOn(item.prefab);
                }

                if (item.id == 0 ||
                    !itemIds.Add(item.id) ||
                    string.IsNullOrWhiteSpace(item.prefabKey) ||
                    item.prefab == null)
                {
                    Debug.LogError(
                        $"Generated item row '{item.key}' has an invalid " +
                        $"id or unresolved prefab key '{item.prefabKey}'.",
                        authoring);
                    valid = false;
                }
            }

            return valid;
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
