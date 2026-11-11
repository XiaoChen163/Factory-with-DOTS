using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class FactoryIconBaker
{
    public const string ItemIconDirectory = "Assets/Art/Icons/Items";
    public const string BuildingIconDirectory = "Assets/Art/Icons/Buildings";
    public const string SettingsAssetPath = "Assets/Settings/FactoryIconBakeSettings.asset";
    public const string ManifestAssetPath =
        "Assets/Data/Generated/FactoryIconBakeManifest.asset";

    private const string BakerVersion = "4";
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
        FactoryDatabaseEditor.EnsureCanWrite();
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

            BakeModels("items", ItemIconDirectory, settings, previous, next, changedOutputs, force);
            BakeModels("buildings", BuildingIconDirectory, settings, previous, next, changedOutputs, force);

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
            BindBakedIcons(previous, manifest);
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

    public static string ItemManifestKey(string key) => "item:" + key;
    public static string BuildingManifestKey(string key) => "building:" + key;

    private static void BakeModels(string kind, string outputDirectory, FactoryIconBakeSettings settings,
        IReadOnlyDictionary<string, FactoryIconBakeManifestEntry> previous,
        ICollection<FactoryIconBakeManifestEntry> next, ICollection<string> changedOutputs, bool force)
    {
        var database = FactoryDatabaseEditor.Load();
        var models = kind == "items"
            ? database.items.Select(i => (i.key, model: i.model))
            : database.buildings.Select(b => (b.key, model: b.visualPrefab));
        foreach (var definition in models)
        {
            string key = definition.key, path = AssetDatabase.GetAssetPath(definition.model);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null) throw new InvalidDataException($"Building '{key}' model not found: {path}");
            string identity = kind == "items" ? ItemManifestKey(key) : BuildingManifestKey(key);
            settings.GetIconSource(model, out GameObject source, out Sprite supplied);
            string output = outputDirectory + "/" + Uri.EscapeDataString(key) + ".png";
            // Keep existing generated paths stable; fresh definitions use their own Key.
            if (previous.TryGetValue(identity, out var old) && old.outputPath.StartsWith(outputDirectory + "/", StringComparison.Ordinal)) output = old.outputPath;
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

    private static void BindBakedIcons(IReadOnlyDictionary<string, FactoryIconBakeManifestEntry> previous, FactoryIconBakeManifest manifest)
    {
        var candidate = UnityEngine.Object.Instantiate(FactoryDatabaseEditor.Load());
        try
        {
            var entries = manifest.entries.ToDictionary(e => e.prefabPath, StringComparer.Ordinal);
            for (int n = 0; n < candidate.items.Length; n++)
            {
                var item = candidate.items[n];
                string identity = ItemManifestKey(item.key);
                if (entries.TryGetValue(identity, out var entry) && CanBind(item.icon, identity, previous, ItemIconDirectory, item.key))
                {
                    item.icon = AssetDatabase.LoadAssetAtPath<Sprite>(entry.outputPath);
                    item.iconKey = Path.GetFileNameWithoutExtension(entry.outputPath);
                    candidate.items[n] = item;
                }
            }
            for (int n = 0; n < candidate.buildings.Length; n++)
            {
                var building = candidate.buildings[n];
                string identity = BuildingManifestKey(building.key);
                if (entries.TryGetValue(identity, out var entry) && CanBind(building.icon, identity, previous, BuildingIconDirectory, building.key))
                {
                    building.icon = AssetDatabase.LoadAssetAtPath<Sprite>(entry.outputPath);
                    candidate.buildings[n] = building;
                }
            }
            FactoryDatabaseEditor.Save(candidate);
        }
        finally { UnityEngine.Object.DestroyImmediate(candidate); }
    }

    private static bool CanBind(Sprite current, string identity, IReadOnlyDictionary<string, FactoryIconBakeManifestEntry> previous, string directory, string key)
    {
        string path = AssetDatabase.GetAssetPath(current);
        return current == null || path == directory + "/" + Uri.EscapeDataString(key) + ".png" ||
            previous.TryGetValue(identity, out var old) && path == old.outputPath;
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
