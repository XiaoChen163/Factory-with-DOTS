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
    public string modelPath;
    [UnityEngine.Serialization.FormerlySerializedAs("prefab")] public GameObject model;
    public string iconKey;
    public Sprite icon;
    public ItemCapabilityDefinition[] capabilities;
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
    public ushort storageSlotCount;
    public sbyte rampRiseHeightUnits;
    public FactoryBuildingPortTableRow[] ports;
    public FactoryColliderProxy[] colliders;
}

// Persistent roots reference domain libraries. Inline rows exist only for migration
// and isolated editor/test snapshots; persistent roots leave them empty.
public sealed class FactoryDatabaseAsset : ScriptableObject
{
    [Min(1)] public int version = 1;
    public FactoryItemDatabaseAsset itemDatabase;
    public FactoryRecipeDatabaseAsset recipeDatabase;
    public FactoryBuildingDatabaseAsset buildingDatabase;

    [SerializeField, HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("items")]
    private FactoryItemTableRow[] inlineItems = Array.Empty<FactoryItemTableRow>();
    [SerializeField, HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("recipes")]
    private FactoryRecipeTableRow[] inlineRecipes = Array.Empty<FactoryRecipeTableRow>();
    [SerializeField, HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("buildings")]
    private FactoryBuildingDefinitionRow[] inlineBuildings = Array.Empty<FactoryBuildingDefinitionRow>();
    [SerializeField, HideInInspector, UnityEngine.Serialization.FormerlySerializedAs("machineTypes")]
    private FactoryMachineTypeTableRow[] inlineMachines = Array.Empty<FactoryMachineTypeTableRow>();

    public FactoryItemTableRow[] items { get => itemDatabase != null ? itemDatabase.items : inlineItems; set { if (itemDatabase != null) itemDatabase.items = value; else inlineItems = value; } }
    public FactoryRecipeTableRow[] recipes { get => recipeDatabase != null ? recipeDatabase.recipes : inlineRecipes; set { if (recipeDatabase != null) recipeDatabase.recipes = value; else inlineRecipes = value; } }
    public FactoryBuildingDefinitionRow[] buildings { get => buildingDatabase != null ? buildingDatabase.buildings : inlineBuildings; set { if (buildingDatabase != null) buildingDatabase.buildings = value; else inlineBuildings = value; } }
    public FactoryMachineTypeTableRow[] machineTypes { get => buildingDatabase != null ? buildingDatabase.machineTypes : inlineMachines; set { if (buildingDatabase != null) buildingDatabase.machineTypes = value; else inlineMachines = value; } }

    public void ClearInlineData()
    {
        inlineItems = Array.Empty<FactoryItemTableRow>(); inlineRecipes = Array.Empty<FactoryRecipeTableRow>();
        inlineBuildings = Array.Empty<FactoryBuildingDefinitionRow>(); inlineMachines = Array.Empty<FactoryMachineTypeTableRow>();
    }

    [NonSerialized] public bool ownsInlineCapabilities;
    private void OnDestroy()
    {
        if (!ownsInlineCapabilities) return;
        var destroyed = new System.Collections.Generic.HashSet<ItemCapabilityDefinition>();
        foreach (var item in inlineItems)
            foreach (var capability in item.capabilities ?? Array.Empty<ItemCapabilityDefinition>())
                if (capability != null && destroyed.Add(capability)) DestroyImmediate(capability);
    }
}
