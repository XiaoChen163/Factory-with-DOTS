using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Views of CSV draft rows, not a second database. Row ordinals refer to source order.
public sealed class FactoryContentItem
{
    public int row, id, maxStack;
    public string key, name, prefabKey, iconKey;
    public FactoryItemCategory category;
}

public sealed class FactoryContentRecipe
{
    public int row;
    public string key, machine;
    public float duration;
}

public sealed class FactoryContentIngredient
{
    public int row, count;
    public string itemKey;
}

public sealed partial class FactoryContentDraft
{
    public const string ItemsPath = "items/items.csv";
    public const string RecipesPath = "items/recipes/recipes.csv";
    public const string InputsPath = "items/recipes/recipe_inputs.csv";
    public const string OutputsPath = "items/recipes/recipe_outputs.csv";
    public const string MachinesPath = "buildings/machine_types.csv";

    public FactoryCsvTable ReadTable(string relativePath) => FactoryCsvTable.Parse(Source(relativePath).text);
    private Table Source(string relativePath) => tables.Single(t => t.path == TableDirectory + "/" + relativePath);
    private static IEnumerable<int> Rows(FactoryCsvTable table) => Enumerable.Range(0, table.RowCount);
    private static int Int(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : 0;

    public List<FactoryContentItem> GetItems(string search = "")
    {
        var table = ReadTable(ItemsPath);
        return Rows(table).Select(row => new FactoryContentItem
        {
            row = row, id = Int(table.Get(row, "id")), key = table.Get(row, "key"), name = table.Get(row, "name_key"),
            maxStack = Int(table.Get(row, "max_stack")), prefabKey = table.Get(row, "prefab_key"),
            iconKey = table.TryGet(row, "icon_key", out string icon) ? icon : "",
            category = Enum.TryParse(table.Get(row, "category"), true, out FactoryItemCategory category) ? category : FactoryItemCategory.None
        }).Where(i => Matches(i.key, search) || Matches(i.name, search)).ToList();
    }

    public static bool Matches(string value, string query) => (value ?? "").IndexOf(query ?? "", StringComparison.OrdinalIgnoreCase) >= 0;

    public List<FactoryContentRecipe> GetRecipes(string search = "")
    {
        var table = ReadTable(RecipesPath);
        return Rows(table).Select(row => new FactoryContentRecipe
        {
            row = row, key = table.Get(row, "key"), machine = table.Get(row, "machine_type_key"),
            duration = float.TryParse(table.Get(row, "duration_seconds"), NumberStyles.Float, CultureInfo.InvariantCulture, out float time) ? time : 0
        }).Where(r => Matches(r.key, search) || Matches(r.machine, search)).ToList();
    }

    public List<string> GetMachineTypes()
    {
        var table = ReadTable(MachinesPath);
        return Rows(table).Select(r => table.Get(r, "key")).ToList();
    }

    public List<FactoryContentIngredient> GetIngredients(string recipe, bool output)
    {
        var table = ReadTable(output ? OutputsPath : InputsPath);
        return Rows(table).Where(r => table.Get(r, "recipe_key") == recipe).Select(r => new FactoryContentIngredient
        { row = r, itemKey = table.Get(r, "item_key"), count = Int(table.Get(r, "count")) }).ToList();
    }

    // An edit is committed to the ScriptableObject only after the whole operation
    // succeeds, so reference renames/merges are one undo step and cannot half-apply.
    private void Edit(string label, Action<Dictionary<string, FactoryCsvTable>> edit, params string[] paths)
    {
        FactoryDatabaseCsvImporter.EnsureCanWrite();
        var documents = paths.Distinct().ToDictionary(p => p, ReadTable, StringComparer.Ordinal);
        edit(documents);
        if (documents.All(d => d.Value.Text == Source(d.Key).text)) return;
        Undo.IncrementCurrentGroup();
        Undo.RegisterCompleteObjectUndo(this, label);
        foreach (var document in documents) Source(document.Key).text = document.Value.Text;
    }

    private static void RequireKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsWhiteSpace))
            throw new InvalidDataException("Key 不能为空或包含空白字符。");
    }

    private static string FreeKey(IEnumerable<string> keys, string prefix)
    {
        var used = new HashSet<string>(keys, StringComparer.Ordinal);
        string key = prefix;
        for (int i = 2; used.Contains(key); i++) key = prefix + "_" + i;
        return key;
    }

    public int AddItem()
    {
        var items = GetItems();
        var ids = new HashSet<int>(items.Select(i => i.id));
        int id = Enumerable.Range(1, ushort.MaxValue).FirstOrDefault(i => !ids.Contains(i));
        if (id == 0) throw new InvalidDataException("没有可用的物品 ID。");
        string key = FreeKey(items.Select(i => i.key), "new_item");
        int row = -1;
        Edit("新增物品", docs => row = docs[ItemsPath].Append(new Dictionary<string, string>
        {
            ["id"] = id.ToString(CultureInfo.InvariantCulture), ["key"] = key, ["name_key"] = key,
            ["max_stack"] = "64", ["category"] = "Component", ["prefab_key"] = ""
        }), ItemsPath);
        return row;
    }

    public void SetItemValue(int row, string column, string value)
    {
        var items = GetItems();
        var item = items.Single(i => i.row == row);
        if (column == "key")
        {
            RequireKey(value);
            if (items.Any(i => i.row != row && i.key == value)) throw new InvalidDataException($"物品 Key 已存在：{value}");
        }
        if (column == "id" && (!ushort.TryParse(value, out ushort id) || id == 0 || items.Any(i => i.row != row && i.id == id)))
            throw new InvalidDataException("物品 ID 必须为 1–65535，且不能重复。");
        if (column == "max_stack" && (!ushort.TryParse(value, out ushort stack) || stack == 0))
            throw new InvalidDataException("最大堆叠必须为 1–65535。");
        Edit("编辑物品", docs =>
        {
            docs[ItemsPath].EnsureColumn(column);
            docs[ItemsPath].Set(row, column, value);
            if (column == "key")
                foreach (string path in new[] { InputsPath, OutputsPath })
                    foreach (int r in Rows(docs[path]).Where(r => docs[path].Get(r, "item_key") == item.key))
                        docs[path].Set(r, "item_key", value);
        }, column == "key" ? new[] { ItemsPath, InputsPath, OutputsPath } : new[] { ItemsPath });
    }

    public List<string> GetItemReferences(string key, int? overStack = null)
    {
        var result = new List<string>();
        foreach (bool output in new[] { false, true })
        {
            var table = ReadTable(output ? OutputsPath : InputsPath);
            foreach (int row in Rows(table))
                if (table.Get(row, "item_key") == key && (!overStack.HasValue || Int(table.Get(row, "count")) > overStack.Value))
                    result.Add($"{table.Get(row, "recipe_key")} / {(output ? "产物" : "原料")} × {table.Get(row, "count")}");
        }
        return result;
    }

    public void DeleteItem(int row)
    {
        string key = GetItems().Single(i => i.row == row).key;
        var references = GetItemReferences(key);
        if (references.Count > 0) throw new InvalidDataException("物品仍被配方引用，请先处理：\n" + string.Join("\n", references));
        Edit("删除物品", docs => docs[ItemsPath].RemoveAt(row), ItemsPath);
    }

    public int AddRecipe()
    {
        string key = FreeKey(GetRecipes().Select(r => r.key), "new_recipe");
        int row = -1;
        Edit("新增配方", docs => row = docs[RecipesPath].Append(new Dictionary<string, string>
        { ["key"] = key, ["machine_type_key"] = GetMachineTypes().FirstOrDefault() ?? "", ["duration_seconds"] = "1" }), RecipesPath);
        return row;
    }

    public void SetRecipeValue(int row, string column, string value)
    {
        var recipes = GetRecipes();
        string oldKey = recipes.Single(r => r.row == row).key;
        if (column == "key")
        {
            RequireKey(value);
            if (recipes.Any(r => r.row != row && r.key == value)) throw new InvalidDataException($"配方 Key 已存在：{value}");
        }
        if (column == "duration_seconds" && (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float time) ||
            time <= 0 || float.IsNaN(time) || float.IsInfinity(time))) throw new InvalidDataException("生产时间必须为有限正数。");
        if (column == "machine_type_key" && !GetMachineTypes().Contains(value)) throw new InvalidDataException("请选择已有加工类型，或先新增类型。");
        Edit("编辑配方", docs =>
        {
            docs[RecipesPath].Set(row, column, value);
            if (column == "key")
                foreach (string path in new[] { InputsPath, OutputsPath })
                    foreach (int r in Rows(docs[path]).Where(r => docs[path].Get(r, "recipe_key") == oldKey))
                        docs[path].Set(r, "recipe_key", value);
        }, column == "key" ? new[] { RecipesPath, InputsPath, OutputsPath } : new[] { RecipesPath });
    }

    public void DeleteRecipe(int row)
    {
        string key = GetRecipes().Single(r => r.row == row).key;
        Edit("删除配方", docs =>
        {
            docs[RecipesPath].RemoveAt(row);
            foreach (string path in new[] { InputsPath, OutputsPath })
                foreach (int r in Rows(docs[path]).Where(r => docs[path].Get(r, "recipe_key") == key).Reverse().ToArray())
                    docs[path].RemoveAt(r);
        }, RecipesPath, InputsPath, OutputsPath);
    }

    public void AddMachineType(string key)
    {
        RequireKey(key);
        if (GetMachineTypes().Contains(key)) throw new InvalidDataException($"加工类型已存在：{key}");
        Edit("新增加工类型", docs => docs[MachinesPath].Append(new Dictionary<string, string> { ["key"] = key }), MachinesPath);
    }

    private void CheckCount(string itemKey, int count)
    {
        var item = GetItems().SingleOrDefault(i => i.key == itemKey);
        if (item == null) throw new InvalidDataException($"物品不存在：{itemKey}");
        if (count <= 0 || count > item.maxStack) throw new InvalidDataException($"{item.name} ({itemKey}) 数量必须为 1–{item.maxStack}，本次数量为 {count}。");
    }

    public void AddIngredient(string recipe, bool output, string itemKey, int count = 1)
    {
        if (!GetRecipes().Any(r => r.key == recipe)) throw new InvalidDataException($"配方不存在：{recipe}");
        string path = output ? OutputsPath : InputsPath;
        var existing = GetIngredients(recipe, output).SingleOrDefault(i => i.itemKey == itemKey);
        CheckCount(itemKey, count);
        int total = checked((existing?.count ?? 0) + count);
        CheckCount(itemKey, total);
        Edit("添加配方条目", docs =>
        {
            if (existing != null) docs[path].Set(existing.row, "count", total.ToString(CultureInfo.InvariantCulture));
            else docs[path].Append(new Dictionary<string, string> { ["recipe_key"] = recipe, ["item_key"] = itemKey, ["count"] = count.ToString(CultureInfo.InvariantCulture) });
        }, path);
    }

    public void ReplaceIngredient(string recipe, bool output, int row, string itemKey)
    {
        var ingredients = GetIngredients(recipe, output);
        var current = ingredients.Single(i => i.row == row);
        if (current.itemKey == itemKey) return;
        var existing = ingredients.SingleOrDefault(i => i.row != row && i.itemKey == itemKey);
        int total = checked(current.count + (existing?.count ?? 0));
        CheckCount(itemKey, total);
        string path = output ? OutputsPath : InputsPath;
        Edit("更换配方物品", docs =>
        {
            if (existing == null) docs[path].Set(row, "item_key", itemKey);
            else
            {
                docs[path].Set(existing.row, "count", total.ToString(CultureInfo.InvariantCulture));
                docs[path].RemoveAt(row);
            }
        }, path);
    }

    public void SetIngredientCount(string recipe, bool output, int row, int count)
    {
        string key = GetIngredients(recipe, output).Single(i => i.row == row).itemKey;
        CheckCount(key, count);
        string path = output ? OutputsPath : InputsPath;
        Edit("修改配方数量", docs => docs[path].Set(row, "count", count.ToString(CultureInfo.InvariantCulture)), path);
    }

    public void RemoveIngredient(string recipe, bool output, int row)
    {
        if (!GetIngredients(recipe, output).Any(i => i.row == row)) throw new InvalidDataException("配方条目不存在。");
        string path = output ? OutputsPath : InputsPath;
        Edit("移除配方条目", docs => docs[path].RemoveAt(row), path);
    }

    public void MoveOutput(string recipe, int index, int delta)
    {
        var rows = GetIngredients(recipe, true);
        int target = index + delta;
        if (index < 0 || index >= rows.Count || target < 0 || target >= rows.Count) return;
        Edit("移动产物槽位", docs => docs[OutputsPath].SwapRows(rows[index].row, rows[target].row), OutputsPath);
    }
}

