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

    private void OnEnable() => BuildIndex();

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
