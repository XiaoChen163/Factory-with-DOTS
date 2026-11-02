using System;
using UnityEngine;

[Serializable]
public sealed class FactoryColliderProxy
{
    public string name, type, axis;
    public Vector3 position;
    public Quaternion rotation = Quaternion.identity;
    public Vector3 size;
    public float radius, height;
}

[Serializable]
public sealed class FactoryBuildingImportData
{
    public string buildingKey, modelFile, sourceJsonPath;
    public FactoryColliderProxy[] colliders = Array.Empty<FactoryColliderProxy>();
}

// Editor-only import hints. CSV remains the source for all gameplay/spatial fields.
public sealed class FactoryBuildingImportSettings : ScriptableObject
{
    public FactoryBuildingImportData data = new FactoryBuildingImportData();
}
