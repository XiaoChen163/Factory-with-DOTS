using System.Linq;
using Unity.Entities;
using UnityEngine;

namespace Factory.Tests
{
    internal static class FactoryTestDatabase
    {
        // Rule tests require two recipes and a two-output recipe regardless of which
        // demonstration recipes are currently edited through the content manager.
        internal static FactoryBuildingDefinitionRow AddMultiOutputProcessor(FactoryDatabaseAsset asset)
        {
            var building = asset.buildings.Single(b => b.key == "furnace");
            building.id = checked((ushort)(asset.buildings.Max(b => b.id) + 1));
            building.key = "test_machine_m";
            building.machineTypeId = asset.machineTypes.Single(m => m.key == "test_machine_m").id;
            building.footprintWidth = building.footprintHeight = 3;
            building.isRectangle = true; building.footprintBitmap = "";
            building.ports = Enumerable.Range(0, 3).Select(n => new FactoryBuildingPortTableRow
            {
                type = BuildingPortType.Input, index = (byte)n, cellOffset = new Vector2Int(-1, n), direction = Vector2Int.right
            }).Concat(Enumerable.Range(0, 3).Select(n => new FactoryBuildingPortTableRow
            {
                type = BuildingPortType.Output, index = (byte)n, number = (ushort)(n + 1), outputSlot = (ushort)(n + 1),
                cellOffset = new Vector2Int(3, n), direction = Vector2Int.right
            })).ToArray();
            asset.buildings = asset.buildings.Append(building).ToArray();
            return building;
        }

        internal static void SetTwoOutputRecipes(FactoryDatabaseAsset asset, ushort machineId)
        {
            var a = asset.items.Single(i => i.key == "iron_ore");
            var b = asset.items.Single(i => i.key == "iron_ingot");
            string machineKey = asset.machineTypes.Single(m => m.id == machineId).key;
            ushort id = checked((ushort)(asset.recipes.Max(r => r.id) + 1));
            var first = new FactoryRecipeTableRow
            {
                id = id, key = "fixture_one", machineTypeId = machineId, machineTypeKey = machineKey, durationSeconds = 1,
                inputs = new[] { new FactoryRecipeIngredientTableRow { itemId = a.id, itemKey = a.key, count = 1 } },
                outputs = new[] { new FactoryRecipeIngredientTableRow { itemId = b.id, itemKey = b.key, count = 1 } }
            };
            var second = first;
            second.id = checked((ushort)(id + 1)); second.key = "fixture_two";
            second.outputs = new[] { new FactoryRecipeIngredientTableRow { itemId = a.id, itemKey = a.key, count = 1 }, first.outputs[0] };
            asset.recipes = asset.recipes.Where(r => r.machineTypeId != machineId).Concat(new[] { first, second }).ToArray();
        }

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
                            itemKey = "iron_ore",
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
