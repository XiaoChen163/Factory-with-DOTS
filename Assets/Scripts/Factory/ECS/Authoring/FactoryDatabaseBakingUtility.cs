using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public static class FactoryDatabaseBakingUtility
{
    private const int MachineRangeCount = (int)BuildingKind.Splitter + 1;

    public static bool TryBuild(
        FactoryDatabaseAsset source,
        UnityEngine.Object context,
        out BlobAssetReference<FactoryDatabaseBlob> result)
    {
        result = default;
        if (!Validate(source, context))
        {
            return false;
        }

        List<FactoryRecipeTableRow> recipes =
            new List<FactoryRecipeTableRow>(source.recipes);
        for (int i = 0; i < recipes.Count; i++)
        {
            FactoryRecipeTableRow recipe = recipes[i];
            recipe.inputs ??=
                Array.Empty<FactoryRecipeIngredientTableRow>();
            recipe.outputs ??=
                Array.Empty<FactoryRecipeIngredientTableRow>();
            recipes[i] = recipe;
        }
        recipes.Sort(CompareRecipes);

        int maxItemId = 0;
        for (int i = 0; i < source.items.Length; i++)
        {
            maxItemId = Math.Max(maxItemId, source.items[i].id);
        }

        int inputCount = 0;
        int outputCount = 0;
        for (int i = 0; i < recipes.Count; i++)
        {
            inputCount += recipes[i].inputs.Length;
            outputCount += recipes[i].outputs.Length;
        }

        using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
        ref FactoryDatabaseBlob root =
            ref builder.ConstructRoot<FactoryDatabaseBlob>();
        root.Version = source.version;

        BlobBuilderArray<FactoryItemBlob> items =
            builder.Allocate(ref root.ItemsById, maxItemId + 1);
        for (int i = 0; i < source.items.Length; i++)
        {
            FactoryItemTableRow row = source.items[i];
            items[row.id] = new FactoryItemBlob
            {
                Id = new ItemId { Value = row.id },
                MaxStack = row.maxStack,
                Category = (byte)row.category,
                Key = new FixedString64Bytes(row.key),
                NameKey = new FixedString64Bytes(row.nameKey)
            };
        }

        BlobBuilderArray<FactoryRecipeBlob> recipeArray =
            builder.Allocate(ref root.Recipes, recipes.Count);
        BlobBuilderArray<FactoryRecipeIngredientBlob> inputs =
            builder.Allocate(ref root.Inputs, inputCount);
        BlobBuilderArray<FactoryRecipeIngredientBlob> outputs =
            builder.Allocate(ref root.Outputs, outputCount);
        BlobBuilderArray<FactoryRecipeRangeBlob> ranges =
            builder.Allocate(
                ref root.RecipeRangesByMachine,
                MachineRangeCount);

        int inputCursor = 0;
        int outputCursor = 0;
        for (int i = 0; i < recipes.Count; i++)
        {
            FactoryRecipeTableRow row = recipes[i];
            recipeArray[i] = new FactoryRecipeBlob
            {
                Id = new RecipeId { Value = row.id },
                Key = new FixedString64Bytes(row.key),
                MachineType = row.machineType,
                DurationTicks = FactorySimulationTime.SecondsToTicks(
                    row.durationSeconds),
                InputStart = inputCursor,
                InputCount = (ushort)row.inputs.Length,
                OutputStart = outputCursor,
                OutputCount = (ushort)row.outputs.Length
            };

            for (int j = 0; j < row.inputs.Length; j++)
            {
                inputs[inputCursor++] = ConvertIngredient(row.inputs[j]);
            }
            for (int j = 0; j < row.outputs.Length; j++)
            {
                outputs[outputCursor++] = ConvertIngredient(row.outputs[j]);
            }

            int machineIndex = (int)row.machineType;
            FactoryRecipeRangeBlob range = ranges[machineIndex];
            if (range.Count == 0)
            {
                range.Start = i;
            }
            range.Count++;
            ranges[machineIndex] = range;
        }

        result = builder.CreateBlobAssetReference<FactoryDatabaseBlob>(
            Allocator.Persistent);
        return true;
    }

    private static FactoryRecipeIngredientBlob ConvertIngredient(
        FactoryRecipeIngredientTableRow row)
    {
        return new FactoryRecipeIngredientBlob
        {
            ItemId = new ItemId { Value = row.itemId },
            Count = row.count
        };
    }

    private static int CompareRecipes(
        FactoryRecipeTableRow left,
        FactoryRecipeTableRow right)
    {
        int machine = left.machineType.CompareTo(right.machineType);
        return machine != 0 ? machine : left.id.CompareTo(right.id);
    }

    private static bool Validate(
        FactoryDatabaseAsset source,
        UnityEngine.Object context)
    {
        if (source == null)
        {
            Debug.LogError("Factory database asset is missing.", context);
            return false;
        }

        bool valid = true;
        if (source.version <= 0)
        {
            LogError("Database version must be positive.", context);
            valid = false;
        }
        if (source.items == null || source.items.Length == 0)
        {
            LogError("At least one item is required.", context);
            valid = false;
        }
        if (source.recipes == null || source.recipes.Length == 0)
        {
            LogError("At least one recipe is required.", context);
            valid = false;
        }

        HashSet<ushort> itemIds = new HashSet<ushort>();
        HashSet<string> itemKeys = new HashSet<string>(
            StringComparer.Ordinal);
        HashSet<string> prefabKeys = new HashSet<string>(
            StringComparer.Ordinal);
        FactoryItemTableRow[] itemRows =
            source.items ?? Array.Empty<FactoryItemTableRow>();
        for (int i = 0; i < itemRows.Length; i++)
        {
            FactoryItemTableRow item = itemRows[i];
            valid &= ValidatePositiveId(
                item.id,
                "item",
                item.key,
                context);
            if (!itemIds.Add(item.id))
            {
                LogError($"Duplicate item id {item.id}.", context);
                valid = false;
            }
            if (string.IsNullOrWhiteSpace(item.key) ||
                !itemKeys.Add(item.key))
            {
                LogError($"Invalid or duplicate item key '{item.key}'.", context);
                valid = false;
            }
            if (item.maxStack == 0)
            {
                LogError($"Item '{item.key}' has zero max stack.", context);
                valid = false;
            }
            if (string.IsNullOrWhiteSpace(item.prefabKey) ||
                !prefabKeys.Add(item.prefabKey) ||
                item.prefab == null)
            {
                LogError(
                    $"Item '{item.key}' has an invalid, duplicate, or " +
                    $"unresolved prefab key '{item.prefabKey}'.",
                    context);
                valid = false;
            }
            valid &= ValidateFixedString(item.key, "item key", context);
            valid &= ValidateFixedString(item.nameKey, "item name key", context);
        }

        HashSet<ushort> recipeIds = new HashSet<ushort>();
        HashSet<string> recipeKeys = new HashSet<string>(
            StringComparer.Ordinal);
        FactoryRecipeTableRow[] recipeRows =
            source.recipes ?? Array.Empty<FactoryRecipeTableRow>();
        for (int i = 0; i < recipeRows.Length; i++)
        {
            FactoryRecipeTableRow recipe = recipeRows[i];
            valid &= ValidatePositiveId(
                recipe.id,
                "recipe",
                recipe.key,
                context);
            if (!recipeIds.Add(recipe.id))
            {
                LogError($"Duplicate recipe id {recipe.id}.", context);
                valid = false;
            }
            if (string.IsNullOrWhiteSpace(recipe.key) ||
                !recipeKeys.Add(recipe.key))
            {
                LogError(
                    $"Invalid or duplicate recipe key '{recipe.key}'.",
                    context);
                valid = false;
            }
            if (recipe.machineType != BuildingKind.Miner &&
                recipe.machineType != BuildingKind.Furnace)
            {
                LogError(
                    $"Recipe '{recipe.key}' has unsupported machine " +
                    $"type {recipe.machineType}.",
                    context);
                valid = false;
            }
            if (recipe.durationSeconds <= 0f)
            {
                LogError(
                    $"Recipe '{recipe.key}' duration must be positive.",
                    context);
                valid = false;
            }

            FactoryRecipeIngredientTableRow[] inputs =
                recipe.inputs ?? Array.Empty<FactoryRecipeIngredientTableRow>();
            FactoryRecipeIngredientTableRow[] outputs =
                recipe.outputs ?? Array.Empty<FactoryRecipeIngredientTableRow>();
            if (inputs.Length > 1 || outputs.Length != 1)
            {
                LogError(
                    $"Recipe '{recipe.key}' currently requires zero or one " +
                    "input and exactly one output.",
                    context);
                valid = false;
            }
            valid &= ValidateIngredients(
                recipe.key,
                inputs,
                itemIds,
                context);
            valid &= ValidateIngredients(
                recipe.key,
                outputs,
                itemIds,
                context);
            valid &= ValidateFixedString(recipe.key, "recipe key", context);
        }

        return valid;
    }

    private static bool ValidateIngredients(
        string recipeKey,
        FactoryRecipeIngredientTableRow[] ingredients,
        HashSet<ushort> itemIds,
        UnityEngine.Object context)
    {
        bool valid = true;
        for (int i = 0; i < ingredients.Length; i++)
        {
            FactoryRecipeIngredientTableRow ingredient = ingredients[i];
            if (!itemIds.Contains(ingredient.itemId) || ingredient.count <= 0)
            {
                LogError(
                    $"Recipe '{recipeKey}' has invalid item " +
                    $"{ingredient.itemId} or count {ingredient.count}.",
                    context);
                valid = false;
            }
        }
        return valid;
    }

    private static bool ValidatePositiveId(
        ushort id,
        string type,
        string key,
        UnityEngine.Object context)
    {
        if (id != 0)
        {
            return true;
        }

        LogError($"The {type} '{key}' uses reserved id 0.", context);
        return false;
    }

    private static bool ValidateFixedString(
        string value,
        string label,
        UnityEngine.Object context)
    {
        if (!string.IsNullOrEmpty(value) &&
            System.Text.Encoding.UTF8.GetByteCount(value) <= 61)
        {
            return true;
        }

        LogError($"The {label} '{value}' is empty or too long.", context);
        return false;
    }

    private static void LogError(
        string message,
        UnityEngine.Object context)
    {
        Debug.LogError("Factory database: " + message, context);
    }
}
