using Unity.Entities;
using UnityEngine;

namespace Factory.Tests
{
    internal static class FactoryTestDatabase
    {
        internal static BlobAssetReference<FactoryDatabaseBlob> CreateDatabase()
        {
            FactoryDatabaseAsset asset =
                ScriptableObject.CreateInstance<FactoryDatabaseAsset>();
            GameObject visualPrefab = new GameObject("Storage Visual");
            asset.items = new[]
            {
                new FactoryItemTableRow
                {
                    id = 1,
                    key = "iron_ore",
                    nameKey = "iron_ore",
                    maxStack = 1,
                    category = FactoryItemCategory.Ore
                }
            };
            asset.machineTypes = new[]
            {
                new FactoryMachineTypeTableRow
                {
                    id = 1,
                    key = "furnace"
                }
            };
            asset.buildings = new[]
            {
                new FactoryBuildingDefinitionRow
                {
                    id = 1,
                    key = "storage",
                    kind = BuildingKind.Storage,
                    footprintWidth = 1,
                    footprintHeight = 1,
                    isRectangle = true,
                    visualPrefab = visualPrefab,
                    storageCapacity = 10,
                    storageSlotCount = 2,
                    ports = new[]
                    {
                        new FactoryBuildingPortTableRow
                        {
                            type = BuildingPortType.Input,
                            index = 0,
                            cellOffset = Vector2Int.left,
                            direction = Vector2Int.right
                        }
                    }
                }
            };
            asset.recipes = new[]
            {
                new FactoryRecipeTableRow
                {
                    id = 1,
                    key = "smelt",
                    machineTypeKey = "furnace",
                    machineTypeId = 1,
                    durationSeconds = 1f,
                    inputs = new[]
                    {
                        new FactoryRecipeIngredientTableRow
                        {
                            itemKey = "iron_ore",
                            itemId = 1,
                            count = 1
                        }
                    },
                    outputs = new[]
                    {
                        new FactoryRecipeIngredientTableRow
                        {
                            itemKey = "iron_ingot",
                            itemId = 1,
                            count = 1
                        }
                    }
                }
            };

            if (!FactoryDatabaseBakingUtility.TryBuild(
                    asset,
                    null,
                    out BlobAssetReference<FactoryDatabaseBlob> result))
            {
                throw new System.InvalidOperationException(
                    "Failed to build minimal factory database.");
            }

            Object.DestroyImmediate(visualPrefab);
            Object.DestroyImmediate(asset);
            return result;
        }
    }
}
