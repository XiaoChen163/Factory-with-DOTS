using System;
using UnityEngine;

public sealed class FactoryBuildingDatabaseAsset : ScriptableObject
{
    public FactoryBuildingDefinitionRow[] buildings = Array.Empty<FactoryBuildingDefinitionRow>();
    public FactoryMachineTypeTableRow[] machineTypes = Array.Empty<FactoryMachineTypeTableRow>();
}
