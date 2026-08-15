using System;
using UnityEngine;

public enum FactoryItemCategory : byte
{
    None,
    Ore,
    Ingot,
    Component
}

public enum FactoryBuildingBehavior : byte
{
    Belt = 1,
    Processor = 2,
    Storage = 3,
    Merger = 4,
    Splitter = 5,
    Foundation = 6
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
    public Vector2Int cellOffset;
    public Vector2Int direction;
}

[Serializable]
public struct FactoryBuildingTableRow
{
    public ushort id;
    public string key;
    public string nameKey;
    public FactoryBuildingBehavior behavior;
    public BuildingKind kind;
    public string machineTypeKey;
    public ushort machineTypeId;
    public string portLayoutKey;
    public byte footprintWidth;
    public byte footprintHeight;
    public FactoryBuildingPortTableRow[] ports;
}

[Serializable]
public struct FactoryBuildingLevelTableRow
{
    public ushort id;
    public string key;
    public string buildingKey;
    public ushort buildingId;
    public byte level;
    public string nameKey;
    public string visualPrefabKey;
    public GameObject visualPrefab;
    public string iconKey;
    public Sprite icon;
    public int menuOrder;
}

[Serializable]
public struct FactoryBeltLevelTableRow
{
    public string buildingLevelKey;
    public ushort buildingLevelId;
    public float cellsPerSecond;
}

[Serializable]
public struct FactoryProcessorLevelTableRow
{
    public string buildingLevelKey;
    public ushort buildingLevelId;
    public ushort workRatePermille;
}

[Serializable]
public struct FactoryStorageLevelTableRow
{
    public string buildingLevelKey;
    public ushort buildingLevelId;
    public int capacity;
    public ushort slotCount;
}

// Generated cache. Edit the CSV files in Assets/Data/FactoryTables instead.
public sealed class FactoryDatabaseAsset : ScriptableObject
{
    [Min(1)] public int version = 1;
    public FactoryItemTableRow[] items = Array.Empty<FactoryItemTableRow>();
    public FactoryMachineTypeTableRow[] machineTypes =
        Array.Empty<FactoryMachineTypeTableRow>();
    public FactoryBuildingTableRow[] buildings =
        Array.Empty<FactoryBuildingTableRow>();
    public FactoryBuildingLevelTableRow[] buildingLevels =
        Array.Empty<FactoryBuildingLevelTableRow>();
    public FactoryBeltLevelTableRow[] beltLevels =
        Array.Empty<FactoryBeltLevelTableRow>();
    public FactoryProcessorLevelTableRow[] processorLevels =
        Array.Empty<FactoryProcessorLevelTableRow>();
    public FactoryStorageLevelTableRow[] storageLevels =
        Array.Empty<FactoryStorageLevelTableRow>();
    public FactoryRecipeTableRow[] recipes =
        Array.Empty<FactoryRecipeTableRow>();
}
