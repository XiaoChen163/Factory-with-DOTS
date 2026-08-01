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
    public BuildingKind MachineType;
    public int DurationTicks;
    public int InputStart;
    public ushort InputCount;
    public int OutputStart;
    public ushort OutputCount;
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
    public BuildingKind MachineType;
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
        BuildingKind machineType)
    {
        int index = (int)machineType;
        return index >= 0 &&
               index < database.RecipeRangesByMachine.Length
            ? database.RecipeRangesByMachine[index]
            : default;
    }
}
