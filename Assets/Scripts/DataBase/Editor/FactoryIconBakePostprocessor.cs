using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

public sealed class FactoryIconBakePostprocessor : AssetPostprocessor
{
    private static readonly HashSet<string> RenderDependencyExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".prefab", ".fbx", ".obj", ".dae", ".blend",
            ".mat", ".shader", ".shadergraph", ".asset",
            ".png", ".jpg", ".jpeg", ".tga", ".psd", ".exr", ".hdr"
        };

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (FactoryIconBaker.IsBaking)
            return;

        IEnumerable<string> changed = importedAssets
            .Concat(deletedAssets)
            .Concat(movedAssets)
            .Concat(movedFromAssetPaths);
        if (changed.Any(IsPossibleRenderDependency))
            FactoryIconBaker.QueueIncrementalBake();
    }

    private static bool IsPossibleRenderDependency(string path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
            return false;
        if (path.StartsWith(FactoryIconBaker.ItemIconDirectory + "/", StringComparison.Ordinal) ||
            path.StartsWith(FactoryIconBaker.BuildingIconDirectory + "/", StringComparison.Ordinal) ||
            path.StartsWith("Assets/Data/Generated/", StringComparison.Ordinal) ||
            path == FactoryIconBaker.ManifestAssetPath)
        {
            return false;
        }

        if (path == FactoryIconBaker.SettingsAssetPath)
            return true;

        return RenderDependencyExtensions.Contains(Path.GetExtension(path));
    }
}
