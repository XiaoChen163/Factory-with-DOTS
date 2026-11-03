using System;
using UnityEngine;

[Serializable]
public sealed class FactoryBuildingImportData
{
    public string buildingKey, modelFile, sourceJsonPath;
    public GameObject model;
    public GameObject generatedPrefab;
    public FactoryColliderProxy[] colliders = Array.Empty<FactoryColliderProxy>();
}

// Editor-only import hints. CSV remains the source for all gameplay/spatial fields.
public sealed class FactoryBuildingImportSettings : ScriptableObject
{
    public FactoryBuildingImportData data = new FactoryBuildingImportData();
}
