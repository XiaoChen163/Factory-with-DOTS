using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed partial class FactoryContentDraft
{
    // Pairing hints live only in the editor draft; model and collision data live in the database asset.
    [SerializeField] private List<FactoryBuildingImportData> buildingImports = new List<FactoryBuildingImportData>();
    private void LoadBuildingImports() => buildingImports.Clear();

    public FactoryBuildingImportData GetBuildingImport(string key)
    {
        var hint = buildingImports.FirstOrDefault(i => i.buildingKey == key);
        var building = GetBuildings().SingleOrDefault(b => b.key == key);
        var definition = content.buildings.FirstOrDefault(b => b.key == key);
        return new FactoryBuildingImportData
        {
            buildingKey = key, modelFile = hint?.modelFile, sourceJsonPath = hint?.sourceJsonPath,
            model = definition.visualPrefab,
            colliders = definition.colliders == null ? Array.Empty<FactoryColliderProxy>() : definition.colliders.Select(c => c == null ? null : JsonUtility.FromJson<FactoryColliderProxy>(JsonUtility.ToJson(c))).ToArray()
        };
    }

    public void SetBuildingModel(string key, GameObject model)
    {
        string path = model == null ? "" : AssetDatabase.GetAssetPath(model);
        if (model != null) FactoryModelPipeline.RequireFbx(path);
        SetBuildingVisual(key, path);
    }

    private void SetBuildingVisual(string key, string path) =>
        EditBuilding(key, "设置建筑模型", b => { b.visualPrefab = string.IsNullOrEmpty(path) ? null : FactoryDatabaseEditor.LoadModel(path); b.visualPrefabPath = path; return b; });

    public void SetBuildingColliders(string key, FactoryColliderProxy[] shapes) =>
        EditBuilding(key, "修改建筑碰撞体", b => { b.colliders = shapes ?? Array.Empty<FactoryColliderProxy>(); return b; });

    private void PutBuildingImport(FactoryBuildingImportData data)
    {
        buildingImports.RemoveAll(i => i.buildingKey == data.buildingKey);
        buildingImports.Add(new FactoryBuildingImportData { buildingKey = data.buildingKey, modelFile = data.modelFile, sourceJsonPath = data.sourceJsonPath });
    }
}
