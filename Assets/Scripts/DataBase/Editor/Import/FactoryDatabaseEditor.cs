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

    // Destroy the returned snapshot when finished. It owns cloned capability objects.
    public static FactoryDatabaseAsset ReadCandidate()
    {
        var candidate = CreateSnapshot(Load());
        try { Validate(candidate); return candidate; }
        catch { UnityEngine.Object.DestroyImmediate(candidate); throw; }
    }

    [Serializable]
    private sealed class Rows
    {
        public FactoryItemTableRow[] items;
        public FactoryRecipeTableRow[] recipes;
        public FactoryBuildingDefinitionRow[] buildings;
        public FactoryMachineTypeTableRow[] machineTypes;
    }
    public static void CopyData(FactoryDatabaseAsset source, FactoryDatabaseAsset target)
    {
        var rows = JsonUtility.FromJson<Rows>(JsonUtility.ToJson(new Rows
        { items = source.items, recipes = source.recipes, buildings = source.buildings, machineTypes = source.machineTypes }));
        target.version = source.version; target.items = rows.items; target.recipes = rows.recipes;
        target.buildings = rows.buildings; target.machineTypes = rows.machineTypes;
    }
    public static FactoryDatabaseAsset CreateSnapshot(FactoryDatabaseAsset source)
    {
        var snapshot = ScriptableObject.CreateInstance<FactoryDatabaseAsset>();
        CopyData(source, snapshot);
        var cloned = new Dictionary<ItemCapabilityDefinition, ItemCapabilityDefinition>();
        foreach (var item in snapshot.items)
            if (item.capabilities != null)
                for (int i = 0; i < item.capabilities.Length; i++)
                    if (item.capabilities[i] != null)
                    {
                        var original = item.capabilities[i];
                        if (!cloned.TryGetValue(original, out var copy)) cloned.Add(original, copy = UnityEngine.Object.Instantiate(original));
                        item.capabilities[i] = copy;
                    }
        snapshot.ownsInlineCapabilities = true;
        return snapshot;
    }

    [MenuItem("Factory/Validate Static Database")]
    public static void ValidateFromMenu()
    {
        Validate(Load());
        Debug.Log("Factory content libraries validated.", Load());
    }

    public static void Validate(FactoryDatabaseAsset candidate)
    {
        FactoryDatabaseValidation.Validate(candidate);
        if (EditorUtility.IsPersistent(candidate))
        {
            RequireLibraries(candidate);
            foreach (var item in candidate.items)
                foreach (var capability in item.capabilities ?? Array.Empty<ItemCapabilityDefinition>())
                    if (AssetDatabase.GetAssetPath(capability) != AssetDatabase.GetAssetPath(candidate.itemDatabase) || !AssetDatabase.IsSubAsset(capability))
                        throw new InvalidDataException("Item capabilities must be sub-assets of the referenced item database.");
        }
        foreach (var item in candidate.items)
        {
            if (item.model == null) throw new InvalidDataException($"Item '{item.key}' needs an FBX.");
            LoadModel(AssetDatabase.GetAssetPath(item.model));
            foreach (var presentation in (item.capabilities ?? Array.Empty<ItemCapabilityDefinition>()).OfType<WeaponPresentationCapabilityDefinition>())
            {
                foreach (var model in new[] { presentation.firstPersonPrefab, presentation.thirdPersonPrefab })
                    if (model != null && !EditorUtility.IsPersistent(model)) throw new InvalidDataException("Held models must reference project assets.");
                foreach (var controller in new[] { presentation.firstPersonAnimations, presentation.thirdPersonAnimations })
                    if (controller != null && !EditorUtility.IsPersistent(controller)) throw new InvalidDataException("Animation controllers must reference project assets.");
            }
        }
        foreach (var building in candidate.buildings)
        {
            if (string.IsNullOrWhiteSpace(building.displayName)) throw new InvalidDataException($"Building '{building.key}' needs a name.");
            LoadModel(AssetDatabase.GetAssetPath(building.visualPrefab));
            ValidateBuildingVisualPrefab(building.visualPrefab, building.key, building.kind);
        }
    }
    private static void RequireLibraries(FactoryDatabaseAsset source)
    {
        if (source.itemDatabase == null || source.recipeDatabase == null || source.buildingDatabase == null)
            throw new InvalidDataException("Factory database requires item, recipe and building libraries. Run the library migration first.");
    }
    public static UnityEngine.Object[] SourceObjects()
    {
        var source = Load();
        RequireLibraries(source);
        return new UnityEngine.Object[] { source, source.itemDatabase, source.recipeDatabase, source.buildingDatabase }
            .Concat(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(source.itemDatabase)).Where(o => o is ItemCapabilityDefinition)).ToArray();
    }
    public static string SourceJson() => string.Join("\n", SourceObjects().Select(o => EditorJsonUtility.ToJson(o)));
    public static string SourceBytes() => string.Join("\n", SourceObjects().Select(AssetDatabase.GetAssetPath).Distinct().Select(p => Convert.ToBase64String(File.ReadAllBytes(p))));

    public static void Save(FactoryDatabaseAsset candidate)
    {
        EnsureCanWrite();
        Validate(candidate);
        var asset = Load();
        RequireLibraries(asset);
        var roots = new UnityEngine.Object[] { asset, asset.itemDatabase, asset.recipeDatabase, asset.buildingDatabase };
        var disk = roots.ToDictionary(AssetDatabase.GetAssetPath, o => File.ReadAllBytes(AssetDatabase.GetAssetPath(o)), StringComparer.Ordinal);
        // Fail before any mutation if a source is locked or read-only.
        foreach (string path in disk.Keys)
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) { }
        var oldCapabilities = asset.items.SelectMany(i => (i.capabilities ?? Array.Empty<ItemCapabilityDefinition>())
            .Select(c => (item: i.id, capability: c))).Where(e => e.capability != null)
            .ToDictionary(e => (e.item, e.capability.Kind), e => e.capability);
        var created = new List<ItemCapabilityDefinition>();
        var retained = new HashSet<ItemCapabilityDefinition>();
        try
        {
            var rows = JsonUtility.FromJson<Rows>(JsonUtility.ToJson(new Rows
            { items = candidate.items, recipes = candidate.recipes, buildings = candidate.buildings, machineTypes = candidate.machineTypes }));
            foreach (var item in rows.items)
            {
                if (item.capabilities == null) continue;
                for (int i = 0; i < item.capabilities.Length; i++)
                {
                    var definition = item.capabilities[i];
                    if (!oldCapabilities.TryGetValue((item.id, definition.Kind), out var stored) || stored.GetType() != definition.GetType())
                    {
                        stored = ScriptableObject.CreateInstance(definition.GetType()) as ItemCapabilityDefinition;
                        AssetDatabase.AddObjectToAsset(stored, asset.itemDatabase);
                        created.Add(stored);
                    }
                    EditorUtility.CopySerialized(definition, stored);
                    stored.name = item.key + "." + definition.Kind;
                    EditorUtility.SetDirty(stored);
                    item.capabilities[i] = stored;
                    retained.Add(stored);
                }
            }
            asset.version = candidate.version;
            asset.items = rows.items; asset.recipes = rows.recipes; asset.buildings = rows.buildings; asset.machineTypes = rows.machineTypes;
            foreach (var orphan in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(asset.itemDatabase)).OfType<ItemCapabilityDefinition>().ToArray())
                if (!retained.Contains(orphan)) UnityEngine.Object.DestroyImmediate(orphan, true);
            foreach (var root in roots) EditorUtility.SetDirty(root);
            foreach (var root in roots)
            {
                AssetDatabase.SaveAssetIfDirty(root);
                if (EditorUtility.IsDirty(root)) throw new IOException("Could not save content asset: " + AssetDatabase.GetAssetPath(root));
            }
        }
        catch (Exception error)
        {
            try
            {
                foreach (var capability in created) if (capability != null) UnityEngine.Object.DestroyImmediate(capability, true);
                AssetDatabase.ReleaseCachedFileHandles();
                foreach (var entry in disk) File.WriteAllBytes(entry.Key, entry.Value);
                foreach (string path in disk.Keys.Reverse()) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }
            catch (Exception rollback) { throw new IOException("Content save failed and rollback failed: " + rollback.Message, error); }
            throw;
        }
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
