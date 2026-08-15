using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class FactoryDatabaseCsvImporter
{
    private const string TableDirectory = "Assets/Data/FactoryTables";
    private const string ItemTableDirectory = TableDirectory + "/items";
    private const string RecipeTableDirectory = ItemTableDirectory + "/recipes";
    private const string BuildingTableDirectory = TableDirectory + "/buildings";
    private const string BuildingLevelTableDirectory = BuildingTableDirectory + "/levels";
    private const string ItemPrefabDirectory = "Assets/Prefabs/Items";
    private const string BuildingPrefabDirectory = "Assets/Prefabs/Buildings";
    private const string ItemIconDirectory = "Assets/Art/Icons/Items";
    private const string BuildingIconDirectory = "Assets/Art/Icons/Buildings";
    private const string ItemTablePath = ItemTableDirectory + "/items.csv";
    private const string MachineTypeTablePath = BuildingTableDirectory + "/machine_types.csv";
    private const string BuildingTablePath = BuildingTableDirectory + "/buildings.csv";
    private const string BuildingLevelTablePath = BuildingLevelTableDirectory + "/building_levels.csv";
    private const string BeltLevelTablePath = BuildingLevelTableDirectory + "/belt_level_stats.csv";
    private const string ProcessorLevelTablePath = BuildingLevelTableDirectory + "/processor_level_stats.csv";
    private const string StorageLevelTablePath = BuildingLevelTableDirectory + "/storage_level_stats.csv";
    private const string BuildingPortTablePath = BuildingTableDirectory + "/building_ports.csv";
    private const string RecipeTablePath = RecipeTableDirectory + "/recipes.csv";
    private const string InputTablePath = RecipeTableDirectory + "/recipe_inputs.csv";
    private const string OutputTablePath = RecipeTableDirectory + "/recipe_outputs.csv";
    private const string OutputDirectory = "Assets/Data/Generated";
    public const string DatabaseAssetPath = OutputDirectory + "/FactoryDatabase.asset";

    [MenuItem("Factory/Rebuild Static Database")]
    public static void RebuildFromMenu() => Rebuild();

    private static void Rebuild()
    {
        try
        {
            FactoryItemTableRow[] items = ReadItems();
            Dictionary<string, FactoryItemTableRow> itemsByKey =
                IndexByKey(items, row => row.key, "item");
            FactoryMachineTypeTableRow[] machineTypes = ReadMachineTypes();
            Dictionary<string, FactoryMachineTypeTableRow> machinesByKey =
                IndexByKey(machineTypes, row => row.key, "machine type");
            FactoryBuildingTableRow[] buildings = ReadBuildings(machinesByKey);
            Dictionary<string, FactoryBuildingTableRow> buildingsByKey =
                IndexByKey(buildings, row => row.key, "building");
            FactoryBuildingLevelTableRow[] levels = ReadBuildingLevels(buildingsByKey);
            Dictionary<string, FactoryBuildingLevelTableRow> levelsByKey =
                IndexByKey(levels, row => row.key, "building level");
            FactoryBeltLevelTableRow[] beltLevels =
                ReadBeltLevels(levels, levelsByKey, buildingsByKey);
            FactoryProcessorLevelTableRow[] processorLevels =
                ReadProcessorLevels(levels, levelsByKey, buildingsByKey);
            FactoryStorageLevelTableRow[] storageLevels =
                ReadStorageLevels(levels, levelsByKey, buildingsByKey);
            FactoryRecipeTableRow[] recipes = ReadRecipes(machinesByKey, itemsByKey);

            EnsureAssetFolder(OutputDirectory);
            FactoryDatabaseAsset asset =
                AssetDatabase.LoadAssetAtPath<FactoryDatabaseAsset>(DatabaseAssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<FactoryDatabaseAsset>();
                AssetDatabase.CreateAsset(asset, DatabaseAssetPath);
            }

            asset.version = Math.Max(1, asset.version);
            asset.items = items;
            asset.machineTypes = machineTypes;
            asset.buildings = buildings;
            asset.buildingLevels = levels;
            asset.beltLevels = beltLevels;
            asset.processorLevels = processorLevels;
            asset.storageLevels = storageLevels;
            asset.recipes = recipes;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"Factory database rebuilt: {items.Length} items, " +
                $"{buildings.Length} buildings, {levels.Length} build options, " +
                $"{beltLevels.Length} belt levels, {processorLevels.Length} processor levels, " +
                $"{storageLevels.Length} storage levels, " +
                $"{recipes.Length} recipes.",
                asset);
        }
        catch (Exception exception)
        {
            Debug.LogError("Failed to rebuild factory database from CSV:\n" + exception);
        }
    }

    private static FactoryItemTableRow[] ReadItems()
    {
        CsvTable table = CsvTable.Read(ItemTablePath);
        Dictionary<string, GameObject> prefabs = BuildPrefabIndex(ItemPrefabDirectory);
        Dictionary<string, Sprite> icons = BuildSpriteIndex(ItemIconDirectory);
        List<FactoryItemTableRow> result = new List<FactoryItemTableRow>(table.RowCount);
        HashSet<ushort> ids = new HashSet<ushort>();
        HashSet<string> keys = NewKeySet();
        for (int row = 0; row < table.RowCount; row++)
        {
            ushort id = ParseUShort(table.Get(row, "id"), "item id");
            string key = RequiredKey(table.Get(row, "key"), "item key");
            string prefabKey = RequiredKey(table.Get(row, "prefab_key"), "item prefab key");
            bool hasConfiguredIcon = table.TryGet(row, "icon_key", out string configuredIcon);
            string iconKey = hasConfiguredIcon
                ? RequiredKey(configuredIcon, "item icon key")
                : prefabKey;
            if (id == 0 || !ids.Add(id) || !keys.Add(key))
                throw new InvalidDataException($"Duplicate or invalid item '{key}' ({id}).");
            GameObject prefab = ResolvePrefab(prefabs, prefabKey, ItemPrefabDirectory);
            if (prefab.GetComponentsInChildren<MonoBehaviour>(true).Length > 0)
                throw new InvalidDataException($"Item prefab '{prefabKey}' must be presentation-only.");
            result.Add(new FactoryItemTableRow
            {
                id = id,
                key = key,
                nameKey = table.Get(row, "name_key"),
                maxStack = ParseUShort(table.Get(row, "max_stack"), "max stack"),
                category = ParseEnum<FactoryItemCategory>(table.Get(row, "category"), "item category"),
                prefabKey = prefabKey,
                prefab = prefab,
                iconKey = iconKey,
                icon = hasConfiguredIcon
                    ? ResolveSprite(icons, iconKey, ItemIconDirectory)
                    : TryResolveSprite(icons, iconKey)
            });
        }
        result.Sort((a, b) => a.id.CompareTo(b.id));
        return result.ToArray();
    }

    private static FactoryMachineTypeTableRow[] ReadMachineTypes()
    {
        CsvTable table = CsvTable.Read(MachineTypeTablePath);
        List<string> keys = new List<string>(table.RowCount);
        HashSet<string> unique = NewKeySet();
        for (int row = 0; row < table.RowCount; row++)
        {
            string key = RequiredKey(table.Get(row, "key"), "machine type key");
            if (!unique.Add(key))
                throw new InvalidDataException($"Duplicate machine type key '{key}'.");
            keys.Add(key);
        }
        keys.Sort(StringComparer.Ordinal);
        FactoryMachineTypeTableRow[] result = new FactoryMachineTypeTableRow[keys.Count];
        for (int i = 0; i < keys.Count; i++)
            result[i] = new FactoryMachineTypeTableRow { id = CheckedId(i + 1, "machine type"), key = keys[i] };
        return result;
    }

    private static FactoryBuildingTableRow[] ReadBuildings(
        Dictionary<string, FactoryMachineTypeTableRow> machinesByKey)
    {
        Dictionary<string, List<FactoryBuildingPortTableRow>> ports = ReadBuildingPorts();
        CsvTable table = CsvTable.Read(BuildingTablePath);
        List<FactoryBuildingTableRow> rows = new List<FactoryBuildingTableRow>(table.RowCount);
        HashSet<ushort> ids = new HashSet<ushort>();
        HashSet<string> keys = NewKeySet();
        for (int row = 0; row < table.RowCount; row++)
        {
            ushort id = ParseUShort(table.Get(row, "id"), "building id");
            string key = RequiredKey(table.Get(row, "key"), "building key");
            if (id == 0 || !ids.Add(id) || !keys.Add(key))
                throw new InvalidDataException($"Duplicate or invalid building '{key}' ({id}).");
            string machineKey = table.Get(row, "machine_type_key");
            ushort machineId = 0;
            if (!string.IsNullOrWhiteSpace(machineKey))
            {
                machineKey = RequiredKey(machineKey, "machine type key");
                if (!machinesByKey.TryGetValue(machineKey, out FactoryMachineTypeTableRow machine))
                    throw new InvalidDataException($"Building '{key}' references unknown machine type '{machineKey}'.");
                machineId = machine.id;
            }
            FactoryBuildingBehavior behavior = ParseEnum<FactoryBuildingBehavior>(
                table.Get(row, "behavior"), "building behavior");
            string layoutKey = table.Get(row, "port_layout_key").Trim();
            List<FactoryBuildingPortTableRow> buildingPorts;
            if (string.IsNullOrEmpty(layoutKey) && behavior == FactoryBuildingBehavior.Foundation)
                buildingPorts = new List<FactoryBuildingPortTableRow>();
            else
            {
                layoutKey = RequiredKey(layoutKey, "port layout key");
                if (!ports.TryGetValue(layoutKey, out buildingPorts))
                    throw new InvalidDataException($"Building '{key}' references unknown port layout '{layoutKey}'.");
            }
            rows.Add(new FactoryBuildingTableRow
            {
                id = id,
                key = key,
                nameKey = table.Get(row, "name_key"),
                behavior = behavior,
                kind = ParseEnum<BuildingKind>(table.Get(row, "kind"), "building kind"),
                machineTypeKey = machineKey,
                machineTypeId = machineId,
                portLayoutKey = layoutKey,
                footprintWidth = ParseByte(table.Get(row, "footprint_width"), "footprint width"),
                footprintHeight = ParseByte(table.Get(row, "footprint_height"), "footprint height"),
                ports = buildingPorts.ToArray()
            });
        }
        rows.Sort((a, b) => a.id.CompareTo(b.id));
        for (int i = 0; i < rows.Count; i++)
        {
            FactoryBuildingTableRow value = rows[i];
            ValidateBuilding(value);
            rows[i] = value;
        }
        return rows.ToArray();
    }

    private static Dictionary<string, List<FactoryBuildingPortTableRow>> ReadBuildingPorts()
    {
        CsvTable table = CsvTable.Read(BuildingPortTablePath);
        Dictionary<string, List<FactoryBuildingPortTableRow>> result =
            new Dictionary<string, List<FactoryBuildingPortTableRow>>(StringComparer.Ordinal);
        HashSet<string> unique = NewKeySet();
        for (int row = 0; row < table.RowCount; row++)
        {
            string layoutKey = RequiredKey(table.Get(row, "layout_key"), "port layout key");
            BuildingPortType type = ParseEnum<BuildingPortType>(table.Get(row, "type"), "port type");
            byte index = ParseByte(table.Get(row, "index"), "port index");
            string compound = layoutKey + ":" + type + ":" + index;
            if (!unique.Add(compound))
                throw new InvalidDataException($"Duplicate building port '{compound}'.");
            Vector2Int direction = new Vector2Int(
                ParseInt(table.Get(row, "direction_x"), "port direction x"),
                ParseInt(table.Get(row, "direction_y"), "port direction y"));
            if (Math.Abs(direction.x) + Math.Abs(direction.y) != 1)
                throw new InvalidDataException($"Port '{compound}' direction must be cardinal.");
            if (!result.TryGetValue(layoutKey, out List<FactoryBuildingPortTableRow> list))
            {
                list = new List<FactoryBuildingPortTableRow>();
                result.Add(layoutKey, list);
            }
            list.Add(new FactoryBuildingPortTableRow
            {
                type = type,
                index = index,
                cellOffset = new Vector2Int(
                    ParseInt(table.Get(row, "cell_x"), "port cell x"),
                    ParseInt(table.Get(row, "cell_y"), "port cell y")),
                direction = direction
            });
        }
        return result;
    }

    private static FactoryBuildingLevelTableRow[] ReadBuildingLevels(
        Dictionary<string, FactoryBuildingTableRow> buildingsByKey)
    {
        CsvTable table = CsvTable.Read(BuildingLevelTablePath);
        Dictionary<string, GameObject> prefabs = BuildPrefabIndex(BuildingPrefabDirectory);
        Dictionary<string, Sprite> icons = BuildSpriteIndex(BuildingIconDirectory);
        List<FactoryBuildingLevelTableRow> rows = new List<FactoryBuildingLevelTableRow>(table.RowCount);
        HashSet<ushort> ids = new HashSet<ushort>();
        HashSet<string> keys = NewKeySet();
        HashSet<string> buildingLevels = NewKeySet();
        for (int row = 0; row < table.RowCount; row++)
        {
            ushort id = ParseUShort(table.Get(row, "id"), "building level id");
            string key = RequiredKey(table.Get(row, "key"), "building level key");
            string buildingKey = RequiredKey(table.Get(row, "building_key"), "building key");
            byte level = ParseByte(table.Get(row, "level"), "building level");
            if (id == 0 || !ids.Add(id) || !keys.Add(key) || level == 0 ||
                !buildingLevels.Add(buildingKey + ":" + level))
                throw new InvalidDataException($"Duplicate or invalid building level '{key}'.");
            if (!buildingsByKey.TryGetValue(buildingKey, out FactoryBuildingTableRow building))
                throw new InvalidDataException($"Building level '{key}' references unknown building '{buildingKey}'.");
            string prefabKey = RequiredKey(table.Get(row, "visual_prefab_key"), "building visual prefab key");
            bool hasConfiguredIcon = table.TryGet(row, "icon_key", out string configuredIcon);
            string iconKey = hasConfiguredIcon
                ? RequiredKey(configuredIcon, "building icon key")
                : prefabKey;
            GameObject prefab = ResolvePrefab(prefabs, prefabKey, BuildingPrefabDirectory);
            ValidateBuildingVisualPrefab(prefab, prefabKey);
            rows.Add(new FactoryBuildingLevelTableRow
            {
                id = id,
                key = key,
                buildingKey = buildingKey,
                buildingId = building.id,
                level = level,
                nameKey = table.Get(row, "name_key"),
                visualPrefabKey = prefabKey,
                visualPrefab = prefab,
                iconKey = iconKey,
                icon = hasConfiguredIcon
                    ? ResolveSprite(icons, iconKey, BuildingIconDirectory)
                    : TryResolveSprite(icons, iconKey),
                menuOrder = ParseInt(table.Get(row, "menu_order"), "menu order")
            });
        }
        rows.Sort((a, b) => a.menuOrder != b.menuOrder
            ? a.menuOrder.CompareTo(b.menuOrder)
            : string.CompareOrdinal(a.key, b.key));
        return rows.ToArray();
    }

    private static FactoryBeltLevelTableRow[] ReadBeltLevels(
        FactoryBuildingLevelTableRow[] levels,
        Dictionary<string, FactoryBuildingLevelTableRow> levelsByKey,
        Dictionary<string, FactoryBuildingTableRow> buildingsByKey)
    {
        CsvTable table = CsvTable.Read(BeltLevelTablePath);
        List<FactoryBeltLevelTableRow> rows =
            new List<FactoryBeltLevelTableRow>(table.RowCount);
        HashSet<string> configuredLevels = NewKeySet();
        for (int row = 0; row < table.RowCount; row++)
        {
            string levelKey = RequiredKey(
                table.Get(row, "building_level_key"),
                "belt building level key");
            if (!configuredLevels.Add(levelKey))
                throw new InvalidDataException(
                    $"Duplicate belt stats for building level '{levelKey}'.");
            FactoryBuildingLevelTableRow level = ResolveLevelForBehavior(
                levelKey,
                FactoryBuildingBehavior.Belt,
                "belt",
                levelsByKey,
                buildingsByKey);
            float cellsPerSecond = ParseFloat(
                table.Get(row, "cells_per_second"),
                "belt cells per second");
            if (float.IsNaN(cellsPerSecond) ||
                float.IsInfinity(cellsPerSecond) ||
                cellsPerSecond <= 0f)
                throw new InvalidDataException(
                    $"Belt level '{levelKey}' cells_per_second must be positive.");
            rows.Add(new FactoryBeltLevelTableRow
            {
                buildingLevelKey = levelKey,
                buildingLevelId = level.id,
                cellsPerSecond = cellsPerSecond
            });
        }

        ValidateRequiredLevelStats(
            levels,
            buildingsByKey,
            FactoryBuildingBehavior.Belt,
            configuredLevels,
            "belt");
        return rows.ToArray();
    }

    private static FactoryProcessorLevelTableRow[] ReadProcessorLevels(
        FactoryBuildingLevelTableRow[] levels,
        Dictionary<string, FactoryBuildingLevelTableRow> levelsByKey,
        Dictionary<string, FactoryBuildingTableRow> buildingsByKey)
    {
        CsvTable table = CsvTable.Read(ProcessorLevelTablePath);
        List<FactoryProcessorLevelTableRow> rows =
            new List<FactoryProcessorLevelTableRow>(table.RowCount);
        HashSet<string> configuredLevels = NewKeySet();
        for (int row = 0; row < table.RowCount; row++)
        {
            string levelKey = RequiredKey(
                table.Get(row, "building_level_key"),
                "processor building level key");
            if (!configuredLevels.Add(levelKey))
                throw new InvalidDataException(
                    $"Duplicate processor stats for building level '{levelKey}'.");
            FactoryBuildingLevelTableRow level = ResolveLevelForBehavior(
                levelKey,
                FactoryBuildingBehavior.Processor,
                "processor",
                levelsByKey,
                buildingsByKey);
            ushort workRatePermille = ParseUShort(
                table.Get(row, "work_rate_permille"),
                "processor work rate permille");
            if (workRatePermille == 0)
                throw new InvalidDataException(
                    $"Processor level '{levelKey}' work_rate_permille must be positive.");
            rows.Add(new FactoryProcessorLevelTableRow
            {
                buildingLevelKey = levelKey,
                buildingLevelId = level.id,
                workRatePermille = workRatePermille
            });
        }

        ValidateRequiredLevelStats(
            levels,
            buildingsByKey,
            FactoryBuildingBehavior.Processor,
            configuredLevels,
            "processor");
        return rows.ToArray();
    }

    private static FactoryBuildingLevelTableRow ResolveLevelForBehavior(
        string levelKey,
        FactoryBuildingBehavior expectedBehavior,
        string statsLabel,
        Dictionary<string, FactoryBuildingLevelTableRow> levelsByKey,
        Dictionary<string, FactoryBuildingTableRow> buildingsByKey)
    {
        if (!levelsByKey.TryGetValue(levelKey, out FactoryBuildingLevelTableRow level))
            throw new InvalidDataException(
                $"{statsLabel} stats reference unknown building level '{levelKey}'.");
        FactoryBuildingTableRow building = buildingsByKey[level.buildingKey];
        if (building.behavior != expectedBehavior)
            throw new InvalidDataException(
                $"{statsLabel} stats cannot target {building.behavior} building level '{levelKey}'.");
        return level;
    }

    private static FactoryStorageLevelTableRow[] ReadStorageLevels(
        FactoryBuildingLevelTableRow[] levels,
        Dictionary<string, FactoryBuildingLevelTableRow> levelsByKey,
        Dictionary<string, FactoryBuildingTableRow> buildingsByKey)
    {
        CsvTable table = CsvTable.Read(StorageLevelTablePath);
        List<FactoryStorageLevelTableRow> rows =
            new List<FactoryStorageLevelTableRow>(table.RowCount);
        HashSet<string> configuredLevels = NewKeySet();
        for (int row = 0; row < table.RowCount; row++)
        {
            string levelKey = RequiredKey(
                table.Get(row, "building_level_key"),
                "storage building level key");
            if (!configuredLevels.Add(levelKey))
                throw new InvalidDataException(
                    $"Duplicate storage stats for building level '{levelKey}'.");
            FactoryBuildingLevelTableRow level = ResolveLevelForBehavior(
                levelKey,
                FactoryBuildingBehavior.Storage,
                "storage",
                levelsByKey,
                buildingsByKey);
            int capacity = ParseInt(
                table.Get(row, "storage_capacity"),
                "storage capacity");
            if (capacity <= 0)
                throw new InvalidDataException(
                    $"Storage level '{levelKey}' storage_capacity must be positive.");
            ushort slotCount = ParseUShort(
                table.Get(row, "slot_count"),
                "storage slot count");
            if (slotCount == 0)
                throw new InvalidDataException(
                    $"Storage level '{levelKey}' slot_count must be positive.");
            rows.Add(new FactoryStorageLevelTableRow
            {
                buildingLevelKey = levelKey,
                buildingLevelId = level.id,
                capacity = capacity,
                slotCount = slotCount
            });
        }

        ValidateRequiredLevelStats(
            levels,
            buildingsByKey,
            FactoryBuildingBehavior.Storage,
            configuredLevels,
            "storage");
        return rows.ToArray();
    }

    private static void ValidateRequiredLevelStats(
        FactoryBuildingLevelTableRow[] levels,
        Dictionary<string, FactoryBuildingTableRow> buildingsByKey,
        FactoryBuildingBehavior behavior,
        HashSet<string> configuredLevels,
        string statsLabel)
    {
        foreach (FactoryBuildingLevelTableRow level in levels)
        {
            if (buildingsByKey[level.buildingKey].behavior == behavior &&
                !configuredLevels.Contains(level.key))
            {
                throw new InvalidDataException(
                    $"Building level '{level.key}' requires exactly one {statsLabel} stats row.");
            }
        }
    }

    private static FactoryRecipeTableRow[] ReadRecipes(
        Dictionary<string, FactoryMachineTypeTableRow> machinesByKey,
        Dictionary<string, FactoryItemTableRow> itemsByKey)
    {
        Dictionary<string, List<FactoryRecipeIngredientTableRow>> inputs =
            ReadIngredients(InputTablePath, itemsByKey);
        Dictionary<string, List<FactoryRecipeIngredientTableRow>> outputs =
            ReadIngredients(OutputTablePath, itemsByKey);
        CsvTable table = CsvTable.Read(RecipeTablePath);
        List<FactoryRecipeTableRow> rows = new List<FactoryRecipeTableRow>(table.RowCount);
        HashSet<string> keys = NewKeySet();
        for (int row = 0; row < table.RowCount; row++)
        {
            string key = RequiredKey(table.Get(row, "key"), "recipe key");
            string machineKey = RequiredKey(table.Get(row, "machine_type_key"), "machine type key");
            if (!keys.Add(key))
                throw new InvalidDataException($"Duplicate recipe key '{key}'.");
            if (!machinesByKey.TryGetValue(machineKey, out FactoryMachineTypeTableRow machine))
                throw new InvalidDataException($"Recipe '{key}' references unknown machine type '{machineKey}'.");
            rows.Add(new FactoryRecipeTableRow
            {
                key = key,
                machineTypeKey = machineKey,
                machineTypeId = machine.id,
                durationSeconds = ParseFloat(table.Get(row, "duration_seconds"), "duration"),
                inputs = GetIngredients(inputs, key),
                outputs = GetIngredients(outputs, key)
            });
        }
        rows.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
        for (int i = 0; i < rows.Count; i++)
        {
            FactoryRecipeTableRow value = rows[i];
            value.id = CheckedId(i + 1, "recipe");
            rows[i] = value;
        }
        foreach (string recipeKey in inputs.Keys.Concat(outputs.Keys))
            if (!keys.Contains(recipeKey))
                throw new InvalidDataException($"Ingredient references unknown recipe '{recipeKey}'.");
        return rows.ToArray();
    }

    private static Dictionary<string, List<FactoryRecipeIngredientTableRow>> ReadIngredients(
        string path,
        Dictionary<string, FactoryItemTableRow> itemsByKey)
    {
        CsvTable table = CsvTable.Read(path);
        Dictionary<string, List<FactoryRecipeIngredientTableRow>> result =
            new Dictionary<string, List<FactoryRecipeIngredientTableRow>>(StringComparer.Ordinal);
        HashSet<string> uniqueIngredients = NewKeySet();
        for (int row = 0; row < table.RowCount; row++)
        {
            string recipeKey = RequiredKey(table.Get(row, "recipe_key"), "ingredient recipe key");
            string itemKey = RequiredKey(table.Get(row, "item_key"), "ingredient item key");
            if (!uniqueIngredients.Add(recipeKey + ":" + itemKey))
                throw new InvalidDataException(
                    $"Recipe '{recipeKey}' declares item '{itemKey}' more than once in '{path}'.");
            if (!itemsByKey.TryGetValue(itemKey, out FactoryItemTableRow item))
                throw new InvalidDataException($"Recipe '{recipeKey}' references unknown item '{itemKey}'.");
            int count = ParseInt(table.Get(row, "count"), "ingredient count");
            if (count <= 0 || count > item.maxStack)
                throw new InvalidDataException(
                    $"Recipe '{recipeKey}' item '{itemKey}' count must be between 1 and its max stack ({item.maxStack}).");
            if (!result.TryGetValue(recipeKey, out List<FactoryRecipeIngredientTableRow> list))
            {
                list = new List<FactoryRecipeIngredientTableRow>();
                result.Add(recipeKey, list);
            }
            list.Add(new FactoryRecipeIngredientTableRow
            {
                itemKey = itemKey,
                itemId = item.id,
                count = count
            });
        }
        return result;
    }

    private static FactoryRecipeIngredientTableRow[] GetIngredients(
        Dictionary<string, List<FactoryRecipeIngredientTableRow>> source,
        string recipeKey) => source.TryGetValue(recipeKey, out List<FactoryRecipeIngredientTableRow> rows)
            ? rows.ToArray()
            : Array.Empty<FactoryRecipeIngredientTableRow>();

    private static void ValidateBuilding(FactoryBuildingTableRow row)
    {
        if (row.footprintWidth == 0 || row.footprintHeight == 0)
            throw new InvalidDataException($"Building '{row.key}' footprint must be positive.");
        bool behaviorMatchesKind =
            row.behavior == FactoryBuildingBehavior.Belt && row.kind == BuildingKind.Belt ||
            row.behavior == FactoryBuildingBehavior.Processor &&
                (row.kind == BuildingKind.Miner || row.kind == BuildingKind.Furnace) ||
            row.behavior == FactoryBuildingBehavior.Storage && row.kind == BuildingKind.Storage ||
            row.behavior == FactoryBuildingBehavior.Merger && row.kind == BuildingKind.Merger ||
            row.behavior == FactoryBuildingBehavior.Splitter && row.kind == BuildingKind.Splitter;
        behaviorMatchesKind |= row.behavior == FactoryBuildingBehavior.Foundation &&
                               row.kind == BuildingKind.Foundation;
        if (!behaviorMatchesKind)
            throw new InvalidDataException(
                $"Building '{row.key}' behavior '{row.behavior}' does not match kind '{row.kind}'.");
        if (row.behavior == FactoryBuildingBehavior.Processor && row.machineTypeId == 0)
            throw new InvalidDataException($"Processor building '{row.key}' requires a machine type.");
        if (row.behavior != FactoryBuildingBehavior.Processor && row.machineTypeId != 0)
            throw new InvalidDataException($"Non-processor building '{row.key}' cannot declare a machine type.");
        if (row.behavior == FactoryBuildingBehavior.Processor && row.ports.All(port => port.type != BuildingPortType.Output))
            throw new InvalidDataException($"Processor building '{row.key}' requires an output port.");
        if (row.behavior == FactoryBuildingBehavior.Foundation && row.ports.Length != 0)
            throw new InvalidDataException($"Foundation building '{row.key}' cannot declare ports.");
    }

    private static void ValidateBuildingVisualPrefab(GameObject prefab, string prefabKey)
    {
        if (prefab.GetComponentInChildren<MeshRenderer>(true) == null)
            throw new InvalidDataException($"Building visual prefab '{prefabKey}' has no MeshRenderer.");
        foreach (Component component in prefab.GetComponentsInChildren<Component>(true))
        {
            if (component is Transform || component is MeshFilter || component is MeshRenderer)
                continue;
            throw new InvalidDataException(
                $"Building visual prefab '{prefabKey}' contains non-rendering component " +
                $"'{component.GetType().Name}'. Only Transform, MeshFilter and MeshRenderer are allowed.");
        }
    }

    private static Dictionary<string, GameObject> BuildPrefabIndex(string directory)
    {
        Dictionary<string, GameObject> result = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { directory }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;
            string key = NormalizeKey(Path.GetFileNameWithoutExtension(path));
            if (string.IsNullOrEmpty(key) || result.ContainsKey(key))
                throw new InvalidDataException($"Duplicate or invalid prefab name at '{path}'.");
            result.Add(key, prefab);
        }
        return result;
    }

    private static Dictionary<string, Sprite> BuildSpriteIndex(string directory)
    {
        Dictionary<string, Sprite> result =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);
        if (!AssetDatabase.IsValidFolder(directory))
            return result;
        foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { directory }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                continue;
            string key = NormalizeKey(Path.GetFileNameWithoutExtension(path));
            if (string.IsNullOrEmpty(key) || result.ContainsKey(key))
                throw new InvalidDataException($"Duplicate or invalid icon name at '{path}'.");
            result.Add(key, sprite);
        }
        return result;
    }

    private static Sprite ResolveSprite(
        Dictionary<string, Sprite> sprites,
        string key,
        string directory)
    {
        if (sprites.TryGetValue(NormalizeKey(key), out Sprite sprite))
            return sprite;
        throw new InvalidDataException(
            $"Icon key '{key}' was not found in '{directory}'.");
    }

    private static Sprite TryResolveSprite(
        Dictionary<string, Sprite> sprites,
        string key)
    {
        sprites.TryGetValue(NormalizeKey(key), out Sprite sprite);
        return sprite;
    }

    private static GameObject ResolvePrefab(
        Dictionary<string, GameObject> prefabs,
        string key,
        string directory)
    {
        if (prefabs.TryGetValue(NormalizeKey(key), out GameObject prefab))
            return prefab;
        throw new InvalidDataException($"Prefab key '{key}' was not found in '{directory}'.");
    }

    private static Dictionary<string, T> IndexByKey<T>(T[] rows, Func<T, string> selector, string label)
    {
        Dictionary<string, T> result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (T row in rows)
        {
            string key = selector(row);
            if (!result.TryAdd(key, row))
                throw new InvalidDataException($"Duplicate {label} key '{key}'.");
        }
        return result;
    }

    private static HashSet<string> NewKeySet() => new HashSet<string>(StringComparer.Ordinal);

    private static string RequiredKey(string value, string label)
    {
        string result = value.Trim();
        if (string.IsNullOrEmpty(result) || result.Any(char.IsWhiteSpace))
            throw new InvalidDataException($"Invalid {label}: '{value}'.");
        return result;
    }

    private static string NormalizeKey(string value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static ushort CheckedId(int value, string label)
    {
        if (value <= 0 || value > ushort.MaxValue)
            throw new InvalidDataException($"Too many {label} rows.");
        return (ushort)value;
    }

    private static byte ParseByte(string value, string label)
    {
        if (byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte result))
            return result;
        throw new InvalidDataException($"Invalid {label}: '{value}'.");
    }

    private static ushort ParseUShort(string value, string label)
    {
        if (ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ushort result))
            return result;
        throw new InvalidDataException($"Invalid {label}: '{value}'.");
    }

    private static int ParseInt(string value, string label)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            return result;
        throw new InvalidDataException($"Invalid {label}: '{value}'.");
    }

    private static float ParseFloat(string value, string label)
    {
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
            return result;
        throw new InvalidDataException($"Invalid {label}: '{value}'.");
    }

    private static T ParseEnum<T>(string value, string label) where T : struct
    {
        if (Enum.TryParse(value, true, out T result) && Enum.IsDefined(typeof(T), result))
            return result;
        throw new InvalidDataException($"Invalid {label}: '{value}'.");
    }

    private static void EnsureAssetFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;
        string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
        string name = Path.GetFileName(folder);
        if (!string.IsNullOrEmpty(parent))
        {
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }

    private sealed class CsvTable
    {
        private readonly Dictionary<string, int> columns;
        private readonly List<string[]> rows;

        private CsvTable(string[] header, List<string[]> rows)
        {
            columns = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < header.Length; i++)
            {
                string name = header[i].Trim();
                if (!columns.TryAdd(name, i))
                    throw new InvalidDataException($"Duplicate CSV column '{name}'.");
            }
            this.rows = rows;
        }

        public int RowCount => rows.Count;

        public string Get(int row, string column)
        {
            if (!columns.TryGetValue(column, out int index))
                throw new InvalidDataException($"Missing CSV column '{column}'.");
            if (index >= rows[row].Length)
                throw new InvalidDataException($"CSV row {row + 2} is missing column '{column}'.");
            return rows[row][index].Trim();
        }

        public bool TryGet(int row, string column, out string value)
        {
            if (!columns.TryGetValue(column, out int index) ||
                index >= rows[row].Length)
            {
                value = string.Empty;
                return false;
            }
            value = rows[row][index].Trim();
            return true;
        }

        public static CsvTable Read(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Factory CSV table not found.", path);
            List<string[]> parsed = new List<string[]>();
            foreach (string raw in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    continue;
                parsed.Add(ParseLine(raw));
            }
            if (parsed.Count == 0)
                throw new InvalidDataException($"CSV table '{path}' is empty.");
            string[] header = parsed[0];
            parsed.RemoveAt(0);
            return new CsvTable(header, parsed);
        }

        private static string[] ParseLine(string line)
        {
            List<string> fields = new List<string>();
            System.Text.StringBuilder field = new System.Text.StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char current = line[i];
                if (current == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (current == ',' && !quoted)
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else
                {
                    field.Append(current);
                }
            }
            if (quoted)
                throw new InvalidDataException($"Unclosed CSV quote in '{line}'.");
            fields.Add(field.ToString());
            return fields.ToArray();
        }
    }
}
