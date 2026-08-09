using System;
using UnityEngine;

[CreateAssetMenu(
    fileName = "FactoryPresentationCatalog",
    menuName = "Factory/Presentation Catalog")]
public sealed class FactoryPresentationCatalog : ScriptableObject
{
    [SerializeField] private FactoryDatabaseAsset database;

    private Sprite[] itemIcons = Array.Empty<Sprite>();
    private Sprite[] buildingIcons = Array.Empty<Sprite>();

    public void BuildIndex()
    {
        if (database == null)
        {
            itemIcons = Array.Empty<Sprite>();
            buildingIcons = Array.Empty<Sprite>();
            return;
        }

        itemIcons = BuildItemIndex(database.items);
        buildingIcons = BuildBuildingIndex(database.buildingLevels);
    }

    public Sprite GetItemIcon(ItemId id) =>
        id.Value < itemIcons.Length ? itemIcons[id.Value] : null;

    public Sprite GetBuildingIcon(BuildingLevelId id) =>
        id.Value < buildingIcons.Length ? buildingIcons[id.Value] : null;

    public string GetItemName(ItemId id)
    {
        if (database != null)
            for (int i = 0; i < database.items.Length; i++)
                if (database.items[i].id == id.Value)
                    return DisplayName(database.items[i].nameKey, database.items[i].key);
        return id.IsValid ? $"物品 {id.Value}" : "空";
    }

    public string GetBuildingName(BuildingLevelId id)
    {
        if (database != null)
            for (int i = 0; i < database.buildingLevels.Length; i++)
                if (database.buildingLevels[i].id == id.Value)
                    return DisplayName(database.buildingLevels[i].nameKey,
                        database.buildingLevels[i].key);
        return $"建筑 {id.Value}";
    }

    public string GetRecipeName(RecipeId id)
    {
        if (database != null)
            for (int i = 0; i < database.recipes.Length; i++)
                if (database.recipes[i].id == id.Value)
                    return DisplayName(null, database.recipes[i].key);
        return $"配方 {id.Value}";
    }

    private void OnEnable() => BuildIndex();

    private static string DisplayName(string nameKey, string fallback)
    {
        string value = string.IsNullOrWhiteSpace(nameKey) ? fallback : nameKey;
        return string.IsNullOrWhiteSpace(value) ? "未命名" : value.Replace('_', ' ');
    }

    private static Sprite[] BuildItemIndex(FactoryItemTableRow[] rows)
    {
        int max = 0;
        for (int i = 0; i < rows.Length; i++)
            max = Math.Max(max, rows[i].id);
        Sprite[] result = new Sprite[max + 1];
        for (int i = 0; i < rows.Length; i++)
            result[rows[i].id] = rows[i].icon;
        return result;
    }

    private static Sprite[] BuildBuildingIndex(FactoryBuildingLevelTableRow[] rows)
    {
        int max = 0;
        for (int i = 0; i < rows.Length; i++)
            max = Math.Max(max, rows[i].id);
        Sprite[] result = new Sprite[max + 1];
        for (int i = 0; i < rows.Length; i++)
            result[rows[i].id] = rows[i].icon;
        return result;
    }
}
