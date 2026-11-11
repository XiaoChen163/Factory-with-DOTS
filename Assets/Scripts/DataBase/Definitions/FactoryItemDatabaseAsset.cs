using System;
using UnityEngine;

public sealed class FactoryItemDatabaseAsset : ScriptableObject
{
    public FactoryItemTableRow[] items = Array.Empty<FactoryItemTableRow>();
}
