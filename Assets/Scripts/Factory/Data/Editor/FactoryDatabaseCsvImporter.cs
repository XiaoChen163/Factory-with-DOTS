using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class FactoryDatabaseCsvImporter : AssetPostprocessor
{
    private const string TableDirectory = "Assets/Data/FactoryTables";
    private const string ItemPrefabDirectory = "Assets/Prefabs/Items";
    private const string ItemTablePath = TableDirectory + "/items.csv";
    private const string RecipeTablePath = TableDirectory + "/recipes.csv";
    private const string InputTablePath =
        TableDirectory + "/recipe_inputs.csv";
    private const string OutputTablePath =
        TableDirectory + "/recipe_outputs.csv";
    private const string OutputDirectory = "Assets/Data/Generated";
    public const string DatabaseAssetPath =
        OutputDirectory + "/FactoryDatabase.asset";

    private static bool rebuildQueued;

    [MenuItem("Factory/Rebuild Static Database")]
    public static void RebuildFromMenu()
    {
        Rebuild();
    }

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (!ContainsFactorySource(importedAssets) &&
            !ContainsFactorySource(deletedAssets) &&
            !ContainsFactorySource(movedAssets) &&
            !ContainsFactorySource(movedFromAssetPaths))
        {
            return;
        }

        if (rebuildQueued)
        {
            return;
        }

        rebuildQueued = true;
        EditorApplication.delayCall += () =>
        {
            rebuildQueued = false;
            Rebuild();
        };
    }

    private static bool ContainsFactorySource(IEnumerable<string> paths)
    {
        return paths.Any(path =>
            (path.StartsWith(
                 TableDirectory + "/",
                 StringComparison.Ordinal) &&
             path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) ||
            (path.StartsWith(
                 ItemPrefabDirectory + "/",
                 StringComparison.Ordinal) &&
             path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)));
    }

    private static void Rebuild()
    {
        try
        {
            FactoryItemTableRow[] items = ReadItems();
            FactoryRecipeTableRow[] recipes = ReadRecipes();

            EnsureAssetFolder(OutputDirectory);
            FactoryDatabaseAsset asset =
                AssetDatabase.LoadAssetAtPath<FactoryDatabaseAsset>(
                    DatabaseAssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<
                    FactoryDatabaseAsset>();
                AssetDatabase.CreateAsset(asset, DatabaseAssetPath);
            }

            asset.version = Math.Max(1, asset.version);
            asset.items = items;
            asset.recipes = recipes;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            Debug.Log(
                $"Factory database rebuilt: {items.Length} items, " +
                $"{recipes.Length} recipes.",
                asset);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Failed to rebuild factory database from CSV:\n" +
                exception);
        }
    }

    private static FactoryItemTableRow[] ReadItems()
    {
        CsvTable table = CsvTable.Read(ItemTablePath);
        Dictionary<string, GameObject> prefabs = BuildItemPrefabIndex();
        HashSet<string> usedPrefabKeys =
            new HashSet<string>(StringComparer.Ordinal);
        List<FactoryItemTableRow> result =
            new List<FactoryItemTableRow>(table.RowCount);
        for (int row = 0; row < table.RowCount; row++)
        {
            string prefabKey = table.Get(row, "prefab_key");
            string normalizedPrefabKey = NormalizePrefabKey(prefabKey);
            if (string.IsNullOrEmpty(normalizedPrefabKey) ||
                !usedPrefabKeys.Add(normalizedPrefabKey))
            {
                throw new InvalidDataException(
                    $"Duplicate or invalid item prefab key '{prefabKey}'.");
            }
            if (!prefabs.TryGetValue(
                    normalizedPrefabKey,
                    out GameObject prefab))
            {
                throw new InvalidDataException(
                    $"Item prefab key '{prefabKey}' was not found in " +
                    $"'{ItemPrefabDirectory}'.");
            }

            result.Add(new FactoryItemTableRow
            {
                id = ParseUShort(table.Get(row, "id"), "item id"),
                key = table.Get(row, "key"),
                nameKey = table.Get(row, "name_key"),
                maxStack = ParseUShort(
                    table.Get(row, "max_stack"),
                    "max stack"),
                category = ParseEnum<FactoryItemCategory>(
                    table.Get(row, "category"),
                    "item category"),
                prefabKey = prefabKey,
                prefab = prefab
            });
        }

        result.Sort((left, right) => left.id.CompareTo(right.id));
        return result.ToArray();
    }

    private static Dictionary<string, GameObject> BuildItemPrefabIndex()
    {
        Dictionary<string, GameObject> result =
            new Dictionary<string, GameObject>(StringComparer.Ordinal);
        string[] guids = AssetDatabase.FindAssets(
            "t:Prefab",
            new[] { ItemPrefabDirectory });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                continue;
            }

            string key = NormalizePrefabKey(
                Path.GetFileNameWithoutExtension(path));
            if (string.IsNullOrEmpty(key) || result.ContainsKey(key))
            {
                throw new InvalidDataException(
                    $"Duplicate or invalid item prefab name at '{path}'.");
            }
            if (prefab.GetComponentsInChildren<MonoBehaviour>(true).Length > 0)
            {
                throw new InvalidDataException(
                    $"Item prefab '{path}' contains MonoBehaviour logic. " +
                    "Item prefabs must be presentation-only.");
            }
            if (prefab.GetComponentInChildren<MeshRenderer>(true) == null)
            {
                throw new InvalidDataException(
                    $"Item prefab '{path}' has no MeshRenderer.");
            }

            result.Add(key, prefab);
        }

        return result;
    }

    private static string NormalizePrefabKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Concat(
                value.Where(char.IsLetterOrDigit))
            .ToLowerInvariant();
    }

    private static FactoryRecipeTableRow[] ReadRecipes()
    {
        Dictionary<ushort, List<FactoryRecipeIngredientTableRow>> inputs =
            ReadIngredients(InputTablePath);
        Dictionary<ushort, List<FactoryRecipeIngredientTableRow>> outputs =
            ReadIngredients(OutputTablePath);
        CsvTable table = CsvTable.Read(RecipeTablePath);
        List<FactoryRecipeTableRow> result =
            new List<FactoryRecipeTableRow>(table.RowCount);
        for (int row = 0; row < table.RowCount; row++)
        {
            ushort id = ParseUShort(
                table.Get(row, "id"),
                "recipe id");
            result.Add(new FactoryRecipeTableRow
            {
                id = id,
                key = table.Get(row, "key"),
                machineType = ParseEnum<BuildingKind>(
                    table.Get(row, "machine_type"),
                    "machine type"),
                durationSeconds = ParseFloat(
                    table.Get(row, "duration_seconds"),
                    "duration"),
                inputs = GetIngredients(inputs, id),
                outputs = GetIngredients(outputs, id)
            });
        }

        result.Sort((left, right) => left.id.CompareTo(right.id));
        return result.ToArray();
    }

    private static Dictionary<ushort, List<
        FactoryRecipeIngredientTableRow>> ReadIngredients(string path)
    {
        CsvTable table = CsvTable.Read(path);
        Dictionary<ushort, List<FactoryRecipeIngredientTableRow>> result =
            new Dictionary<ushort, List<FactoryRecipeIngredientTableRow>>();
        for (int row = 0; row < table.RowCount; row++)
        {
            ushort recipeId = ParseUShort(
                table.Get(row, "recipe_id"),
                "ingredient recipe id");
            FactoryRecipeIngredientTableRow ingredient =
                new FactoryRecipeIngredientTableRow
                {
                    itemId = ParseUShort(
                        table.Get(row, "item_id"),
                        "ingredient item id"),
                    count = ParseInt(
                        table.Get(row, "count"),
                        "ingredient count")
                };
            if (!result.TryGetValue(
                    recipeId,
                    out List<FactoryRecipeIngredientTableRow> list))
            {
                list = new List<FactoryRecipeIngredientTableRow>();
                result.Add(recipeId, list);
            }
            list.Add(ingredient);
        }

        return result;
    }

    private static FactoryRecipeIngredientTableRow[] GetIngredients(
        Dictionary<ushort, List<FactoryRecipeIngredientTableRow>> source,
        ushort recipeId)
    {
        return source.TryGetValue(
            recipeId,
            out List<FactoryRecipeIngredientTableRow> result)
            ? result.ToArray()
            : Array.Empty<FactoryRecipeIngredientTableRow>();
    }

    private static ushort ParseUShort(string value, string label)
    {
        if (ushort.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out ushort result))
        {
            return result;
        }
        throw new InvalidDataException($"Invalid {label}: '{value}'.");
    }

    private static int ParseInt(string value, string label)
    {
        if (int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int result))
        {
            return result;
        }
        throw new InvalidDataException($"Invalid {label}: '{value}'.");
    }

    private static float ParseFloat(string value, string label)
    {
        if (float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float result))
        {
            return result;
        }
        throw new InvalidDataException($"Invalid {label}: '{value}'.");
    }

    private static T ParseEnum<T>(string value, string label)
        where T : struct
    {
        if (Enum.TryParse(value, true, out T result))
        {
            return result;
        }
        throw new InvalidDataException($"Invalid {label}: '{value}'.");
    }

    private static void EnsureAssetFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
        {
            return;
        }

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
                {
                    throw new InvalidDataException(
                        $"Duplicate CSV column '{name}'.");
                }
            }
            this.rows = rows;
        }

        public int RowCount => rows.Count;

        public string Get(int row, string column)
        {
            if (!columns.TryGetValue(column, out int index))
            {
                throw new InvalidDataException(
                    $"Missing CSV column '{column}'.");
            }
            if (index >= rows[row].Length)
            {
                throw new InvalidDataException(
                    $"CSV row {row + 2} is missing column '{column}'.");
            }
            return rows[row][index].Trim();
        }

        public static CsvTable Read(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Factory CSV table not found.",
                    path);
            }

            string[] lines = File.ReadAllLines(path);
            List<string[]> parsed = new List<string[]>();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line) ||
                    line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }
                parsed.Add(ParseLine(line));
            }
            if (parsed.Count == 0)
            {
                throw new InvalidDataException($"CSV table '{path}' is empty.");
            }

            string[] header = parsed[0];
            parsed.RemoveAt(0);
            return new CsvTable(header, parsed);
        }

        private static string[] ParseLine(string line)
        {
            List<string> values = new List<string>();
            System.Text.StringBuilder current =
                new System.Text.StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char character = line[i];
                if (character == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = !quoted;
                    }
                }
                else if (character == ',' && !quoted)
                {
                    values.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(character);
                }
            }
            if (quoted)
            {
                throw new InvalidDataException(
                    $"Unterminated quoted CSV value: {line}");
            }
            values.Add(current.ToString());
            return values.ToArray();
        }
    }
}