public static class FactoryItemAssetBindings
{
    public static string PrefabKey(GameObject prefab)
    {
        if (prefab == null) return "";
        return AssetKey(prefab, FactoryDatabaseCsvImporter.ItemPrefabDirectory,
            FactoryDatabaseCsvImporter.BuildPrefabIndex(FactoryDatabaseCsvImporter.ItemPrefabDirectory));
    }

    public static string IconKey(Sprite icon)
    {
        if (icon == null) return "";
        return AssetKey(icon, FactoryDatabaseCsvImporter.ItemIconDirectory,
            FactoryDatabaseCsvImporter.BuildSpriteIndex(FactoryDatabaseCsvImporter.ItemIconDirectory));
    }

    private static string AssetKey<T>(T asset, string directory, Dictionary<string, T> index) where T : UnityEngine.Object
    {
        string path = AssetDatabase.GetAssetPath(asset);
        string key = Path.GetFileNameWithoutExtension(path);
        if (!path.StartsWith(directory + "/", StringComparison.Ordinal) ||
            !index.TryGetValue(FactoryDatabaseCsvImporter.NormalizeKey(key), out T resolved) || resolved != asset)
            throw new InvalidDataException($"请选择 {directory} 下按文件名唯一匹配的资产。");
        return key;
    }
}
