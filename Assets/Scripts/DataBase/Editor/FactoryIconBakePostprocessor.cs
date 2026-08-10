using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public sealed class FactoryIconBakePostprocessor : AssetPostprocessor
{
    private const string AutoBakeMenuPath =
        "Factory/Icons/Auto Bake On Prefab Changes";
    private const string AutoBakePreferencePrefix =
        "Factory.IconBaker.AutoBakePrefabChanges.";

    private static bool bakeQueued;

    private static string AutoBakePreferenceKey =>
        AutoBakePreferencePrefix + Hash128.Compute(Application.dataPath);

    private static bool AutoBakeEnabled
    {
        get => EditorPrefs.GetBool(AutoBakePreferenceKey, false);
        set => EditorPrefs.SetBool(AutoBakePreferenceKey, value);
    }

    static FactoryIconBakePostprocessor()
    {
        EditorApplication.delayCall += UpdateMenuCheckmark;
    }

    [MenuItem(AutoBakeMenuPath)]
    private static void ToggleAutoBake()
    {
        AutoBakeEnabled = !AutoBakeEnabled;
        UpdateMenuCheckmark();
        Debug.Log(
            "Factory icon auto-bake on prefab changes: " +
            (AutoBakeEnabled ? "enabled" : "disabled"));
    }

    [MenuItem(AutoBakeMenuPath, true)]
    private static bool ValidateAutoBakeMenu()
    {
        UpdateMenuCheckmark();
        return true;
    }

    private static void UpdateMenuCheckmark()
    {
        Menu.SetChecked(AutoBakeMenuPath, AutoBakeEnabled);
    }

    private static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        if (!AutoBakeEnabled || FactoryIconBaker.IsBaking)
            return;

        IEnumerable<string> changedAssets = importedAssets
            .Concat(deletedAssets)
            .Concat(movedAssets)
            .Concat(movedFromAssetPaths);
        if (changedAssets.Any(IsFactoryPrefab))
            QueueIncrementalBake();
    }

    private static bool IsFactoryPrefab(string path)
    {
        if (string.IsNullOrEmpty(path) ||
            !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path.StartsWith(
                   FactoryIconBaker.ItemPrefabDirectory + "/",
                   StringComparison.Ordinal) ||
               path.StartsWith(
                   FactoryIconBaker.BuildingPrefabDirectory + "/",
                   StringComparison.Ordinal);
    }

    private static void QueueIncrementalBake()
    {
        if (bakeQueued)
            return;

        bakeQueued = true;
        EditorApplication.delayCall += () =>
        {
            bakeQueued = false;
            if (AutoBakeEnabled &&
                !EditorApplication.isCompiling &&
                !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                FactoryIconBaker.BakeAll(false);
            }
        };
    }
}
