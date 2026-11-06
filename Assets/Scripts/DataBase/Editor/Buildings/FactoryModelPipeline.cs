using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public enum FactoryModelKind { Building, Item }

public static class FactoryModelPipeline
{
    public const string ArtDirectory = "Assets/Art/Buildings";
    public const string ItemArtDirectory = "Assets/Art/Items";
    public const string MaterialDirectory = "Assets/Art/Materials";

    public static void RequireFbx(string path)
    {
        if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new InvalidDataException("请选择可读取的 FBX 文件。");
    }

    public static string Pair(FactoryBuildingImportData data)
    {
        if (string.IsNullOrEmpty(data.sourceJsonPath) || string.IsNullOrEmpty(data.modelFile)) return null;
        string path = Path.Combine(Path.GetDirectoryName(data.sourceJsonPath) ?? "", Path.GetFileName(data.modelFile));
        return File.Exists(path) ? path : null;
    }

    public static GameObject Import(string key, string source, GameObject previousModel = null, FactoryModelKind kind = FactoryModelKind.Building)
    {
        FactoryDatabaseCsvImporter.EnsureCanWrite();
        RequireFbx(source);
        string full = Path.GetFullPath(source), assets = Path.GetFullPath("Assets") + Path.DirectorySeparatorChar;
        string path;
        if (full.StartsWith(assets, StringComparison.OrdinalIgnoreCase))
            path = "Assets/" + full.Substring(assets.Length).Replace('\\', '/');
        else
        {
            string directory = (kind == FactoryModelKind.Item ? ItemArtDirectory : ArtDirectory) + "/" + Uri.EscapeDataString(key);
            Directory.CreateDirectory(directory);
            string previousPath = AssetDatabase.GetAssetPath(previousModel);
            path = previousPath.StartsWith(directory + "/", StringComparison.Ordinal) && previousPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)
                ? previousPath : directory + "/" + Path.GetFileName(source);
            File.Copy(full, path, true); // Existing .meta stays in place.
            string sourceDir = Path.GetDirectoryName(full);
            foreach (string texture in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories)
                .Where(p => new[] { ".png", ".jpg", ".jpeg", ".tga", ".tif", ".tiff", ".psd", ".exr", ".bmp" }.Contains(Path.GetExtension(p).ToLowerInvariant())))
            {
                string target = directory + "/" + Path.GetRelativePath(sourceDir, texture).Replace('\\', '/');
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(texture, target, true);
            }
            AssetDatabase.Refresh();
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        Configure(path);
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    public static void Configure(string path)
    {
        RequireFbx(path);
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.preserveHierarchy = true;
        importer.globalScale = 1;
        importer.useFileScale = true;
        importer.bakeAxisConversion = true;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.importCameras = false;
        importer.importLights = false;
        importer.addCollider = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
        importer.SaveAndReimport();
        bool changed = false;
        var mapped = importer.GetExternalObjectMap();
        foreach (var source in Sources(path))
        {
            if (mapped.TryGetValue(source, out var value) && value != null) continue;
            var matches = Matches(source.name);
            if (matches.Length != 1) continue;
            importer.AddRemap(source, matches[0]);
            changed = true;
        }
        if (changed) importer.SaveAndReimport();
    }

    public static AssetImporter.SourceAssetIdentifier[] Sources(string path)
    {
        // Source names, including remapped slots, remain stable across import order changes.
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        var serialized = new SerializedObject(importer);
        var materials = serialized.FindProperty("m_Materials");
        return Enumerable.Range(0, materials.arraySize).Select(i =>
            new AssetImporter.SourceAssetIdentifier(typeof(Material), materials.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue)).ToArray();
    }

    public static Material[] Matches(string name)
    {
        if (!AssetDatabase.IsValidFolder(MaterialDirectory)) return Array.Empty<Material>();
        return AssetDatabase.FindAssets("t:Material", new[] { MaterialDirectory }).Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<Material>)
            .Where(m => m != null && (m.name == name || m.name == Uri.EscapeDataString(name))).ToArray();
    }

    public static void Remap(string path, AssetImporter.SourceAssetIdentifier source, Material material)
    {
        FactoryDatabaseCsvImporter.EnsureCanWrite();
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        if (material == null) importer.RemoveRemap(source); else importer.AddRemap(source, material);
        importer.SaveAndReimport();
    }

    public static Material CreateMaterial(string path, AssetImporter.SourceAssetIdentifier source)
    {
        FactoryDatabaseCsvImporter.EnsureCanWrite();
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        if (importer.GetExternalObjectMap().TryGetValue(source, out var mapped) && mapped is Material existing) return existing;
        var matches = Matches(source.name);
        if (matches.Length > 1) throw new InvalidDataException("存在多个同名材质，请手动选择：" + source.name);
        if (matches.Length == 1) { Remap(path, source, matches[0]); return matches[0]; }
        var original = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().FirstOrDefault(m => m.name == source.name);
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) throw new InvalidDataException("未找到 URP/Lit Shader。");
        var result = new Material(shader) { name = source.name };
        if (original != null)
        {
            if (original.HasProperty("_Color")) result.SetColor("_BaseColor", original.GetColor("_Color"));
            if (original.HasProperty("_MainTex")) result.SetTexture("_BaseMap", original.GetTexture("_MainTex"));
            if (original.HasProperty("_BumpMap")) { result.SetTexture("_BumpMap", original.GetTexture("_BumpMap")); if (result.GetTexture("_BumpMap") != null) result.EnableKeyword("_NORMALMAP"); }
            foreach (string property in new[] { "_MetallicGlossMap", "_EmissionMap" })
                if (original.HasProperty(property)) result.SetTexture(property, original.GetTexture(property));
            if (original.HasProperty("_Metallic")) result.SetFloat("_Metallic", original.GetFloat("_Metallic"));
            if (original.HasProperty("_Glossiness")) result.SetFloat("_Smoothness", original.GetFloat("_Glossiness"));
            if (original.HasProperty("_EmissionColor")) result.SetColor("_EmissionColor", original.GetColor("_EmissionColor"));
            if (result.GetTexture("_MetallicGlossMap") != null) result.EnableKeyword("_METALLICSPECGLOSSMAP");
            if (result.GetTexture("_EmissionMap") != null || result.GetColor("_EmissionColor").maxColorComponent > 0) result.EnableKeyword("_EMISSION");
        }
        string materialDirectory = MaterialDirectory + (path.StartsWith(ItemArtDirectory + "/", StringComparison.Ordinal) ? "/Items" : "/Buildings");
        Directory.CreateDirectory(materialDirectory);
        string output = AssetDatabase.GenerateUniqueAssetPath(materialDirectory + "/" + Uri.EscapeDataString(source.name) + ".mat");
        AssetDatabase.CreateAsset(result, output);
        Remap(path, source, result);
        return result;
    }
}
