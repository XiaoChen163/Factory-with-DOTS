using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// The asset is the source of truth. No external table import may overwrite it.
public static class FactoryDatabaseEditor
{
    // The asset was moved with its .meta; scene and authoring references keep the same GUID.
    public const string DatabaseAssetPath = "Assets/Data/FactoryDatabase.asset";
    public const string ItemIconDirectory = "Assets/Art/Icons/Items";

    public static void EnsureCanWrite()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Play Mode 中不能写入 Factory 内容。");
    }

    public static FactoryDatabaseAsset Load()
    {
        var asset = AssetDatabase.LoadAssetAtPath<FactoryDatabaseAsset>(DatabaseAssetPath);
        if (asset == null) throw new InvalidDataException("Factory 数据资产不存在：" + DatabaseAssetPath);
        return asset;
    }

    // Caller owns this isolated candidate.
    public static FactoryDatabaseAsset ReadCandidate()
    {
        var candidate = UnityEngine.Object.Instantiate(Load());
        try { Validate(candidate); return candidate; }
        catch { UnityEngine.Object.DestroyImmediate(candidate); throw; }
    }

    [MenuItem("Factory/Validate Static Database")]
    public static void ValidateFromMenu()
    {
        Validate(Load());
        Debug.Log("Factory 数据资产校验通过。", Load());
    }

    public static void Validate(FactoryDatabaseAsset candidate)
    {
        FactoryDatabaseValidation.Validate(candidate);
        foreach (var item in candidate.items)
        {
            if (item.model == null) throw new InvalidDataException($"物品 '{item.key}' 缺少 FBX。");
            LoadModel(AssetDatabase.GetAssetPath(item.model));
        }
        foreach (var building in candidate.buildings)
        {
            if (string.IsNullOrWhiteSpace(building.displayName)) throw new InvalidDataException($"建筑 '{building.key}' 缺少名称。");
            LoadModel(AssetDatabase.GetAssetPath(building.visualPrefab));
            ValidateBuildingVisualPrefab(building.visualPrefab, building.key, building.kind);
        }
    }

    public static void Save(FactoryDatabaseAsset candidate)
    {
        EnsureCanWrite();
        Validate(candidate);
        var asset = Load();
        var previous = UnityEngine.Object.Instantiate(asset);
        byte[] disk = File.ReadAllBytes(DatabaseAssetPath);
        try
        {
            // Fail before mutating the loaded object if another process holds a write lock.
            using (new FileStream(DatabaseAssetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) { }
            string before = EditorJsonUtility.ToJson(asset);
            CopyData(candidate, asset);
            if (EditorJsonUtility.ToJson(asset) == before) return;
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            if (EditorUtility.IsDirty(asset)) throw new IOException("数据资产未能保存：" + DatabaseAssetPath);
        }
        catch
        {
            CopyData(previous, asset);
            EditorUtility.ClearDirty(asset);
            if (!File.ReadAllBytes(DatabaseAssetPath).SequenceEqual(disk)) File.WriteAllBytes(DatabaseAssetPath, disk);
            throw;
        }
        finally { UnityEngine.Object.DestroyImmediate(previous); }
    }

    public static void CopyData(FactoryDatabaseAsset source, FactoryDatabaseAsset target)
    {
        target.version = source.version;
        target.items = source.items;
        target.machineTypes = source.machineTypes;
        target.buildings = source.buildings;
        target.recipes = source.recipes;
    }

    public static string NormalizeKey(string value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty : string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    public static GameObject LoadModel(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("模型必须是项目内的 FBX：" + path);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null || model.GetComponentInChildren<MeshRenderer>(true) == null)
            throw new InvalidDataException("FBX 未找到或缺少静态网格：" + path);
        foreach (var component in model.GetComponentsInChildren<Component>(true))
            if (!(component is Transform || component is MeshFilter || component is MeshRenderer))
                throw new InvalidDataException($"模型 '{path}' 包含不支持的组件：{component?.GetType().Name}。仅允许网格表现，不允许碰撞体、刚体或脚本。");
        return model;
    }

    private static void ValidateBuildingVisualPrefab(
        GameObject prefab,
        string prefabKey,
        BuildingKind kind)
    {
        if (prefab.GetComponentInChildren<MeshRenderer>(true) == null)
            throw new InvalidDataException($"Building visual prefab '{prefabKey}' has no MeshRenderer.");
        if (kind == BuildingKind.Belt)
            foreach (string part in new[] { "Connection Edge East", "Connection Edge North", "Connection Edge West", "Connection Edge South", "Direction Triangle" })
            {
                var child = FactoryBuildingVisualUtility.FindPart(prefab, part);
                if (child == null || (part == "Direction Triangle" ? child.GetComponentInChildren<MeshRenderer>(true) : child.GetComponent<MeshRenderer>()) == null)
                    throw new InvalidDataException($"Belt model '{prefabKey}' requires a unique MeshRenderer child '{part}'.");
            }
        foreach (Component component in prefab.GetComponentsInChildren<Component>(true))
        {
            if (component is Transform || component is MeshFilter || component is MeshRenderer ||
                kind == BuildingKind.RampFoundation && component is MeshCollider)
                continue;
            throw new InvalidDataException(
                $"Building visual prefab '{prefabKey}' contains non-rendering component " +
                $"'{component.GetType().Name}'. Only Transform, MeshFilter and MeshRenderer are allowed.");
        }
    }

    public static Dictionary<string, Sprite> BuildSpriteIndex(string directory)
    {
        Dictionary<string, Sprite> result =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);
        if (!AssetDatabase.IsValidFolder(directory))
            return result;
        foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { directory }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                continue;
            string key = NormalizeKey(Path.GetFileNameWithoutExtension(path));
            if (string.IsNullOrEmpty(key) || result.ContainsKey(key))
                throw new InvalidDataException($"Duplicate or invalid icon name at '{path}'.");
            result.Add(key, sprite);
        }
        return result;
    }

}

public sealed class FactoryDatabaseBuildValidation : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report) => FactoryDatabaseEditor.Validate(FactoryDatabaseEditor.Load());
}
