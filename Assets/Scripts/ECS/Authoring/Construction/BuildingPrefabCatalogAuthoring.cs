using Unity.Entities;
using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public sealed class BuildingPrefabCatalogAuthoring : MonoBehaviour
{
    public FactoryDatabaseAsset database;
    public GameObject inputPortVisualPrefab;
    public GameObject outputPortVisualPrefab;
    public GameObject rampFoundationVisualPrefab;

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
            if (authoring.rampFoundationVisualPrefab != null)
                DependsOn(authoring.rampFoundationVisualPrefab);
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
                    authoring.outputPortVisualPrefab),
                RampFoundationVisual = GetPrefabEntity(
                    authoring.rampFoundationVisualPrefab)
            });

            DynamicBuffer<BuildingVisualPrefabEntry> buildingPrefabs =
                AddBuffer<BuildingVisualPrefabEntry>(entity);
            DynamicBuffer<FoundationVisualMaterial> foundationMaterials =
                AddBuffer<FoundationVisualMaterial>(entity);
            DynamicBuffer<FoundationDefinitionMaterial> foundationDefinitionMaterials =
                AddBuffer<FoundationDefinitionMaterial>(entity);
            DynamicBuffer<BeltVisualPartsBakingData> beltVisualParts =
                AddBuffer<BeltVisualPartsBakingData>(entity);
            HashSet<GameObject> configuredBeltVisuals =
                new HashSet<GameObject>();
            for (int i = 0; i < authoring.database.buildings.Length; i++)
            {
                FactoryBuildingDefinitionRow definition =
                    authoring.database.buildings[i];
                DependsOn(definition.visualPrefab);
                Entity visualPrefab = GetPrefabEntity(definition.visualPrefab);
                var collider = FactoryBuildingColliderBuilder.Build(definition.colliders);
                if (collider.IsCreated) AddBlobAsset(ref collider, out _);
                buildingPrefabs.Add(new BuildingVisualPrefabEntry
                {
                    BuildingDefinition = new BuildingDefinitionId { Value = definition.id },
                    Prefab = visualPrefab,
                    PreviewPrefab = definition.visualPrefab,
                    Collider = collider
                });

                if (definition.kind == BuildingKind.Foundation)
                {
                    MeshRenderer[] renderers =
                        definition.visualPrefab.GetComponentsInChildren<MeshRenderer>(true);
                    if (renderers.Length != 1 ||
                        renderers[0].sharedMaterials.Length != 1 ||
                        renderers[0].sharedMaterial == null)
                    {
                        Debug.LogError(
                            $"Foundation definition '{definition.key}' must use exactly one visual material.",
                            authoring);
                        return;
                    }
                    ushort materialId = (ushort)foundationMaterials.Length;
                    foundationMaterials.Add(new FoundationVisualMaterial
                    {
                        Value = (UnityObjectRef<Material>)renderers[0].sharedMaterial
                    });
                    foundationDefinitionMaterials.Add(new FoundationDefinitionMaterial
                    {
                        BuildingDefinition = new BuildingDefinitionId { Value = definition.id },
                        VisualMaterialId = materialId
                    });
                }

                if (definition.kind == BuildingKind.Belt &&
                    configuredBeltVisuals.Add(definition.visualPrefab))
                {
                    AddBeltVisualPartsMapping(
                        definition.visualPrefab,
                        visualPrefab,
                        beltVisualParts,
                        authoring);
                }
            }

            DynamicBuffer<ItemPrefabEntry> itemPrefabs =
                AddBuffer<ItemPrefabEntry>(entity);
            var itemSources = AddBuffer<ItemModelSourceBakingData>(entity);
            for (int i = 0; i < authoring.database.items.Length; i++)
            {
                FactoryItemTableRow item = authoring.database.items[i];
                Entity itemPrefab = GetEntity(item.model, TransformUsageFlags.Dynamic);
                itemSources.Add(new ItemModelSourceBakingData { Source = itemPrefab, ItemType = new ItemId { Value = item.id } });
                itemPrefabs.Add(new ItemPrefabEntry
                {
                    ItemType = new ItemId { Value = item.id },
                    Prefab = Entity.Null
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
                if (item.model != null)
                {
                    DependsOn(item.model);
                }

                if (item.id == 0 ||
                    !itemIds.Add(item.id) ||
                    string.IsNullOrWhiteSpace(item.modelPath) ||
                    item.model == null)
                {
                    Debug.LogError(
                        $"Generated item row '{item.key}' has an invalid " +
                        $"id or unresolved prefab key '{item.modelPath}'.",
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
                DirectionTriangle = GetRenderableEntity(directionTriangle),
                TriangleBindRotation = directionTriangle.localRotation,
                TriangleParentRotation = Quaternion.Inverse(prefab.transform.rotation) * directionTriangle.parent.rotation
            });
        }

        private Transform FindRequiredChild(
            GameObject prefab,
            string childName,
            BuildingPrefabCatalogAuthoring authoring)
        {
            Transform child = FactoryBuildingVisualUtility.FindPart(prefab, childName);
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
                TransformUsageFlags.Dynamic);
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

// FBX exporters may add grouping nodes. Names must be unique throughout the model.
public static class FactoryBuildingVisualUtility
{
    public static Transform FindPart(GameObject model, string name)
    {
        if (model == null) return null;
        Transform match = null;
        foreach (var child in model.GetComponentsInChildren<Transform>(true))
        {
            if (child == model.transform || child.name != name) continue;
            if (match != null) throw new System.IO.InvalidDataException($"模型 '{model.name}' 子物体名称重复：{name}");
            match = child;
        }
        return match;
    }
}
