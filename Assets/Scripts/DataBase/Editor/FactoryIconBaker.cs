using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class FactoryIconBaker
{
    public const string ItemPrefabDirectory = "Assets/Prefabs/Items";
    public const string BuildingPrefabDirectory = "Assets/Prefabs/Buildings";
    public const string ItemIconDirectory = "Assets/Art/Icons/Items";
    public const string BuildingIconDirectory = "Assets/Art/Icons/Buildings";
    public const string SettingsAssetPath = "Assets/Settings/FactoryIconBakeSettings.asset";
    public const string ManifestAssetPath =
        "Assets/Data/Generated/FactoryIconBakeManifest.asset";

    private const string BakerVersion = "1";
    public static bool IsBaking { get; private set; }

    [MenuItem("Factory/Icons/Bake All")]
    public static void BakeAllFromMenu() => BakeAll(true);

    [MenuItem("Factory/Icons/Bake Changed")]
    public static void BakeChangedFromMenu() => BakeAll(false);

    [MenuItem("Factory/Icons/Create or Select Settings")]
    public static void SelectSettings()
    {
        FactoryIconBakeSettings settings = LoadOrCreateSettings();
        Selection.activeObject = settings;
        EditorGUIUtility.PingObject(settings);
    }

    public static void BakeAll(bool force)
    {
        if (IsBaking || EditorApplication.isCompiling)
            return;

        IsBaking = true;
        try
        {
            EnsureAssetFolder(ItemIconDirectory);
            EnsureAssetFolder(BuildingIconDirectory);

            FactoryIconBakeSettings settings = LoadOrCreateSettings();
            FactoryIconBakeManifest manifest = LoadOrCreateManifest();
            Dictionary<string, FactoryIconBakeManifestEntry> previous =
                manifest.entries.ToDictionary(value => value.prefabPath, StringComparer.Ordinal);
            List<FactoryIconBakeManifestEntry> next = new List<FactoryIconBakeManifestEntry>();
            List<string> changedOutputs = new List<string>();

            BakeDirectory(
                ItemPrefabDirectory,
                ItemIconDirectory,
                settings,
                previous,
                next,
                changedOutputs,
                force);
            BakeDirectory(
                BuildingPrefabDirectory,
                BuildingIconDirectory,
                settings,
                previous,
                next,
                changedOutputs,
                force);

            manifest.entries = next.OrderBy(value => value.prefabPath, StringComparer.Ordinal)
                .ToArray();
            EditorUtility.SetDirty(manifest);

            if (changedOutputs.Count > 0)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                foreach (string outputPath in changedOutputs)
                    ConfigureTextureImporter(outputPath);
            }

            AssetDatabase.SaveAssets();
            FactoryDatabaseCsvImporter.RebuildFromMenu();
            Debug.Log(
                $"Factory icon bake complete: {changedOutputs.Count} updated, " +
                $"{next.Count - changedOutputs.Count} unchanged.",
                settings);
        }
        catch (Exception exception)
        {
            Debug.LogError("Failed to bake factory icons:\n" + exception);
        }
        finally
        {
            IsBaking = false;
        }
    }

    private static void BakeDirectory(
        string prefabDirectory,
        string outputDirectory,
        FactoryIconBakeSettings settings,
        IReadOnlyDictionary<string, FactoryIconBakeManifestEntry> previous,
        ICollection<FactoryIconBakeManifestEntry> next,
        ICollection<string> changedOutputs,
        bool force)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { prefabDirectory }))
        {
            string prefabPath = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                continue;

            string outputPath = outputDirectory + "/" +
                                Path.GetFileNameWithoutExtension(prefabPath) + ".png";
            string hash = CalculateBakeHash(prefabPath, settings);
            bool current = previous.TryGetValue(prefabPath, out FactoryIconBakeManifestEntry entry) &&
                           entry.hash == hash &&
                           entry.outputPath == outputPath &&
                           File.Exists(ToAbsolutePath(outputPath));

            if (force || !current)
            {
                byte[] png = FactoryIconRenderer.RenderPng(prefab, settings);
                File.WriteAllBytes(ToAbsolutePath(outputPath), png);
                changedOutputs.Add(outputPath);
            }

            next.Add(new FactoryIconBakeManifestEntry
            {
                prefabPath = prefabPath,
                outputPath = outputPath,
                hash = hash
            });
        }
    }

    private static string CalculateBakeHash(
        string prefabPath,
        FactoryIconBakeSettings settings)
    {
        string settingsJson = EditorJsonUtility.ToJson(settings);
        string dependencyHash = AssetDatabase.GetAssetDependencyHash(prefabPath).ToString();
        return Hash128.Compute(BakerVersion + dependencyHash + settingsJson).ToString();
    }

    private static void ConfigureTextureImporter(string outputPath)
    {
        if (AssetImporter.GetAtPath(outputPath) is not TextureImporter importer)
            throw new InvalidOperationException($"Could not import baked icon '{outputPath}'.");

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.sRGBTexture = true;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }

    private static FactoryIconBakeSettings LoadOrCreateSettings()
    {
        FactoryIconBakeSettings settings =
            AssetDatabase.LoadAssetAtPath<FactoryIconBakeSettings>(SettingsAssetPath);
        if (settings != null)
            return settings;

        EnsureAssetFolder(Path.GetDirectoryName(SettingsAssetPath)?.Replace('\\', '/'));
        settings = ScriptableObject.CreateInstance<FactoryIconBakeSettings>();
        AssetDatabase.CreateAsset(settings, SettingsAssetPath);
        AssetDatabase.SaveAssets();
        return settings;
    }

    private static FactoryIconBakeManifest LoadOrCreateManifest()
    {
        FactoryIconBakeManifest manifest =
            AssetDatabase.LoadAssetAtPath<FactoryIconBakeManifest>(ManifestAssetPath);
        if (manifest != null)
            return manifest;

        EnsureAssetFolder(Path.GetDirectoryName(ManifestAssetPath)?.Replace('\\', '/'));
        manifest = ScriptableObject.CreateInstance<FactoryIconBakeManifest>();
        AssetDatabase.CreateAsset(manifest, ManifestAssetPath);
        return manifest;
    }

    private static void EnsureAssetFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || AssetDatabase.IsValidFolder(path))
            return;

        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string name = Path.GetFileName(path);
        EnsureAssetFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static string ToAbsolutePath(string assetPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ??
                             throw new InvalidOperationException("Project root is unavailable.");
        return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }
}
