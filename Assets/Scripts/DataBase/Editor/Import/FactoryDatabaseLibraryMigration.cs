using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class FactoryDatabaseLibraryMigration
{
    public const string ItemsPath = "Assets/Data/ItemDatabase.asset";
    public const string RecipesPath = "Assets/Data/RecipeDatabase.asset";
    public const string BuildingsPath = "Assets/Data/BuildingDatabase.asset";

    [MenuItem("Factory/Migrate Content Libraries")]
    public static void Run()
    {
        FactoryDatabaseEditor.EnsureCanWrite();
        var root = FactoryDatabaseEditor.Load();
        if (root.itemDatabase != null && root.recipeDatabase != null && root.buildingDatabase != null)
        {
            FactoryDatabaseEditor.Validate(root);
            Debug.Log("FACTORY_CONTENT_LIBRARIES_ALREADY_MIGRATED"); return;
        }
        if (root.itemDatabase != null || root.recipeDatabase != null || root.buildingDatabase != null)
            throw new InvalidDataException("Partial library configuration; migration will not overwrite it.");
        foreach (string path in new[] { ItemsPath, RecipesPath, BuildingsPath })
            if (File.Exists(path)) throw new InvalidDataException("Migration target already exists: " + path);
        FactoryDatabaseValidation.Validate(root);
        byte[] backup = File.ReadAllBytes(FactoryDatabaseEditor.DatabaseAssetPath);
        var items = root.items; var recipes = root.recipes; var buildings = root.buildings; var machines = root.machineTypes;
        try
        {
            var itemLibrary = ScriptableObject.CreateInstance<FactoryItemDatabaseAsset>(); itemLibrary.items = items;
            var recipeLibrary = ScriptableObject.CreateInstance<FactoryRecipeDatabaseAsset>(); recipeLibrary.recipes = recipes;
            var buildingLibrary = ScriptableObject.CreateInstance<FactoryBuildingDatabaseAsset>(); buildingLibrary.buildings = buildings; buildingLibrary.machineTypes = machines;
            AssetDatabase.CreateAsset(itemLibrary, ItemsPath); AssetDatabase.CreateAsset(recipeLibrary, RecipesPath); AssetDatabase.CreateAsset(buildingLibrary, BuildingsPath);
            root.itemDatabase = itemLibrary; root.recipeDatabase = recipeLibrary; root.buildingDatabase = buildingLibrary;
            root.ClearInlineData();
            FactoryDatabaseEditor.Validate(root);
            EditorUtility.SetDirty(root); AssetDatabase.SaveAssetIfDirty(root);
            Debug.Log($"FACTORY_CONTENT_LIBRARIES_MIGRATED items={items.Length} buildings={buildings.Length} recipes={recipes.Length}");
        }
        catch
        {
            root.itemDatabase = null; root.recipeDatabase = null; root.buildingDatabase = null;
            root.items = items; root.recipes = recipes; root.buildings = buildings; root.machineTypes = machines;
            foreach (string path in new[] { ItemsPath, RecipesPath, BuildingsPath }) AssetDatabase.DeleteAsset(path);
            AssetDatabase.ReleaseCachedFileHandles(); File.WriteAllBytes(FactoryDatabaseEditor.DatabaseAssetPath, backup);
            AssetDatabase.ImportAsset(FactoryDatabaseEditor.DatabaseAssetPath, ImportAssetOptions.ForceUpdate); throw;
        }
    }
}
