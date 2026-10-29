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
    public string iconKey;
    public Sprite icon;
}

[Serializable]
public struct FactoryRecipeIngredientTableRow
{
    public string itemKey;
    public ushort itemId;
    public int count;
}

[Serializable]
public struct FactoryRecipeTableRow
{
    public ushort id;
    public string key;
    public string machineTypeKey;
    public ushort machineTypeId;
    public float durationSeconds;
    public FactoryRecipeIngredientTableRow[] inputs;
    public FactoryRecipeIngredientTableRow[] outputs;
}

[Serializable]
public struct FactoryMachineTypeTableRow
{
    public ushort id;
    public string key;
}

[Serializable]
public struct FactoryBuildingPortTableRow
{
    public BuildingPortType type;
    public byte index;
    public ushort number;
    // One-based recipe output ordinal. Zero means this is not a processor output.
    public ushort outputSlot;
    public Vector2Int cellOffset;
    public Vector2Int direction;
}

[Serializable]
public struct FactoryBuildingDefinitionRow
{
    public ushort id;
    public string key;
    public BuildingKind kind;
    public byte footprintWidth;
    public byte footprintHeight;
    public bool isRectangle;
    public string footprintBitmap;
    public string visualPrefabPath;
    public GameObject visualPrefab;
    public string displayName;
    public Sprite icon;
    public int menuOrder;
    public ushort machineTypeId;
    public byte beltTier;
    public float cellsPerSecond;
    public int storageCapacity;
    public ushort storageSlotCount;
    public sbyte rampRiseHeightUnits;
    public FactoryBuildingPortTableRow[] ports;
}

// Generated cache. Edit the CSV files in Assets/Data/FactoryTables instead.
public sealed class FactoryDatabaseAsset : ScriptableObject
{
    [Min(1)] public int version = 1;
    public FactoryItemTableRow[] items = Array.Empty<FactoryItemTableRow>();
    public FactoryMachineTypeTableRow[] machineTypes =
        Array.Empty<FactoryMachineTypeTableRow>();
    public FactoryBuildingDefinitionRow[] buildings =
        Array.Empty<FactoryBuildingDefinitionRow>();
    public FactoryRecipeTableRow[] recipes =
        Array.Empty<FactoryRecipeTableRow>();
}
