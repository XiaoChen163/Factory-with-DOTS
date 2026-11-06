using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class FactoryContentManagerWindow
{
    private void DrawBuildingModel(FactoryContentBuilding building, FactoryBuildingImportData data)
    {
        string key = building.key;
        var modelField = new ObjectField("FBX 模型") { name = "building-model", objectType = typeof(GameObject), allowSceneObjects = false, value = data.model != null && AssetDatabase.GetAssetPath(data.model).EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) ? data.model : null };
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
        materials.SetEnabled(data.model != null && AssetDatabase.GetAssetPath(data.model).EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)); detail.Add(materials);
        if (data.model != null && !AssetDatabase.GetAssetPath(data.model).EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            detail.Add(new HelpBox("当前使用旧版程序生成模型。选择或导入 FBX 后直接替换引用。", HelpBoxMessageType.Info));
        detail.Add(new HelpBox("选择或导入 FBX 后，保存草稿即可用于建造，无需生成 Prefab。传送带需包含唯一命名的 Connection Edge East / North / West / South 和 Direction Triangle 网格子物体；允许分组嵌套。", HelpBoxMessageType.Info));
    }

    private void DrawBuildingColliders(string key)
    {
        detail.Add(Heading("碰撞体（building_colliders.csv）"));
        var shapes = draft.GetBuildingImport(key).colliders;
        detail.Add(new Label($"{shapes.Length} 个碰撞体 · 建筑局部坐标 · Unity 单位 · 胶囊 / 圆柱沿局部 Y 轴"));
        for (int i = 0; i < shapes.Length; i++)
        {
            int index = i;
            var shape = shapes[i];
            var foldout = new Foldout { text = shape.name + " · " + shape.type, value = false };
            detail.Add(foldout);
            void EditShape(Action<FactoryColliderProxy> edit)
            {
                var current = draft.GetBuildingImport(key).colliders;
                edit(current[index]);
                draft.SetBuildingColliders(key, current);
            }
            Bind(foldout, new TextField("碰撞体 Key") { value = shape.name, isDelayed = true }, value => EditShape(s => s.name = value));
            var type = new PopupField<string>("形状", new[] { "Box", "Sphere", "Capsule", "Cylinder" }.ToList(), shape.type);
            type.RegisterValueChangedCallback(e => BuildingAction(() => EditShape(s =>
            {
                s.type = e.newValue; s.axis = "Y";
                if (s.size.x <= 0 || s.size.y <= 0 || s.size.z <= 0) s.size = Vector3.one;
                if (s.radius <= 0) s.radius = .5f;
                if (s.height < s.radius * 2) s.height = s.radius * 2;
            })));
            foldout.Add(type);
            Bind(foldout, new Vector3Field("位置") { value = shape.position }, value => EditShape(s => s.position = value));
            Bind(foldout, new Vector3Field("旋转（角度）") { value = shape.rotation.eulerAngles }, value => EditShape(s => s.rotation = Quaternion.Euler(value)));
            if (shape.type == "Box") Bind(foldout, new Vector3Field("尺寸") { value = shape.size }, value => EditShape(s => s.size = value));
            else Bind(foldout, new FloatField("半径") { value = shape.radius, isDelayed = true }, value => EditShape(s => s.radius = value));
            if (shape.type == "Capsule" || shape.type == "Cylinder")
                Bind(foldout, new FloatField("总高度") { value = shape.height, isDelayed = true }, value => EditShape(s => s.height = value));
            foldout.Add(new Button(() => BuildingAction(() => draft.SetBuildingColliders(key,
                draft.GetBuildingImport(key).colliders.Where((s, n) => n != index).ToArray()))) { text = "删除碰撞体" });
        }
        detail.Add(new Button(() => BuildingAction(() =>
        {
            var current = draft.GetBuildingImport(key).colliders;
            int suffix = 1;
            while (current.Any(s => s.name == "collider_" + suffix)) suffix++;
            draft.SetBuildingColliders(key, current.Concat(new[] { new FactoryColliderProxy { name = "collider_" + suffix, type = "Box", size = Vector3.one } }).ToArray());
        })) { text = "新增碰撞体", name = "building-add-collider" });
    }
}
