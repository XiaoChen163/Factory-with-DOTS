using System;
using UnityEngine;

[Serializable]
public sealed class FactoryBuildingImportData
{
    public string buildingKey, modelFile, sourceJsonPath;
    public GameObject model;
    public FactoryColliderProxy[] colliders = Array.Empty<FactoryColliderProxy>();
}

