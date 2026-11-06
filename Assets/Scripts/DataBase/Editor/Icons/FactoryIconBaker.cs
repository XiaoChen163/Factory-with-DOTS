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

    private const string BakerVersion = "3";
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
            BakeBuildings(settings, previous, next, changedOutputs, force);

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

    public static string BuildingManifestKey(string key) => "building:" + key;

    private static void BakeBuildings(FactoryIconBakeSettings settings,
        IReadOnlyDictionary<string, FactoryIconBakeManifestEntry> previous,
        ICollection<FactoryIconBakeManifestEntry> next, ICollection<string> changedOutputs, bool force)
    {
        var table = FactoryCsvTable.Read("Assets/Data/FactoryTables/buildings/buildings.csv");
        for (int row = 0; row < table.RowCount; row++)
        {
            string key = table.Get(row, "key"), path = table.Get(row, "model_path");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) throw new InvalidDataException($"Building '{key}' model not found: {path}");
            string identity = BuildingManifestKey(key);
            settings.GetIconSource(model, out GameObject source, out Sprite supplied);
            string output = BuildingIconDirectory + "/" + Uri.EscapeDataString(key) + ".png";
            // Keep existing generated paths stable; fresh definitions use their own Key.
            if (previous.TryGetValue(identity, out var old) && old.outputPath.StartsWith(BuildingIconDirectory + "/", StringComparison.Ordinal)) output = old.outputPath;
            if (supplied != null) output = AssetDatabase.GetAssetPath(supplied);
            string hash = CalculateBakeHash(path, settings);
            bool current = previous.TryGetValue(identity, out var entry) && entry.hash == hash && entry.outputPath == output && File.Exists(ToAbsolutePath(output));
            if (supplied == null && (force || !current))
            {
                File.WriteAllBytes(ToAbsolutePath(output), FactoryIconRenderer.RenderPng(source, settings, model));
                changedOutputs.Add(output);
            }
            next.Add(new FactoryIconBakeManifestEntry { prefabPath = identity, outputPath = output, hash = hash });
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

            settings.GetIconSource(prefab, out GameObject iconModel, out Sprite suppliedIcon);
            if (suppliedIcon != null)
            {
                // Supplied art belongs to its author, not the generated PNG directory.
                next.Add(new FactoryIconBakeManifestEntry
                {
                    prefabPath = prefabPath,
                    outputPath = AssetDatabase.GetAssetPath(suppliedIcon),
                    hash = CalculateBakeHash(prefabPath, settings)
                });
                continue;
            }
            string outputPath = outputDirectory + "/" +
                                Path.GetFileNameWithoutExtension(prefabPath) + ".png";
            string hash = CalculateBakeHash(prefabPath, settings);
            bool current = previous.TryGetValue(prefabPath, out FactoryIconBakeManifestEntry entry) &&
                           entry.hash == hash &&
                           entry.outputPath == outputPath &&
                           File.Exists(ToAbsolutePath(outputPath));

            if (force || !current)
            {
                byte[] png = FactoryIconRenderer.RenderPng(iconModel, settings, prefab);
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
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        settings.GetIconSource(prefab, out GameObject model, out Sprite sprite);
        string sourcePath = AssetDatabase.GetAssetPath(sprite != null ? (UnityEngine.Object)sprite : model);
        string sourceHash = string.IsNullOrEmpty(sourcePath) ? string.Empty :
            AssetDatabase.GetAssetDependencyHash(sourcePath).ToString();
        return Hash128.Compute(BakerVersion + dependencyHash + sourceHash + settingsJson).ToString();
    }

    public static Sprite GetSuppliedBuildingIcon(GameObject prefab)
    {
        FactoryIconBakeSettings settings = AssetDatabase.LoadAssetAtPath<FactoryIconBakeSettings>(SettingsAssetPath);
        if (settings == null) return null;
        settings.GetIconSource(prefab, out _, out Sprite sprite);
        return sprite;
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
