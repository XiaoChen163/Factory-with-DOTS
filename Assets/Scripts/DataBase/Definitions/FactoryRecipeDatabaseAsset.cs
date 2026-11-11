using System;
using UnityEngine;

public sealed class FactoryRecipeDatabaseAsset : ScriptableObject
{
    public FactoryRecipeTableRow[] recipes = Array.Empty<FactoryRecipeTableRow>();
}
