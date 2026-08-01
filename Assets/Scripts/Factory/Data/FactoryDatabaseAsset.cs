using System;
using UnityEngine;

public enum FactoryItemCategory : byte
{
    None,
    Ore,
    Ingot,
    Component
}

[Serializable]
public struct FactoryItemTableRow
{
    public ushort id;
    public string key;
    public string nameKey;
    public ushort maxStack;
    public FactoryItemCategory category;
    public string prefabKey;
    public GameObject prefab;
}

[Serializable]
public struct FactoryRecipeIngredientTableRow
{
    public ushort itemId;
    public int count;
}

[Serializable]
public struct FactoryRecipeTableRow
{
    public ushort id;
    public string key;
    public BuildingKind machineType;
    public float durationSeconds;
    public FactoryRecipeIngredientTableRow[] inputs;
    public FactoryRecipeIngredientTableRow[] outputs;
}

// Generated cache. Edit the CSV files in Assets/Data/FactoryTables instead.
public sealed class FactoryDatabaseAsset : ScriptableObject
{
    [Min(1)] public int version = 1;
    public FactoryItemTableRow[] items = Array.Empty<FactoryItemTableRow>();
    public FactoryRecipeTableRow[] recipes =
        Array.Empty<FactoryRecipeTableRow>();
}
