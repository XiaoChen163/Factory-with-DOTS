using System;
using Unity.Collections;
using Unity.Entities;

[Serializable]
public struct ItemId : IEquatable<ItemId>
{
    public ushort Value;

    public bool IsValid => Value != 0;
    public static ItemId Invalid => default;

    public bool Equals(ItemId other)
    {
        return Value == other.Value;
    }

    public override bool Equals(object obj)
    {
        return obj is ItemId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value;
    }

    public static bool operator ==(ItemId left, ItemId right)
    {
        return left.Value == right.Value;
    }

    public static bool operator !=(ItemId left, ItemId right)
    {
        return left.Value != right.Value;
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}

[Serializable]
public struct RecipeId : IEquatable<RecipeId>
{
    public ushort Value;

    public bool IsValid => Value != 0;
    public static RecipeId Invalid => default;

    public bool Equals(RecipeId other)
    {
        return Value == other.Value;
    }

    public override bool Equals(object obj)
    {
        return obj is RecipeId other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Value;
    }

    public static bool operator ==(RecipeId left, RecipeId right)
    {
        return left.Value == right.Value;
    }

    public static bool operator !=(RecipeId left, RecipeId right)
    {
        return left.Value != right.Value;
    }

    public override string ToString()
    {
        return Value.ToString();
    }
}

[Serializable]
public struct MachineTypeId : IEquatable<MachineTypeId>
{
    public ushort Value;
    public bool IsValid => Value != 0;
    public bool Equals(MachineTypeId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is MachineTypeId other && Equals(other);
    public override int GetHashCode() => Value;
    public static bool operator ==(MachineTypeId left, MachineTypeId right) => left.Value == right.Value;
    public static bool operator !=(MachineTypeId left, MachineTypeId right) => left.Value != right.Value;
}

[Serializable]
public struct BuildingDefinitionId : IEquatable<BuildingDefinitionId>
{
    public ushort Value;
    public bool IsValid => Value != 0;
    public bool Equals(BuildingDefinitionId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is BuildingDefinitionId other && Equals(other);
    public override int GetHashCode() => Value;
    public static bool operator ==(BuildingDefinitionId left, BuildingDefinitionId right) => left.Value == right.Value;
    public static bool operator !=(BuildingDefinitionId left, BuildingDefinitionId right) => left.Value != right.Value;
}

public struct FactoryItemBlob
{
    public ItemId Id;
    public ushort MaxStack;
    public byte Category;
    public FixedString64Bytes Key;
    public FixedString64Bytes NameKey;
}

public struct FactoryRecipeIngredientBlob
{
    public ItemId ItemId;
    public int Count;
}

public struct FactoryRecipeBlob
{
    public RecipeId Id;
    public MachineTypeId MachineType;
    public int DurationTicks;
    public int InputStart;
    public ushort InputCount;
    public int OutputStart;
    public ushort OutputCount;
    public FixedString64Bytes Key;
}

public struct FactoryMachineTypeBlob
{
    public MachineTypeId Id;
    public FixedString64Bytes Key;
}

public struct FactoryBuildingPortBlob
{
    public Unity.Mathematics.int2 CellOffset;
    public Unity.Mathematics.int2 Direction;
    public BuildingPortType Type;
    public byte Index;
}

public struct FactoryBuildingDefinitionBlob
{
    public BuildingDefinitionId Id;
    public BuildingKind Kind;
    public MachineTypeId MachineType;
    public int PortStart;
    public ushort PortCount;
    public byte FootprintWidth;
    public byte FootprintHeight;
    public int OccupiedCellStart;
    public int OccupiedCellCount;
    public int MenuOrder;
    public byte BeltTier;
    public float CellsPerSecond;
    public int Capacity;
    public ushort SlotCount;
    public sbyte RampRiseHeightUnits;
    public FixedString64Bytes Key;
}

public struct FactoryRecipeRangeBlob
{
    public int Start;
    public int Count;
}

public struct FactoryDatabaseBlob
{
    public int Version;
    public BlobArray<FactoryItemBlob> ItemsById;
    public BlobArray<FactoryMachineTypeBlob> MachineTypesById;
    public BlobArray<FactoryBuildingDefinitionBlob> BuildingDefinitionsById;
    public BlobArray<BuildingDefinitionId> BuildingDefinitionMenu;
    public BlobArray<FactoryBuildingPortBlob> BuildingPorts;
    public BlobArray<Unity.Mathematics.int2> BuildingOccupiedCells;
    public BlobArray<FactoryRecipeBlob> Recipes;
    public BlobArray<FactoryRecipeIngredientBlob> Inputs;
    public BlobArray<FactoryRecipeIngredientBlob> Outputs;
    public BlobArray<FactoryRecipeRangeBlob> RecipeRangesByMachine;
}

public struct FactoryDatabase : IComponentData
{
    public BlobAssetReference<FactoryDatabaseBlob> Value;
}

public struct ItemProcessor : IComponentData
{
    public MachineTypeId MachineType;
}

public struct BuildingIdentity : IComponentData
{
    public BuildingDefinitionId BuildingDefinition;
}

public static class FactoryDatabaseUtility
{
    // Resolve content keys at setup/UI boundaries; simulation continues using numeric IDs.
    public static BuildingDefinitionId GetBuildingDefinitionId(
        ref FactoryDatabaseBlob database, FixedString64Bytes key)
    {
        for (int i = 1; i < database.BuildingDefinitionsById.Length; i++)
            if (database.BuildingDefinitionsById[i].Id.IsValid && database.BuildingDefinitionsById[i].Key == key)
                return database.BuildingDefinitionsById[i].Id;
        throw new ArgumentException($"Unknown building key '{key}'.");
    }

    public static bool IsValidItem(
        ref FactoryDatabaseBlob database,
        ItemId itemId)
    {
        return itemId.IsValid &&
               itemId.Value < database.ItemsById.Length &&
               database.ItemsById[itemId.Value].Id == itemId;
    }

    public static FactoryRecipeRangeBlob GetRecipeRange(
        ref FactoryDatabaseBlob database,
        MachineTypeId machineType)
    {
        int index = machineType.Value;
        return index >= 0 &&
               index < database.RecipeRangesByMachine.Length
            ? database.RecipeRangesByMachine[index]
            : default;
    }


    public static bool TryGetBuildingDefinition(
        ref FactoryDatabaseBlob database,
        BuildingDefinitionId id,
        out FactoryBuildingDefinitionBlob building)
    {
        if (!id.IsValid || id.Value >= database.BuildingDefinitionsById.Length)
        {
            building = default;
            return false;
        }
        building = database.BuildingDefinitionsById[id.Value];
        return building.Id == id;
    }

    public static bool TryGetBeltStats(ref FactoryDatabaseBlob database,
        BuildingDefinitionId id, out FactoryBuildingDefinitionBlob building)
    {
        return TryGetBuildingDefinition(ref database, id, out building) &&
            (building.Kind == BuildingKind.Belt || VerticalBeltUtility.IsVertical(building.Kind)) &&
            building.BeltTier > 0 && building.CellsPerSecond > 0f &&
            !float.IsNaN(building.CellsPerSecond) && !float.IsInfinity(building.CellsPerSecond);
    }

    public static bool TryGetStorageStats(ref FactoryDatabaseBlob database,
        BuildingDefinitionId id, out FactoryBuildingDefinitionBlob building)
    {
        return TryGetBuildingDefinition(ref database, id, out building) &&
            building.Kind == BuildingKind.Storage && building.Capacity > 0 && building.SlotCount > 0;
    }
}
