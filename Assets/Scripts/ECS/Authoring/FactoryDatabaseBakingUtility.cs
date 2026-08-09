using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public static class FactoryDatabaseBakingUtility
{
    public static bool TryBuild(
        FactoryDatabaseAsset source,
        UnityEngine.Object context,
        out BlobAssetReference<FactoryDatabaseBlob> result)
    {
        result = default;
        if (!Validate(source, context))
            return false;

        List<FactoryRecipeTableRow> recipes = new List<FactoryRecipeTableRow>(source.recipes);
        recipes.Sort((left, right) =>
        {
            int machine = left.machineTypeId.CompareTo(right.machineTypeId);
            return machine != 0 ? machine : left.id.CompareTo(right.id);
        });

        int maxItemId = MaxId(source.items, row => row.id);
        int maxMachineId = MaxId(source.machineTypes, row => row.id);
        int maxBuildingId = MaxId(source.buildings, row => row.id);
        int maxLevelId = MaxId(source.buildingLevels, row => row.id);
        int inputCount = 0;
        int outputCount = 0;
        int portCount = 0;
        foreach (FactoryRecipeTableRow recipe in recipes)
        {
            inputCount += recipe.inputs?.Length ?? 0;
            outputCount += recipe.outputs?.Length ?? 0;
        }
        foreach (FactoryBuildingTableRow building in source.buildings)
            portCount += building.ports?.Length ?? 0;

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref FactoryDatabaseBlob root = ref builder.ConstructRoot<FactoryDatabaseBlob>();
        root.Version = source.version;

        BlobBuilderArray<FactoryItemBlob> items =
            builder.Allocate(ref root.ItemsById, maxItemId + 1);
        foreach (FactoryItemTableRow row in source.items)
        {
            items[row.id] = new FactoryItemBlob
            {
                Id = new ItemId { Value = row.id },
                MaxStack = row.maxStack,
                Category = (byte)row.category,
                Key = new FixedString64Bytes(row.key),
                NameKey = new FixedString64Bytes(row.nameKey)
            };
        }

        BlobBuilderArray<FactoryMachineTypeBlob> machines =
            builder.Allocate(ref root.MachineTypesById, maxMachineId + 1);
        foreach (FactoryMachineTypeTableRow row in source.machineTypes)
        {
            machines[row.id] = new FactoryMachineTypeBlob
            {
                Id = new MachineTypeId { Value = row.id },
                Key = new FixedString64Bytes(row.key)
            };
        }

        BlobBuilderArray<FactoryBuildingPortBlob> ports =
            builder.Allocate(ref root.BuildingPorts, portCount);
        BlobBuilderArray<FactoryBuildingBlob> buildings =
            builder.Allocate(ref root.BuildingsById, maxBuildingId + 1);
        int portCursor = 0;
        foreach (FactoryBuildingTableRow row in source.buildings)
        {
            FactoryBuildingPortTableRow[] rowPorts =
                row.ports ?? Array.Empty<FactoryBuildingPortTableRow>();
            buildings[row.id] = new FactoryBuildingBlob
            {
                Id = new BuildingTypeId { Value = row.id },
                Kind = row.kind,
                MachineType = new MachineTypeId { Value = row.machineTypeId },
                PortStart = portCursor,
                PortCount = (ushort)rowPorts.Length,
                FootprintWidth = row.footprintWidth,
                FootprintHeight = row.footprintHeight,
                Key = new FixedString64Bytes(row.key),
                NameKey = new FixedString64Bytes(row.nameKey)
            };
            foreach (FactoryBuildingPortTableRow port in rowPorts)
            {
                ports[portCursor++] = new FactoryBuildingPortBlob
                {
                    CellOffset = new int2(port.cellOffset.x, port.cellOffset.y),
                    Direction = new int2(port.direction.x, port.direction.y),
                    Type = port.type,
                    Index = port.index
                };
            }
        }

        BlobBuilderArray<FactoryBuildingLevelBlob> levels =
            builder.Allocate(ref root.BuildingLevelsById, maxLevelId + 1);
        BlobBuilderArray<BuildingLevelId> menu =
            builder.Allocate(ref root.BuildingLevelMenu, source.buildingLevels.Length);
        for (int i = 0; i < source.buildingLevels.Length; i++)
        {
            FactoryBuildingLevelTableRow row = source.buildingLevels[i];
            BuildingLevelId id = new BuildingLevelId { Value = row.id };
            levels[row.id] = new FactoryBuildingLevelBlob
            {
                Id = id,
                BuildingId = new BuildingTypeId { Value = row.buildingId },
                Level = row.level,
                MenuOrder = row.menuOrder,
                Key = new FixedString64Bytes(row.key),
                NameKey = new FixedString64Bytes(row.nameKey),
                VisualPrefabKey = new FixedString64Bytes(row.visualPrefabKey)
            };
            menu[i] = id;
        }

        BlobBuilderArray<FactoryBeltLevelBlob> beltLevels =
            builder.Allocate(ref root.BeltLevelsById, maxLevelId + 1);
        foreach (FactoryBeltLevelTableRow row in source.beltLevels)
        {
            BuildingLevelId levelId = new BuildingLevelId
            {
                Value = row.buildingLevelId
            };
            beltLevels[row.buildingLevelId] = new FactoryBeltLevelBlob
            {
                LevelId = levelId,
                CellsPerSecond = row.cellsPerSecond
            };
        }

        BlobBuilderArray<FactoryProcessorLevelBlob> processorLevels =
            builder.Allocate(ref root.ProcessorLevelsById, maxLevelId + 1);
        foreach (FactoryProcessorLevelTableRow row in source.processorLevels)
        {
            BuildingLevelId levelId = new BuildingLevelId
            {
                Value = row.buildingLevelId
            };
            processorLevels[row.buildingLevelId] = new FactoryProcessorLevelBlob
            {
                LevelId = levelId,
                WorkRatePermille = row.workRatePermille
            };
        }

        BlobBuilderArray<FactoryStorageLevelBlob> storageLevels =
            builder.Allocate(ref root.StorageLevelsById, maxLevelId + 1);
        foreach (FactoryStorageLevelTableRow row in source.storageLevels)
        {
            BuildingLevelId levelId = new BuildingLevelId
            {
                Value = row.buildingLevelId
            };
            storageLevels[row.buildingLevelId] = new FactoryStorageLevelBlob
            {
                LevelId = levelId,
                Capacity = row.capacity,
                SlotCount = row.slotCount
            };
        }

        BlobBuilderArray<FactoryRecipeBlob> recipeArray =
            builder.Allocate(ref root.Recipes, recipes.Count);
        BlobBuilderArray<FactoryRecipeIngredientBlob> inputs =
            builder.Allocate(ref root.Inputs, inputCount);
        BlobBuilderArray<FactoryRecipeIngredientBlob> outputs =
            builder.Allocate(ref root.Outputs, outputCount);
        BlobBuilderArray<FactoryRecipeRangeBlob> ranges =
            builder.Allocate(ref root.RecipeRangesByMachine, maxMachineId + 1);

        int inputCursor = 0;
        int outputCursor = 0;
        for (int i = 0; i < recipes.Count; i++)
        {
            FactoryRecipeTableRow row = recipes[i];
            FactoryRecipeIngredientTableRow[] rowInputs =
                row.inputs ?? Array.Empty<FactoryRecipeIngredientTableRow>();
            FactoryRecipeIngredientTableRow[] rowOutputs =
                row.outputs ?? Array.Empty<FactoryRecipeIngredientTableRow>();
            recipeArray[i] = new FactoryRecipeBlob
            {
                Id = new RecipeId { Value = row.id },
                Key = new FixedString64Bytes(row.key),
                MachineType = new MachineTypeId { Value = row.machineTypeId },
                DurationTicks = FactorySimulationTime.SecondsToTicks(row.durationSeconds),
                InputStart = inputCursor,
                InputCount = (ushort)rowInputs.Length,
                OutputStart = outputCursor,
                OutputCount = (ushort)rowOutputs.Length
            };
            foreach (FactoryRecipeIngredientTableRow ingredient in rowInputs)
                inputs[inputCursor++] = ConvertIngredient(ingredient);
            foreach (FactoryRecipeIngredientTableRow ingredient in rowOutputs)
                outputs[outputCursor++] = ConvertIngredient(ingredient);

            FactoryRecipeRangeBlob range = ranges[row.machineTypeId];
            if (range.Count == 0)
                range.Start = i;
            range.Count++;
            ranges[row.machineTypeId] = range;
        }

        result = builder.CreateBlobAssetReference<FactoryDatabaseBlob>(Allocator.Persistent);
        return true;
    }

    private static FactoryRecipeIngredientBlob ConvertIngredient(
        FactoryRecipeIngredientTableRow row) => new FactoryRecipeIngredientBlob
        {
            ItemId = new ItemId { Value = row.itemId },
            Count = row.count
        };

    private static int MaxId<T>(T[] rows, Func<T, ushort> selector)
    {
        int result = 0;
        foreach (T row in rows)
            result = Math.Max(result, selector(row));
        return result;
    }

    private static bool Validate(FactoryDatabaseAsset source, UnityEngine.Object context)
    {
        if (source == null || source.items == null || source.items.Length == 0 ||
            source.machineTypes == null || source.machineTypes.Length == 0 ||
            source.buildings == null || source.buildings.Length == 0 ||
            source.buildingLevels == null || source.buildingLevels.Length == 0 ||
            source.beltLevels == null ||
            source.processorLevels == null ||
            source.storageLevels == null ||
            source.recipes == null || source.recipes.Length == 0)
        {
            Debug.LogError("Factory database is missing required generated rows.", context);
            return false;
        }

        bool valid = true;
        HashSet<ushort> itemIds = new HashSet<ushort>();
        foreach (FactoryItemTableRow item in source.items)
            valid &= item.id != 0 && itemIds.Add(item.id);
        HashSet<ushort> buildingIds = new HashSet<ushort>();
        Dictionary<ushort, FactoryBuildingTableRow> buildingsById =
            new Dictionary<ushort, FactoryBuildingTableRow>();
        foreach (FactoryBuildingTableRow building in source.buildings)
        {
            bool buildingValid = building.id != 0 && buildingIds.Add(building.id) &&
                                 building.footprintWidth > 0 && building.footprintHeight > 0;
            valid &= buildingValid;
            if (buildingValid)
                buildingsById.Add(building.id, building);
        }
        HashSet<ushort> levelIds = new HashSet<ushort>();
        Dictionary<ushort, FactoryBuildingLevelTableRow> levelsById =
            new Dictionary<ushort, FactoryBuildingLevelTableRow>();
        foreach (FactoryBuildingLevelTableRow level in source.buildingLevels)
        {
            bool levelValid = level.id != 0 && levelIds.Add(level.id) &&
                              buildingsById.ContainsKey(level.buildingId) &&
                              level.visualPrefab != null;
            valid &= levelValid;
            if (levelValid)
                levelsById.Add(level.id, level);
        }
        HashSet<ushort> beltLevelIds = new HashSet<ushort>();
        foreach (FactoryBeltLevelTableRow beltLevel in source.beltLevels)
        {
            bool referenceValid = levelsById.TryGetValue(
                beltLevel.buildingLevelId,
                out FactoryBuildingLevelTableRow level);
            valid &= referenceValid && beltLevelIds.Add(beltLevel.buildingLevelId) &&
                     !float.IsNaN(beltLevel.cellsPerSecond) &&
                     !float.IsInfinity(beltLevel.cellsPerSecond) &&
                     beltLevel.cellsPerSecond > 0f &&
                     buildingsById[level.buildingId].behavior == FactoryBuildingBehavior.Belt;
        }
        HashSet<ushort> processorLevelIds = new HashSet<ushort>();
        foreach (FactoryProcessorLevelTableRow processorLevel in source.processorLevels)
        {
            bool referenceValid = levelsById.TryGetValue(
                processorLevel.buildingLevelId,
                out FactoryBuildingLevelTableRow level);
            valid &= referenceValid && processorLevelIds.Add(processorLevel.buildingLevelId) &&
                     processorLevel.workRatePermille > 0 &&
                     buildingsById[level.buildingId].behavior == FactoryBuildingBehavior.Processor;
        }
        HashSet<ushort> storageLevelIds = new HashSet<ushort>();
        foreach (FactoryStorageLevelTableRow storageLevel in source.storageLevels)
        {
            bool referenceValid = levelsById.TryGetValue(
                storageLevel.buildingLevelId,
                out FactoryBuildingLevelTableRow level);
            valid &= referenceValid && storageLevelIds.Add(storageLevel.buildingLevelId) &&
                     storageLevel.capacity > 0 &&
                     storageLevel.slotCount > 0 &&
                     buildingsById[level.buildingId].behavior == FactoryBuildingBehavior.Storage;
        }
        foreach (FactoryBuildingLevelTableRow level in source.buildingLevels)
        {
            if (!buildingsById.TryGetValue(
                    level.buildingId,
                    out FactoryBuildingTableRow building))
            {
                valid = false;
                continue;
            }
            FactoryBuildingBehavior behavior = building.behavior;
            valid &= behavior == FactoryBuildingBehavior.Belt
                ? beltLevelIds.Contains(level.id) &&
                  !processorLevelIds.Contains(level.id) &&
                  !storageLevelIds.Contains(level.id)
                : behavior == FactoryBuildingBehavior.Processor
                    ? processorLevelIds.Contains(level.id) &&
                      !beltLevelIds.Contains(level.id) &&
                      !storageLevelIds.Contains(level.id)
                    : behavior == FactoryBuildingBehavior.Storage
                        ? storageLevelIds.Contains(level.id) &&
                          !beltLevelIds.Contains(level.id) &&
                          !processorLevelIds.Contains(level.id)
                        : !beltLevelIds.Contains(level.id) &&
                          !processorLevelIds.Contains(level.id) &&
                          !storageLevelIds.Contains(level.id);
        }
        HashSet<ushort> recipeIds = new HashSet<ushort>();
        foreach (FactoryRecipeTableRow recipe in source.recipes)
        {
            valid &= recipe.id != 0 && recipeIds.Add(recipe.id) &&
                     recipe.machineTypeId != 0 && recipe.durationSeconds > 0f &&
                     (recipe.outputs?.Length ?? 0) > 0;
            foreach (FactoryRecipeIngredientTableRow ingredient in
                     recipe.inputs ?? Array.Empty<FactoryRecipeIngredientTableRow>())
                valid &= itemIds.Contains(ingredient.itemId) && ingredient.count > 0;
            foreach (FactoryRecipeIngredientTableRow ingredient in
                     recipe.outputs ?? Array.Empty<FactoryRecipeIngredientTableRow>())
                valid &= itemIds.Contains(ingredient.itemId) && ingredient.count > 0;
        }

        if (!valid)
            Debug.LogError("Factory database generated rows failed validation.", context);
        return valid;
    }
}
