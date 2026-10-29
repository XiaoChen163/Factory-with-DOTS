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
    private const string ItemPrefabDirectory = "Assets/Prefabs/Items";
    private const string ItemIconDirectory = "Assets/Art/Icons/Items";
    private const string ItemTablePath = ItemTableDirectory + "/items.csv";
    private const string MachineTypeTablePath = BuildingTableDirectory + "/machine_types.csv";
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
            FactoryBuildingDefinitionRow[] buildings = ReadBuildings(machinesByKey);
            FactoryRecipeTableRow[] recipes = ReadRecipes(machinesByKey, itemsByKey);

            // Validate the entire candidate before touching the last valid generated asset.
            FactoryDatabaseAsset candidate = ScriptableObject.CreateInstance<FactoryDatabaseAsset>();
            try
            {
                candidate.items = items;
                candidate.machineTypes = machineTypes;
                candidate.buildings = buildings;
                candidate.recipes = recipes;
                FactoryDatabaseValidation.Validate(candidate);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(candidate);
            }

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
            asset.recipes = recipes;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"Factory database rebuilt: {items.Length} items, " +
                $"{buildings.Length} building definitions, {recipes.Length} recipes.",
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
            bool hasConfiguredIcon = table.TryGet(row, "icon_key", out string configuredIcon) &&
                !string.IsNullOrWhiteSpace(configuredIcon);
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

    public static FactoryBuildingDefinitionRow[] ReadBuildingDefinitions(
        string directory, FactoryMachineTypeTableRow[] machineTypes)
    {
        return ReadBuildings(IndexByKey(machineTypes, row => row.key, "machine type"), directory);
    }

    private static FactoryBuildingDefinitionRow[] ReadBuildings(
        Dictionary<string, FactoryMachineTypeTableRow> machinesByKey,
        string directory = BuildingTableDirectory)
    {
        CsvTable table = CsvTable.Read(directory + "/buildings.csv");
        Dictionary<string, FactoryBuildingDefinitionRow> buildings = new Dictionary<string, FactoryBuildingDefinitionRow>(StringComparer.Ordinal);
        List<string> order = new List<string>();
        for (int row = 0; row < table.RowCount; row++)
        {
            string key = RequiredKey(table.Get(row, "key"), "building key");
            string path = table.Get(row, "prefab_path");
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Building '{key}' prefab_path must be an Assets-relative prefab path.");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidDataException($"Building '{key}' prefab not found: '{path}'.");
            BuildingKind kind = ParseEnum<BuildingKind>(table.Get(row, "behavior"), "building behavior");
            ValidateBuildingVisualPrefab(prefab, path, kind);
            var building = new FactoryBuildingDefinitionRow
            {
                id = CheckedId(row + 1, "building"), key = key, kind = kind,
                footprintWidth = ParseByte(table.Get(row, "width"), "width"),
                footprintHeight = ParseByte(table.Get(row, "length"), "length"),
                isRectangle = bool.Parse(table.Get(row, "is_rectangle")),
                footprintBitmap = table.Get(row, "bitmap"),
                visualPrefabPath = path, visualPrefab = prefab,
                ports = Array.Empty<FactoryBuildingPortTableRow>()
            };
            if (!buildings.TryAdd(key, building)) throw new InvalidDataException($"Duplicate building '{key}'.");
            order.Add(key);
        }
        ApplyExtension(directory, "processor_stats", buildings, b => b.kind == BuildingKind.Processor, (b, t, r) =>
        {
            string machine = RequiredKey(t.Get(r, "machine_type_key"), "machine type key");
            if (!machinesByKey.TryGetValue(machine, out var type))
                throw new InvalidDataException($"Building '{b.key}' references unknown machine type '{machine}'.");
            b.machineTypeId = type.id;
            return b;
        });
        ApplyExtension(directory, "belt_stats", buildings, b => b.kind == BuildingKind.Belt || VerticalBeltUtility.IsVertical(b.kind), (b, t, r) =>
        {
            b.beltTier = ParseByte(t.Get(r, "tier"), "belt tier");
            b.cellsPerSecond = ParseFloat(t.Get(r, "cells_per_second"), "belt speed");
            return b;
        });
        ApplyExtension(directory, "storage_stats", buildings, b => b.kind == BuildingKind.Storage, (b, t, r) =>
        {
            b.storageCapacity = ParseInt(t.Get(r, "capacity"), "storage capacity");
            b.storageSlotCount = ParseUShort(t.Get(r, "slot_count"), "storage slots");
            return b;
        });
        ApplyExtension(directory, "ramp_stats", buildings, b => b.kind == BuildingKind.RampFoundation, (b, t, r) =>
        {
            b.rampRiseHeightUnits = checked((sbyte)ParseInt(t.Get(r, "rise_height_units"), "ramp rise"));
            return b;
        });
        ApplyExtension(directory, "building_presentation", buildings, _ => true, (b, t, r) =>
        {
            string path = t.Get(r, "icon_path");
            b.icon = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (!string.IsNullOrEmpty(path) && b.icon == null)
                throw new InvalidDataException($"Building '{b.key}' icon not found: '{path}'.");
            b.menuOrder = ParseInt(t.Get(r, "menu_order"), "menu order");
            return b;
        });
        CsvTable names = CsvTable.Read(directory + "/building_names.csv");
        HashSet<string> nameRows = NewKeySet();
        Dictionary<string, string> fallbackNames = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int row = 0; row < names.RowCount; row++)
        {
            string key = RequiredKey(names.Get(row, "key"), "building key");
            string locale = RequiredKey(names.Get(row, "locale"), "locale");
            if (!buildings.TryGetValue(key, out var building) || !nameRows.Add(key + ":" + locale))
                throw new InvalidDataException($"Unknown or duplicate building name '{key}:{locale}'.");
            string name = names.Get(row, "name");
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException($"Building '{key}' has an empty name.");
            fallbackNames.TryAdd(key, name);
            if (locale == "zh-CN") { building.displayName = name; buildings[key] = building; }
        }
        CsvTable ports = CsvTable.Read(directory + "/building_ports.csv");
        Dictionary<string, List<FactoryBuildingPortTableRow>> portRows = new Dictionary<string, List<FactoryBuildingPortTableRow>>(StringComparer.Ordinal);
        for (int row = 0; row < ports.RowCount; row++)
        {
            string key = RequiredKey(ports.Get(row, "key"), "building key");
            if (!buildings.ContainsKey(key)) throw new InvalidDataException($"Port references unknown building '{key}'.");
            if (!portRows.TryGetValue(key, out var list)) portRows.Add(key, list = new List<FactoryBuildingPortTableRow>());
            list.Add(new FactoryBuildingPortTableRow
            {
                type = ParseEnum<BuildingPortType>(ports.Get(row, "type"), "port type"),
                index = ParseByte(ports.Get(row, "index"), "port index"),
                number = ports.TryGet(row, "number", out string number) && !string.IsNullOrWhiteSpace(number)
                    ? ParseUShort(number, "port number") : (ushort)0,
                outputSlot = ports.TryGet(row, "output_slot", out string outputSlot) && !string.IsNullOrWhiteSpace(outputSlot)
                    ? ParseUShort(outputSlot, "output slot") : (ushort)0,
                cellOffset = new Vector2Int(ParseInt(ports.Get(row, "cell_x"), "port x"), ParseInt(ports.Get(row, "cell_z"), "port z")),
                direction = new Vector2Int(ParseInt(ports.Get(row, "direction_x"), "port direction x"), ParseInt(ports.Get(row, "direction_z"), "port direction z"))
            });
        }
        List<FactoryBuildingDefinitionRow> result = new List<FactoryBuildingDefinitionRow>();
        foreach (string key in order)
        {
            var building = buildings[key];
            if (!fallbackNames.TryGetValue(key, out string fallback))
                throw new InvalidDataException($"Building '{key}' requires a name.");
            if (string.IsNullOrEmpty(building.displayName)) building.displayName = fallback;
            if (portRows.TryGetValue(key, out var list)) building.ports = list.ToArray();
            FactoryBuildingFootprintUtility.ValidatePorts(building, FactoryBuildingFootprintUtility.GetOccupiedOffsets(building));
            if (building.kind == BuildingKind.Processor && !building.ports.Any(p => p.type == BuildingPortType.Output))
                throw new InvalidDataException($"Processor '{key}' requires an output port.");
            if ((building.kind == BuildingKind.Foundation || building.kind == BuildingKind.RampFoundation) && building.ports.Length != 0)
                throw new InvalidDataException($"Foundation '{key}' cannot declare ports.");
            result.Add(building);
        }
        return result.ToArray();
    }

    private static void ApplyExtension(string directory, string name, Dictionary<string, FactoryBuildingDefinitionRow> buildings,
        Func<FactoryBuildingDefinitionRow, bool> eligible,
        Func<FactoryBuildingDefinitionRow, CsvTable, int, FactoryBuildingDefinitionRow> apply)
    {
        CsvTable table = CsvTable.Read(directory + "/" + name + ".csv");
        HashSet<string> configured = NewKeySet();
        for (int row = 0; row < table.RowCount; row++)
        {
            string key = RequiredKey(table.Get(row, "key"), "building key");
            if (!buildings.TryGetValue(key, out var building) || !eligible(building) || !configured.Add(key))
                throw new InvalidDataException($"Table '{name}' has unknown, incompatible or duplicate building '{key}'.");
            buildings[key] = apply(building, table, row);
        }
        foreach (var building in buildings.Values)
            if (eligible(building) && !configured.Contains(building.key))
                throw new InvalidDataException($"Building '{building.key}' requires exactly one '{name}' row.");
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

    private static void ValidateBuildingVisualPrefab(
        GameObject prefab,
        string prefabKey,
        BuildingKind kind)
    {
        if (prefab.GetComponentInChildren<MeshRenderer>(true) == null)
            throw new InvalidDataException($"Building visual prefab '{prefabKey}' has no MeshRenderer.");
        foreach (Component component in prefab.GetComponentsInChildren<Component>(true))
        {
            if (component is Transform || component is MeshFilter || component is MeshRenderer ||
                kind == BuildingKind.RampFoundation && component is MeshCollider)
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
