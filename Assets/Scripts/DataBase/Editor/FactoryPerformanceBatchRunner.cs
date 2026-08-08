using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FactoryPerformanceBatchRunner
{
    private const string SceneArgument = "-factoryPerformanceScene";
    private const string SharedSubScenePath =
        "Assets/Scenes/Stage3EntitiesSubscene.unity";

    public static void ReimportSubSceneAndRun()
    {
        Debug.Log(
            "[ECS Performance] Force reimporting shared SubScene: " +
            SharedSubScenePath);
        AssetDatabase.ImportAsset(
            SharedSubScenePath,
            ImportAssetOptions.ForceSynchronousImport |
            ImportAssetOptions.ForceUpdate);
        Run();
    }

    public static void Run()
    {
        try
        {
            string[] arguments = Environment.GetCommandLineArgs();
            string sceneName = ReadArgument(arguments, SceneArgument);
            if (string.IsNullOrEmpty(sceneName))
            {
                throw new ArgumentException(
                    "Missing " + SceneArgument + " <scene-name>.");
            }

            string scenePath = Path.Combine(
                    "Assets",
                    "Scenes",
                    "Performance",
                    sceneName + ".unity")
                .Replace('\\', '/');
            if (!File.Exists(scenePath))
            {
                throw new FileNotFoundException(
                    "Performance scene not found.",
                    scenePath);
            }

            Debug.Log(
                "[ECS Performance] Opening batch scene: " + scenePath);
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            EditorApplication.delayCall += EnterPlayMode;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(2);
        }
    }

    private static void EnterPlayMode()
    {
        Debug.Log("[ECS Performance] Entering Play Mode for capture.");
        EditorApplication.isPlaying = true;
    }

    private static string ReadArgument(string[] arguments, string name)
    {
        int index = Array.IndexOf(arguments, name);
        return index >= 0 && index + 1 < arguments.Length
            ? arguments[index + 1]
            : null;
    }
}
