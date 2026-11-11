using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed partial class FactoryContentDraft
{
    public int ImportItemModel(string key, string source)
    {
        FactoryDatabaseEditor.EnsureCanWrite();
        RequireKey(key);
        var existing = GetItems().SingleOrDefault(i => i.key == key);
        var previous = existing == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(existing.modelPath);
        var model = FactoryModelPipeline.Import(key, source, previous, FactoryModelKind.Item);
        string path = FactoryItemAssetBindings.ModelPath(model);
        int row = -1;
        ApplyModelImport(staged =>
        {
            row = existing?.row ?? staged.AddItem();
            if (existing == null) staged.SetItemValue(row, "key", key);
            if (existing == null) staged.SetItemValue(row, "name_key", key);
            staged.SetItemValue(row, "model_path", path);
        });
        return row;
    }

    public string ImportBuildingModel(FactoryBuildingJson json, string source, BuildingKind kind, string machineType)
    {
        FactoryDatabaseEditor.EnsureCanWrite();
        if (!json.HasFootprint || !json.HasPorts || !json.HasColliders)
            throw new InvalidDataException("建筑模式需要配对 JSON 同时包含占格、ports 和 colliders；空数组表示该部分为空。");
        string key = json.Building.key;
        bool isNew = !GetBuildings().Any(b => b.key == key);
        if (isNew && !Enum.IsDefined(typeof(BuildingKind), kind)) throw new InvalidDataException("请选择建筑行为。");
        if (isNew && kind == BuildingKind.Processor && !GetMachineTypes().Contains(machineType))
            throw new InvalidDataException("请选择加工类型。");
        var previous = GetBuildingImport(key).model;
        var model = FactoryModelPipeline.Import(key, source, previous);
        FactoryDatabaseEditor.LoadModel(AssetDatabase.GetAssetPath(model));
        ApplyModelImport(staged =>
        {
            // Set behavior first so processor output-slot bindings retain their semantics.
            if (isNew)
            {
                staged.AddBuilding(key);
                staged.SetBuildingBehavior(key, kind);
                if (kind == BuildingKind.Processor) staged.SetBuildingStat(key, "processor_stats", "machine_type_key", machineType);
            }
            staged.ImportBuildingJson(json, true, true, true);
            staged.SetBuildingModel(key, model);
        });
        return key;
    }

    private void ApplyModelImport(Action<FactoryContentDraft> edit)
    {
        // Stage the entire content operation before publishing a single undo step.
        var staged = Instantiate(this);
        try
        {
            edit(staged);
            Undo.IncrementCurrentGroup();
            Undo.RegisterCompleteObjectUndo(this, "导入模型到内容草稿");
            content = JsonUtility.FromJson<Content>(JsonUtility.ToJson(staged.content));
            buildingImports = staged.buildingImports;
        }
        finally { Undo.ClearUndo(staged); DestroyImmediate(staged); }
    }
}
