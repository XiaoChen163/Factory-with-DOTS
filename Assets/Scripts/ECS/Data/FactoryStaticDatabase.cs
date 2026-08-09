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
public struct BuildingTypeId : IEquatable<BuildingTypeId>
{
    public ushort Value;
    public bool IsValid => Value != 0;
    public bool Equals(BuildingTypeId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is BuildingTypeId other && Equals(other);
    public override int GetHashCode() => Value;
    public static bool operator ==(BuildingTypeId left, BuildingTypeId right) => left.Value == right.Value;
    public static bool operator !=(BuildingTypeId left, BuildingTypeId right) => left.Value != right.Value;
}

[Serializable]
public struct BuildingLevelId : IEquatable<BuildingLevelId>
{
    public ushort Value;
    public bool IsValid => Value != 0;
    public bool Equals(BuildingLevelId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is BuildingLevelId other && Equals(other);
    public override int GetHashCode() => Value;
    public static bool operator ==(BuildingLevelId left, BuildingLevelId right) => left.Value == right.Value;
    public static bool operator !=(BuildingLevelId left, BuildingLevelId right) => left.Value != right.Value;
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

public struct FactoryBuildingBlob
{
    public BuildingTypeId Id;
    public BuildingKind Kind;
    public MachineTypeId MachineType;
    public int PortStart;
    public ushort PortCount;
    public byte FootprintWidth;
    public byte FootprintHeight;
    public FixedString64Bytes Key;
    public FixedString64Bytes NameKey;
}

public struct FactoryBuildingLevelBlob
{
    public BuildingLevelId Id;
    public BuildingTypeId BuildingId;
    public byte Level;
    public int MenuOrder;
    public FixedString64Bytes Key;
    public FixedString64Bytes NameKey;
    public FixedString64Bytes VisualPrefabKey;
}

public struct FactoryBeltLevelBlob
{
    public BuildingLevelId LevelId;
    public float CellsPerSecond;
}

public struct FactoryProcessorLevelBlob
{
    public BuildingLevelId LevelId;
    public ushort WorkRatePermille;
}

public struct FactoryStorageLevelBlob
{
    public BuildingLevelId LevelId;
    public int Capacity;
    public ushort SlotCount;
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
    public BlobArray<FactoryBuildingBlob> BuildingsById;
    public BlobArray<FactoryBuildingLevelBlob> BuildingLevelsById;
    public BlobArray<FactoryBeltLevelBlob> BeltLevelsById;
    public BlobArray<FactoryProcessorLevelBlob> ProcessorLevelsById;
    public BlobArray<FactoryStorageLevelBlob> StorageLevelsById;
    public BlobArray<BuildingLevelId> BuildingLevelMenu;
    public BlobArray<FactoryBuildingPortBlob> BuildingPorts;
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
    public ushort WorkRatePermille;
}

public struct BuildingIdentity : IComponentData
{
    public BuildingTypeId BuildingType;
    public BuildingLevelId BuildingLevel;
    public byte Level;
}

public static class FactoryDatabaseUtility
{
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


    public static bool TryGetBuildingLevel(
        ref FactoryDatabaseBlob database,
        BuildingLevelId levelId,
        out FactoryBuildingLevelBlob level,
        out FactoryBuildingBlob building)
    {
        int levelIndex = levelId.Value;
        if (!levelId.IsValid || levelIndex >= database.BuildingLevelsById.Length)
        {
            level = default;
            building = default;
            return false;
        }

        level = database.BuildingLevelsById[levelIndex];
        int buildingIndex = level.BuildingId.Value;
        if (level.Id != levelId || buildingIndex <= 0 ||
            buildingIndex >= database.BuildingsById.Length)
        {
            building = default;
            return false;
        }

        building = database.BuildingsById[buildingIndex];
        return building.Id == level.BuildingId;
    }

    public static bool TryGetBeltLevel(
        ref FactoryDatabaseBlob database,
        BuildingLevelId levelId,
        out FactoryBeltLevelBlob beltLevel)
    {
        int index = levelId.Value;
        if (!levelId.IsValid || index >= database.BeltLevelsById.Length)
        {
            beltLevel = default;
            return false;
        }

        beltLevel = database.BeltLevelsById[index];
        return beltLevel.LevelId == levelId &&
               !float.IsNaN(beltLevel.CellsPerSecond) &&
               !float.IsInfinity(beltLevel.CellsPerSecond) &&
               beltLevel.CellsPerSecond > 0f;
    }

    public static bool TryGetProcessorLevel(
        ref FactoryDatabaseBlob database,
        BuildingLevelId levelId,
        out FactoryProcessorLevelBlob processorLevel)
    {
        int index = levelId.Value;
        if (!levelId.IsValid || index >= database.ProcessorLevelsById.Length)
        {
            processorLevel = default;
            return false;
        }

        processorLevel = database.ProcessorLevelsById[index];
        return processorLevel.LevelId == levelId &&
               processorLevel.WorkRatePermille > 0;
    }


    public static bool TryGetStorageLevel(
        ref FactoryDatabaseBlob database,
        BuildingLevelId levelId,
        out FactoryStorageLevelBlob storageLevel)
    {
        int index = levelId.Value;
        if (!levelId.IsValid || index >= database.StorageLevelsById.Length)
        {
            storageLevel = default;
            return false;
        }

        storageLevel = database.StorageLevelsById[index];
        return storageLevel.LevelId == levelId && storageLevel.Capacity > 0;
    }
}
