using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class FactoryContentManagerWindow
{
    private void DrawBuildingModel(FactoryContentBuilding building, FactoryBuildingImportData data)
    {
        string key = building.key;
        var modelField = new ObjectField("FBX 模型") { name = "building-model", objectType = typeof(GameObject), allowSceneObjects = false, value = data.model };
        modelField.RegisterValueChangedCallback(e =>
        {
            if (Run(() => { if (e.newValue != null) FactoryBuildingModelPipeline.Configure(AssetDatabase.GetAssetPath(e.newValue)); draft.SetBuildingModel(key, e.newValue as GameObject); }))
            { DrawDetail(); UpdateModified(); }
            else modelField.SetValueWithoutNotify(e.previousValue);
        });
        detail.Add(modelField);
        string pair = FactoryBuildingModelPipeline.Pair(data);
        detail.Add(new Label(pair == null ? "未找到 JSON 配对 FBX，可手动选择。" : "配对 FBX：" + pair));
        var import = new Button(() => BuildingAction(() =>
        {
            string path = pair ?? EditorUtility.OpenFilePanel("导入建筑 FBX（连同同目录纹理）", "", "fbx");
            if (!string.IsNullOrEmpty(path)) draft.SetBuildingModel(key, FactoryBuildingModelPipeline.Import(key, path, data.model));
        })) { text = pair == null ? "从外部导入 FBX…" : "导入配对 FBX", name = "building-import-fbx" };
        detail.Add(import);
        if (pair != null) detail.Add(new Button(() => BuildingAction(() =>
        {
            string path = EditorUtility.OpenFilePanel("手动选择建筑 FBX", "", "fbx");
            if (!string.IsNullOrEmpty(path)) draft.SetBuildingModel(key, FactoryBuildingModelPipeline.Import(key, path, data.model));
        })) { text = "选择其他 FBX…" });
        var materials = new Button(() => FactoryBuildingMaterialWindow.Open(data.model)) { text = "材质映射与 URP 适配", name = "building-materials" };
        materials.SetEnabled(data.model != null); detail.Add(materials);
        var generate = new Button(() => BuildingAction(() => draft.GenerateBuildingPrefab(key))) { text = "生成 / 更新 Prefab", name = "building-generate-prefab" };
        generate.SetEnabled(data.model != null); detail.Add(generate);
    }
}
