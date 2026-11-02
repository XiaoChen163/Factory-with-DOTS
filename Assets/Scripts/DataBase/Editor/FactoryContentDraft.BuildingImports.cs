using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed partial class FactoryContentDraft
{
    public const string BuildingImportDirectory = "Assets/Settings/BuildingImports";
    [Serializable]
    private sealed class ImportDraft
    {
        public string path, original, text;
    }
    [SerializeField] private List<ImportDraft> buildingImports = new List<ImportDraft>();
    private bool ImportsModified => buildingImports.Any(i => i.original != i.text);

    public static string BuildingImportPath(string key) => BuildingImportDirectory + "/building-" + Uri.EscapeDataString(key) + ".asset";

    private void LoadBuildingImports()
    {
        var loaded = new List<ImportDraft>();
        if (AssetDatabase.IsValidFolder(BuildingImportDirectory))
            foreach (string guid in AssetDatabase.FindAssets("t:FactoryBuildingImportSettings", new[] { BuildingImportDirectory }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<FactoryBuildingImportSettings>(path);
                if (loaded.Any(i => JsonUtility.FromJson<FactoryBuildingImportData>(i.text).buildingKey == asset.data.buildingKey))
                    throw new InvalidDataException($"建筑导入配置 Key 重复：{asset.data.buildingKey}");
                string json = JsonUtility.ToJson(asset.data);
                loaded.Add(new ImportDraft { path = path, original = json, text = json });
            }
        buildingImports = loaded;
    }

    public FactoryBuildingImportData GetBuildingImport(string key)
    {
        var value = buildingImports.FirstOrDefault(i => JsonUtility.FromJson<FactoryBuildingImportData>(i.text).buildingKey == key);
        return value == null ? new FactoryBuildingImportData { buildingKey = key } : JsonUtility.FromJson<FactoryBuildingImportData>(value.text);
    }

    private void PutBuildingImport(FactoryBuildingImportData data)
    {
        var current = buildingImports.FirstOrDefault(i => JsonUtility.FromJson<FactoryBuildingImportData>(i.text).buildingKey == data.buildingKey);
        string json = JsonUtility.ToJson(data);
        if (current == null) buildingImports.Add(new ImportDraft { path = BuildingImportPath(data.buildingKey), original = "", text = json });
        else current.text = json;
    }

    private void ValidateBuildingImports()
    {
        foreach (var entry in buildingImports)
        {
            var current = AssetDatabase.LoadAssetAtPath<FactoryBuildingImportSettings>(entry.path);
            if ((current == null ? "" : JsonUtility.ToJson(current.data)) != entry.original)
                throw new IOException($"建筑导入配置已被外部修改，请重新读取：{entry.path}");
            foreach (var collider in JsonUtility.FromJson<FactoryBuildingImportData>(entry.text).colliders ?? Array.Empty<FactoryColliderProxy>())
                FactoryBuildingJson.ValidateCollider(collider);
        }
    }

    // This joins the existing CSV save transaction. Existing asset GUIDs stay stable;
    // new configs are deleted if the same save fails before generation completes.
    private void SaveBuildingImports(Action generate)
    {
        var changed = buildingImports.Where(i => i.original != i.text).ToArray();
        var applied = new List<(ImportDraft entry, FactoryBuildingImportSettings asset, bool created)>();
        string currentPath = BuildingImportDirectory;
        try
        {
            foreach (var entry in changed)
            {
                currentPath = entry.path;
                if (!AssetDatabase.IsValidFolder(BuildingImportDirectory))
                    AssetDatabase.CreateFolder("Assets/Settings", "BuildingImports");
                var asset = AssetDatabase.LoadAssetAtPath<FactoryBuildingImportSettings>(entry.path);
                bool created = asset == null;
                if (created && File.Exists(entry.path)) throw new IOException($"导入配置路径已被其他资产占用：{entry.path}");
                if (!created)
                    using (new FileStream(entry.path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { }
                if (created) asset = CreateInstance<FactoryBuildingImportSettings>();
                applied.Add((entry, asset, created));
                asset.data = JsonUtility.FromJson<FactoryBuildingImportData>(entry.text);
                if (created) AssetDatabase.CreateAsset(asset, entry.path);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
                if (!File.Exists(entry.path) || EditorUtility.IsDirty(asset)) throw new IOException($"无法保存建筑导入配置：{entry.path}");
            }
            generate();
        }
        catch (Exception error)
        {
            var failed = new List<string>();
            foreach (var item in applied.AsEnumerable().Reverse())
            {
                try
                {
                    if (item.created)
                    {
                        if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(item.asset))) AssetDatabase.DeleteAsset(item.entry.path);
                        else DestroyImmediate(item.asset);
                    }
                    else
                    {
                        item.asset.data = JsonUtility.FromJson<FactoryBuildingImportData>(item.entry.original);
                        EditorUtility.SetDirty(item.asset);
                        AssetDatabase.SaveAssetIfDirty(item.asset);
                    }
                }
                catch (Exception e) { failed.Add(item.entry.path + ": " + e.Message); }
            }
            throw new IOException("建筑导入配置保存失败：" + currentPath +
                (failed.Count == 0 ? "；已恢复本次配置修改。" : "；恢复失败：" + string.Join("; ", failed)), error);
        }
    }
}
