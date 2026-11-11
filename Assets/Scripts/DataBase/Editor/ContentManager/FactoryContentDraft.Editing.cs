using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Views of the typed asset draft. Row ordinals refer to the current array order.
public sealed class FactoryContentItem
{
    public int row, id, maxStack;
    public string key, name, modelPath, iconKey;
    public FactoryItemCategory category;
    public GameObject model;
    public Sprite icon;
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
    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
    public static bool Matches(string value, string query) => (value ?? "").IndexOf(query ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
    private static void RequireKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsWhiteSpace)) throw new InvalidDataException("Key 不能为空或包含空白字符。");
    }
    private static string FreeKey(IEnumerable<string> keys, string prefix)
    {
        var used = new HashSet<string>(keys, StringComparer.Ordinal);
        string key = prefix;
        for (int i = 2; used.Contains(key); i++) key = prefix + "_" + i;
        return key;
    }
    public List<FactoryContentItem> GetItems(string search = "") => content.items.Select((i, row) => new FactoryContentItem
    {
        row = row, id = i.id, key = i.key, name = i.nameKey, maxStack = i.maxStack, category = i.category,
        modelPath = AssetDatabase.GetAssetPath(i.model), iconKey = i.iconKey, model = i.model, icon = i.icon
    }).Where(i => Matches(i.key, search) || Matches(i.name, search)).ToList();

    public List<FactoryContentRecipe> GetRecipes(string search = "") => content.recipes.Select((r, row) => new FactoryContentRecipe
    { row = row, key = r.key, machine = r.machineTypeKey, duration = r.durationSeconds })
        .Where(r => Matches(r.key, search) || Matches(r.machine, search)).ToList();
    public List<string> GetMachineTypes() => content.machineTypes.Select(m => m.key).ToList();
    private FactoryRecipeTableRow Recipe(string key) => content.recipes.FirstOrDefault(r => r.key == key);
    public List<FactoryContentIngredient> GetIngredients(string recipe, bool output) =>
        ((output ? Recipe(recipe).outputs : Recipe(recipe).inputs) ?? Array.Empty<FactoryRecipeIngredientTableRow>()).Select((i, row) => new FactoryContentIngredient
        { row = row, itemKey = i.itemKey, count = i.count }).ToList();

    public int AddItem()
    {
        int row = content.items.Length;
        string key = FreeKey(content.items.Select(i => i.key), "new_item");
        Edit("新增物品", c => c.items = c.items.Append(new FactoryItemTableRow
        { id = NextId(c.items.Select(i => i.id)), key = key, nameKey = key, maxStack = 64, category = FactoryItemCategory.Component }).ToArray());
        return row;
    }
    public void SetItemValue(int row, string column, string value)
    {
        Edit("编辑物品", c =>
        {
            var item = c.items[row];
            string oldKey = item.key;
            switch (column)
            {
                case "key":
                    RequireKey(value);
                    if (c.items.Where((i, n) => n != row).Any(i => i.key == value)) throw new InvalidDataException("物品 Key 已存在：" + value);
                    item.key = value; break;
                case "id":
                    if (!ushort.TryParse(value, out ushort id) || id == 0 || c.items.Where((i, n) => n != row).Any(i => i.id == id))
                        throw new InvalidDataException("物品 ID 必须为 1–65535，且不能重复。");
                    item.id = id; break;
                case "name_key": item.nameKey = value; break;
                case "max_stack":
                    if (!ushort.TryParse(value, out ushort stack) || stack == 0) throw new InvalidDataException("最大堆叠必须为 1–65535。");
                    item.maxStack = stack; break;
                case "category": item.category = Enum.Parse<FactoryItemCategory>(value); break;
                case "model_path": item.model = string.IsNullOrEmpty(value) ? null : FactoryDatabaseEditor.LoadModel(value); item.modelPath = value; break;
                case "icon_key":
                    item.iconKey = value;
                    var icons = FactoryDatabaseEditor.BuildSpriteIndex(FactoryDatabaseEditor.ItemIconDirectory);
                    icons.TryGetValue(FactoryDatabaseEditor.NormalizeKey(string.IsNullOrEmpty(value) ? item.key : value), out item.icon);
                    if (!string.IsNullOrEmpty(value) && item.icon == null) throw new InvalidDataException("图标不存在：" + value);
                    break;
                default: throw new ArgumentException("未知物品字段：" + column);
            }
            c.items[row] = item;
            if (column == "key" || column == "id")
                for (int n = 0; n < c.recipes.Length; n++)
                {
                    var r = c.recipes[n];
                    r.inputs = RenameIngredient(r.inputs, oldKey, item);
                    r.outputs = RenameIngredient(r.outputs, oldKey, item);
                    c.recipes[n] = r;
                }
        });
    }
    private static FactoryRecipeIngredientTableRow[] RenameIngredient(FactoryRecipeIngredientTableRow[] values, string oldKey, FactoryItemTableRow item) =>
        values.Select(i => { if (i.itemKey == oldKey) { i.itemKey = item.key; i.itemId = item.id; } return i; }).ToArray();
    public void SetItemIcon(int row, Sprite icon) => Edit("设置物品图标", c =>
    {
        c.items[row].icon = icon;
        c.items[row].iconKey = icon == null ? "" : Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(icon));
    });
    public List<string> GetItemReferences(string key, int? overStack = null)
    {
        var result = new List<string>();
        foreach (var recipe in content.recipes)
            foreach (bool output in new[] { false, true })
                foreach (var i in output ? recipe.outputs : recipe.inputs)
                    if (i.itemKey == key && (!overStack.HasValue || i.count > overStack.Value))
                        result.Add($"{recipe.key} / {(output ? "产物" : "原料")} × {i.count}");
        return result;
    }
    public void DeleteItem(int row)
    {
        var references = GetItemReferences(content.items[row].key);
        if (references.Count > 0) throw new InvalidDataException("物品仍被配方引用，请先处理：\n" + string.Join("\n", references));
        Edit("删除物品", c => c.items = c.items.Where((i, n) => n != row).ToArray());
    }
    public int AddRecipe()
    {
        int row = content.recipes.Length;
        Edit("新增配方", c =>
        {
            var machine = c.machineTypes.FirstOrDefault();
            c.recipes = c.recipes.Append(new FactoryRecipeTableRow
            {
                id = NextId(c.recipes.Select(r => r.id)), key = FreeKey(c.recipes.Select(r => r.key), "new_recipe"),
                machineTypeKey = machine.key ?? "", machineTypeId = machine.id, durationSeconds = 1,
                inputs = Array.Empty<FactoryRecipeIngredientTableRow>(), outputs = Array.Empty<FactoryRecipeIngredientTableRow>()
            }).ToArray();
        });
        return row;
    }
    public void SetRecipeValue(int row, string column, string value) => Edit("编辑配方", c =>
    {
        var recipe = c.recipes[row];
        switch (column)
        {
            case "key":
                RequireKey(value);
                if (c.recipes.Where((r, n) => n != row).Any(r => r.key == value)) throw new InvalidDataException("配方 Key 已存在：" + value);
                recipe.key = value; break;
            case "duration_seconds":
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float time) || time <= 0 || float.IsNaN(time) || float.IsInfinity(time))
                    throw new InvalidDataException("生产时间必须为有限正数。");
                recipe.durationSeconds = time; break;
            case "machine_type_key":
                var machine = c.machineTypes.SingleOrDefault(m => m.key == value);
                if (machine.id == 0) throw new InvalidDataException("请选择已有加工类型，或先新增类型。");
                recipe.machineTypeKey = machine.key; recipe.machineTypeId = machine.id; break;
            default: throw new ArgumentException("未知配方字段：" + column);
        }
        c.recipes[row] = recipe;
    });
    public void DeleteRecipe(int row) => Edit("删除配方", c => c.recipes = c.recipes.Where((r, n) => n != row).ToArray());
    public void AddMachineType(string key)
    {
        RequireKey(key);
        if (GetMachineTypes().Contains(key)) throw new InvalidDataException("加工类型已存在：" + key);
        Edit("新增加工类型", c => c.machineTypes = c.machineTypes.Append(new FactoryMachineTypeTableRow
        { id = NextId(c.machineTypes.Select(m => m.id)), key = key }).ToArray());
    }
    private void CheckCount(string itemKey, int count)
    {
        var item = content.items.SingleOrDefault(i => i.key == itemKey);
        if (item.id == 0) throw new InvalidDataException("物品不存在：" + itemKey);
        if (count <= 0 || count > item.maxStack) throw new InvalidDataException($"{item.nameKey} ({itemKey}) 数量必须为 1–{item.maxStack}，本次数量为 {count}。");
    }
    private void EditIngredients(string recipe, bool output, Action<List<FactoryRecipeIngredientTableRow>> edit) => Edit("编辑配方条目", c =>
    {
        int row = Array.FindIndex(c.recipes, r => r.key == recipe);
        if (row < 0) throw new InvalidDataException("配方不存在：" + recipe);
        var r = c.recipes[row];
        var list = (output ? r.outputs : r.inputs).ToList();
        edit(list);
        if (output) r.outputs = list.ToArray(); else r.inputs = list.ToArray();
        c.recipes[row] = r;
    });
    public void AddIngredient(string recipe, bool output, string itemKey, int count = 1)
    {
        CheckCount(itemKey, count);
        EditIngredients(recipe, output, list =>
        {
            int index = list.FindIndex(i => i.itemKey == itemKey);
            int total = checked(count + (index < 0 ? 0 : list[index].count));
            CheckCount(itemKey, total);
            var value = new FactoryRecipeIngredientTableRow { itemKey = itemKey, itemId = content.items.Single(i => i.key == itemKey).id, count = total };
            if (index < 0) list.Add(value); else list[index] = value;
        });
    }
    public void ReplaceIngredient(string recipe, bool output, int row, string itemKey) => EditIngredients(recipe, output, list =>
    {
        if (list[row].itemKey == itemKey) return;
        int existing = list.FindIndex(i => i.itemKey == itemKey);
        int total = checked(list[row].count + (existing < 0 ? 0 : list[existing].count));
        CheckCount(itemKey, total);
        var value = new FactoryRecipeIngredientTableRow { itemKey = itemKey, itemId = content.items.Single(i => i.key == itemKey).id, count = total };
        if (existing < 0) list[row] = value;
        else { list[existing] = value; list.RemoveAt(row); }
    });
    public void SetIngredientCount(string recipe, bool output, int row, int count) => EditIngredients(recipe, output, list =>
    {
        var value = list[row]; CheckCount(value.itemKey, count); value.count = count; list[row] = value;
    });
    public void RemoveIngredient(string recipe, bool output, int row) => EditIngredients(recipe, output, list => list.RemoveAt(row));
    public void MoveOutput(string recipe, int index, int delta) => EditIngredients(recipe, true, list =>
    {
        int target = index + delta;
        if (index < 0 || index >= list.Count || target < 0 || target >= list.Count) return;
        var value = list[index]; list[index] = list[target]; list[target] = value;
    });
}

public static class FactoryItemAssetBindings
{
    public static string ModelPath(GameObject model)
    {
        if (model == null) return "";
        string path = AssetDatabase.GetAssetPath(model);
        FactoryDatabaseEditor.LoadModel(path);
        return path;
    }
}
