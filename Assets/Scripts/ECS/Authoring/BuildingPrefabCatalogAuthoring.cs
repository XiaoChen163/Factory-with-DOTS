using Unity.Entities;
using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public sealed class BuildingPrefabCatalogAuthoring : MonoBehaviour
{
    public FactoryDatabaseAsset database;
    public GameObject inputPortVisualPrefab;
    public GameObject outputPortVisualPrefab;

    private sealed class BuildingPrefabCatalogBaker
        : Baker<BuildingPrefabCatalogAuthoring>
    {
        private const string EastEdgeName = "Connection Edge East";
        private const string NorthEdgeName = "Connection Edge North";
        private const string WestEdgeName = "Connection Edge West";
        private const string SouthEdgeName = "Connection Edge South";
        private const string DirectionTriangleName = "Direction Triangle";

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
                InputPortVisual = GetPrefabEntity(
                    authoring.inputPortVisualPrefab),
                OutputPortVisual = GetPrefabEntity(
                    authoring.outputPortVisualPrefab)
            });

            DynamicBuffer<BuildingVisualPrefabEntry> buildingPrefabs =
                AddBuffer<BuildingVisualPrefabEntry>(entity);
            DynamicBuffer<FoundationVisualMaterial> foundationMaterials =
                AddBuffer<FoundationVisualMaterial>(entity);
            DynamicBuffer<FoundationLevelMaterial> foundationLevelMaterials =
                AddBuffer<FoundationLevelMaterial>(entity);
            DynamicBuffer<BeltVisualPartsBakingData> beltVisualParts =
                AddBuffer<BeltVisualPartsBakingData>(entity);
            HashSet<GameObject> configuredBeltVisuals =
                new HashSet<GameObject>();
            for (int i = 0; i < authoring.database.buildingLevels.Length; i++)
            {
                FactoryBuildingLevelTableRow level =
                    authoring.database.buildingLevels[i];
                DependsOn(level.visualPrefab);
                Entity visualPrefab = GetPrefabEntity(level.visualPrefab);
                buildingPrefabs.Add(new BuildingVisualPrefabEntry
                {
                    BuildingLevel = new BuildingLevelId { Value = level.id },
                    Prefab = visualPrefab
                });

                if (TryGetBuildingKind(
                        authoring.database,
                        level.buildingId,
                        out BuildingKind levelKind) &&
                    levelKind == BuildingKind.Foundation)
                {
                    MeshRenderer[] renderers =
                        level.visualPrefab.GetComponentsInChildren<MeshRenderer>(true);
                    if (renderers.Length != 1 ||
                        renderers[0].sharedMaterials.Length != 1 ||
                        renderers[0].sharedMaterial == null)
                    {
                        Debug.LogError(
                            $"Foundation level '{level.key}' must use exactly one visual material.",
                            authoring);
                        return;
                    }
                    ushort materialId = (ushort)foundationMaterials.Length;
                    foundationMaterials.Add(new FoundationVisualMaterial
                    {
                        Value = (UnityObjectRef<Material>)renderers[0].sharedMaterial
                    });
                    foundationLevelMaterials.Add(new FoundationLevelMaterial
                    {
                        BuildingLevel = new BuildingLevelId { Value = level.id },
                        VisualMaterialId = materialId
                    });
                }

                if (TryGetBuildingKind(
                        authoring.database,
                        level.buildingId,
                        out BuildingKind kind) &&
                    kind == BuildingKind.Belt &&
                    configuredBeltVisuals.Add(level.visualPrefab))
                {
                    AddBeltVisualPartsMapping(
                        level.visualPrefab,
                        visualPrefab,
                        beltVisualParts,
                        authoring);
                }
            }

            DynamicBuffer<ItemPrefabEntry> itemPrefabs =
                AddBuffer<ItemPrefabEntry>(entity);
            for (int i = 0; i < authoring.database.items.Length; i++)
            {
                FactoryItemTableRow item = authoring.database.items[i];
                Entity itemPrefab = GetEntity(
                    item.prefab,
                    TransformUsageFlags.Dynamic);
                itemPrefabs.Add(new ItemPrefabEntry
                {
                    ItemType = new ItemId { Value = item.id },
                    Prefab = itemPrefab
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
            System.Collections.Generic.HashSet<GameObject> itemPrefabs =
                new System.Collections.Generic.HashSet<GameObject>();
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
                    item.prefab == null ||
                    !itemPrefabs.Add(item.prefab))
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

        private void AddBeltVisualPartsMapping(
            GameObject prefab,
            Entity prefabEntity,
            DynamicBuffer<BeltVisualPartsBakingData> mappings,
            BuildingPrefabCatalogAuthoring authoring)
        {
            Transform eastEdge = FindRequiredChild(
                prefab,
                EastEdgeName,
                authoring);
            Transform northEdge = FindRequiredChild(
                prefab,
                NorthEdgeName,
                authoring);
            Transform westEdge = FindRequiredChild(
                prefab,
                WestEdgeName,
                authoring);
            Transform southEdge = FindRequiredChild(
                prefab,
                SouthEdgeName,
                authoring);
            Transform directionTriangle = FindRequiredChild(
                prefab,
                DirectionTriangleName,
                authoring);
            if (eastEdge == null ||
                northEdge == null ||
                westEdge == null ||
                southEdge == null ||
                directionTriangle == null)
            {
                return;
            }

            mappings.Add(new BeltVisualPartsBakingData
            {
                Prefab = prefabEntity,
                EastEdge = GetRenderableEntity(eastEdge),
                NorthEdge = GetRenderableEntity(northEdge),
                WestEdge = GetRenderableEntity(westEdge),
                SouthEdge = GetRenderableEntity(southEdge),
                DirectionTriangle = GetRenderableEntity(directionTriangle)
            });
        }

        private Transform FindRequiredChild(
            GameObject prefab,
            string childName,
            BuildingPrefabCatalogAuthoring authoring)
        {
            Transform child = prefab == null
                ? null
                : prefab.transform.Find(childName);
            if (child == null)
            {
                Debug.LogError(
                    $"Belt visual prefab '{prefab?.name}' is missing " +
                    $"required child '{childName}'.",
                    authoring);
                return null;
            }

            DependsOn(child.gameObject);
            return child;
        }

        private Entity GetRenderableEntity(Transform transform)
        {
            return GetEntity(
                transform.gameObject,
                TransformUsageFlags.Renderable);
        }

        private static bool TryGetBuildingKind(
            FactoryDatabaseAsset database,
            ushort buildingId,
            out BuildingKind kind)
        {
            for (int i = 0; i < database.buildings.Length; i++)
            {
                FactoryBuildingTableRow building = database.buildings[i];
                if (building.id == buildingId)
                {
                    kind = building.kind;
                    return true;
                }
            }

            kind = default;
            return false;
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
