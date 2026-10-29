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
        int inputCount = 0;
        int outputCount = 0;
        int portCount = 0;
        int occupiedCellCount = 0;
        foreach (FactoryRecipeTableRow recipe in recipes)
        {
            inputCount += recipe.inputs?.Length ?? 0;
            outputCount += recipe.outputs?.Length ?? 0;
        }
        foreach (FactoryBuildingDefinitionRow building in source.buildings)
        {
            portCount += building.ports?.Length ?? 0;
            occupiedCellCount += FactoryBuildingFootprintUtility.GetOccupiedOffsets(building).Length;
        }

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
        BlobBuilderArray<FactoryBuildingDefinitionBlob> buildings =
            builder.Allocate(ref root.BuildingDefinitionsById, maxBuildingId + 1);
        int portCursor = 0;
        BlobBuilderArray<int2> occupiedCells =
            builder.Allocate(ref root.BuildingOccupiedCells, occupiedCellCount);
        int occupiedCursor = 0;
        foreach (FactoryBuildingDefinitionRow row in source.buildings)
        {
            int2[] offsets = FactoryBuildingFootprintUtility.GetOccupiedOffsets(row);
            FactoryBuildingPortTableRow[] rowPorts =
                row.ports ?? Array.Empty<FactoryBuildingPortTableRow>();
            buildings[row.id] = new FactoryBuildingDefinitionBlob
            {
                Id = new BuildingDefinitionId { Value = row.id },
                Kind = row.kind,
                MachineType = new MachineTypeId { Value = row.machineTypeId },
                PortStart = portCursor,
                PortCount = (ushort)rowPorts.Length,
                FootprintWidth = row.footprintWidth,
                FootprintHeight = row.footprintHeight,
                OccupiedCellStart = occupiedCursor,
                OccupiedCellCount = offsets.Length,
                Key = new FixedString64Bytes(row.key),
                MenuOrder = row.menuOrder,
                BeltTier = row.beltTier,
                CellsPerSecond = row.cellsPerSecond,
                Capacity = row.storageCapacity,
                SlotCount = row.storageSlotCount,
                RampRiseHeightUnits = row.rampRiseHeightUnits
            };
            foreach (int2 offset in offsets)
                occupiedCells[occupiedCursor++] = offset;
            foreach (FactoryBuildingPortTableRow port in rowPorts)
            {
                ports[portCursor++] = new FactoryBuildingPortBlob
                {
                    CellOffset = new int2(port.cellOffset.x, port.cellOffset.y),
                    Direction = new int2(port.direction.x, port.direction.y),
                    Type = port.type,
                    Index = port.index,
                    Number = port.number,
                    OutputSlot = port.outputSlot
                };
            }
        }

        BlobBuilderArray<BuildingDefinitionId> menu =
            builder.Allocate(ref root.BuildingDefinitionMenu, source.buildings.Length);
        List<FactoryBuildingDefinitionRow> ordered = new List<FactoryBuildingDefinitionRow>(source.buildings);
        ordered.Sort((a, b) => a.menuOrder != b.menuOrder ? a.menuOrder.CompareTo(b.menuOrder) : a.id.CompareTo(b.id));
        for (int i = 0; i < ordered.Count; i++)
            menu[i] = new BuildingDefinitionId { Value = ordered[i].id };

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
        try
        {
            FactoryDatabaseValidation.Validate(source);
            return true;
        }
        catch (System.IO.InvalidDataException exception)
        {
            Debug.LogError(exception.Message, context);
            return false;
        }
    }
}
