using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed partial class FactoryContentDraft
{
    public const string CollidersPath = "buildings/building_colliders.csv";
    // Pairing hints live only in the editor draft; model and collision data live in CSV.
    [SerializeField] private List<FactoryBuildingImportData> buildingImports = new List<FactoryBuildingImportData>();
    private void LoadBuildingImports() => buildingImports.Clear();

    public FactoryBuildingImportData GetBuildingImport(string key)
    {
        var hint = buildingImports.FirstOrDefault(i => i.buildingKey == key);
        var building = GetBuildings().SingleOrDefault(b => b.key == key);
        var shapes = FactoryBuildingColliderCsv.Read(ReadTable(CollidersPath));
        return new FactoryBuildingImportData
        {
            buildingKey = key, modelFile = hint?.modelFile, sourceJsonPath = hint?.sourceJsonPath,
            model = building == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(building.modelPath),
            colliders = shapes.TryGetValue(key, out var colliders) ? colliders : Array.Empty<FactoryColliderProxy>()
        };
    }

    public void SetBuildingModel(string key, GameObject model)
    {
        string path = model == null ? "" : AssetDatabase.GetAssetPath(model);
        if (model != null) FactoryBuildingModelPipeline.RequireFbx(path);
        SetBuildingVisual(key, path);
    }

    // Existing procedural assets remain usable until their FBX replacements are available.
    public void SetBuildingPrefab(string key, GameObject prefab)
    {
        string path = prefab == null ? "" : AssetDatabase.GetAssetPath(prefab);
        if (prefab != null && !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            throw new System.IO.InvalidDataException("请选择项目内的 .prefab 资产。");
        SetBuildingVisual(key, path);
    }

    private void SetBuildingVisual(string key, string path) =>
        Edit("设置建筑模型", docs => docs[BuildingsPath].Set(Find(docs[BuildingsPath], key), "model_path", path), BuildingsPath);

    public void SetBuildingColliders(string key, FactoryColliderProxy[] shapes) =>
        Edit("修改建筑碰撞体", docs => FactoryBuildingColliderCsv.Write(docs[CollidersPath], key, shapes), CollidersPath);

    private void PutBuildingImport(FactoryBuildingImportData data)
    {
        buildingImports.RemoveAll(i => i.buildingKey == data.buildingKey);
        buildingImports.Add(new FactoryBuildingImportData { buildingKey = data.buildingKey, modelFile = data.modelFile, sourceJsonPath = data.sourceJsonPath });
    }
}
