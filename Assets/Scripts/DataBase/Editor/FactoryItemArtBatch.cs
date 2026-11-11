using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// Item mode of the content manager, with the same transactional recipe editing API.
public static class FactoryItemArtBatch
{
    const string Root = "D:/UnityProject/Factory-with-DOTS-Arts/Factory Items";
    sealed class Recipe
    {
        public string key, machine, input, output;
        public int count;
        public Recipe(string key, string machine, string input, string output, int count = 1)
        { this.key = key; this.machine = machine; this.input = input; this.output = output; this.count = count; }
    }
    static readonly Recipe[] Recipes = {
        new Recipe("smelt_iron", "smelting", "iron_ore", "iron_ingot"),
        new Recipe("smelt_copper", "smelting", "copper_ore", "copper_ingot"),
        new Recipe("stamp_iron_plate", "stamping", "iron_ingot", "iron_plate"),
        new Recipe("stamp_iron_gear", "stamping", "iron_ingot", "iron_gear"),
        new Recipe("stamp_iron_pipe", "stamping", "iron_ingot", "iron_pipe"),
        new Recipe("stamp_iron_nail", "stamping", "iron_ingot", "iron_nail", 4),
        new Recipe("stamp_copper_wire", "stamping", "copper_ingot", "copper_wire", 2)
    };

    [MenuItem("Factory/Art/Import Reference Items and Recipes")]
    public static void Run()
    {
        var draft = ScriptableObject.CreateInstance<FactoryContentDraft>();
        try
        {
            draft.Load();
            var before = draft.GetItems().ToDictionary(x => x.key);
            var guids = before.Values.ToDictionary(x => x.key, x => AssetDatabase.AssetPathToGUID(x.modelPath));
            string buildingsBefore = string.Join(";", draft.GetBuildings().Select(b => JsonUtility.ToJson(b)));
            var items = JArray.Parse(File.ReadAllText(Root + "/Model/items-manifest.json"));
            foreach (var item in items) Import(draft, item);
            foreach (var recipe in Recipes) ConfigureRecipe(draft, recipe);
            if (string.Join(";", draft.GetBuildings().Select(b => JsonUtility.ToJson(b))) != buildingsBefore)
                throw new Exception("Unexpected building change.");
            draft.Save();
            AssetDatabase.SaveAssets();
            draft.Load();
            foreach (var item in items) Verify(draft, item);
            foreach (var previous in before.Values)
            {
                var current = draft.GetItems().Single(x => x.key == previous.key);
                if (current.id != previous.id || current.name != previous.name || current.maxStack != previous.maxStack ||
                    current.category != previous.category || current.iconKey != previous.iconKey || current.modelPath != previous.modelPath ||
                    AssetDatabase.AssetPathToGUID(current.modelPath) != guids[previous.key])
                    throw new Exception("Existing item identity changed: " + previous.key);
            }
            foreach (var recipe in Recipes) VerifyRecipe(draft, recipe);
            File.WriteAllText(Root + "/Model/recipe-import-report.txt", string.Join("\n", Recipes.Select(r =>
                r.key + " [" + r.machine + "] 1 " + r.input + " -> " + r.count + " " + r.output)) +
                "\nITEMS_AND_RECIPES_OK\nExisting item IDs, model paths and GUIDs preserved.\nBuilding tables unchanged.\n");
            Debug.Log("FACTORY_REFERENCE_ITEMS_AND_RECIPES_OK items=" + items.Count + " recipes=" + Recipes.Length);
        }
        finally { UnityEngine.Object.DestroyImmediate(draft); }
    }

    static void Import(FactoryContentDraft draft, JToken item)
    {
        string key = (string)item["key"], folder = Root + "/Model/" + (string)item["title"];
        bool isNew = !draft.GetItems().Any(x => x.key == key);
        // Exactly the public API used by the unified importer's Item mode.
        int row = draft.ImportItemModel(key, folder + "/" + key + ".fbx");
        if (isNew)
        {
            draft.SetItemValue(row, "name_key", (string)item["name"]);
            draft.SetItemValue(row, "category", (string)item["category"]);
        }
        var current = draft.GetItems().Single(x => x.key == key);
        var palette = JObject.Parse(File.ReadAllText(folder + "/materials.json"));
        foreach (var source in FactoryModelPipeline.Sources(current.modelPath))
        {
            var data = palette[source.name] ?? throw new Exception("Missing palette: " + source.name);
            var m = FactoryModelPipeline.CreateMaterial(current.modelPath, source);
            var c = data["color"];
            m.SetColor("_BaseColor", new Color((float)c[0], (float)c[1], (float)c[2]).gamma);
            m.SetFloat("_Metallic", (float)data["metallic"]);
            m.SetFloat("_Smoothness", (float)data["smoothness"]);
            m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(current.modelPath);
        ValidateGeometry(model, folder, key);
        var settings = ScriptableObject.CreateInstance<FactoryIconBakeSettings>();
        try
        {
            settings.viewDirection = new Vector3(-.8f, -.85f, 1.1f);
            settings.resolution = 700;
            File.WriteAllBytes(Root + "/Preview/" + key + "_Unity.png", FactoryIconRenderer.RenderPng(model, settings));
            string iconKey = string.IsNullOrEmpty(current.iconKey) ? key : current.iconKey;
            var index = FactoryDatabaseEditor.BuildSpriteIndex(FactoryIconBaker.ItemIconDirectory);
            string icon = index.TryGetValue(FactoryDatabaseEditor.NormalizeKey(iconKey), out var sprite)
                ? AssetDatabase.GetAssetPath(sprite) : FactoryIconBaker.ItemIconDirectory + "/" + iconKey + ".png";
            settings.resolution = 256;
            File.WriteAllBytes(icon, FactoryIconRenderer.RenderPng(model, settings));
            AssetDatabase.ImportAsset(icon, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(icon);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
        }
        finally { UnityEngine.Object.DestroyImmediate(settings); }
    }

    static void ConfigureRecipe(FactoryContentDraft draft, Recipe recipe)
    {
        var existing = draft.GetRecipes().SingleOrDefault(r => r.key == recipe.key);
        int row = existing == null ? draft.AddRecipe() : draft.GetRecipes().FindIndex(r => r.key == recipe.key);
        // GetRecipes returns source row order; use the explicit stored row for existing recipes.
        if (existing != null) row = existing.row;
        if (existing == null)
        {
            draft.SetRecipeValue(row, "key", recipe.key);
            draft.SetRecipeValue(row, "duration_seconds", recipe.machine == "smelting" ? "2" : "1");
        }
        draft.SetRecipeValue(row, "machine_type_key", recipe.machine);
        foreach (bool output in new[] { false, true })
            foreach (var ingredient in draft.GetIngredients(recipe.key, output).OrderByDescending(x => x.row).ToArray())
                draft.RemoveIngredient(recipe.key, output, ingredient.row);
        draft.AddIngredient(recipe.key, false, recipe.input, 1);
        draft.AddIngredient(recipe.key, true, recipe.output, recipe.count);
    }

    static Bounds ValidateGeometry(GameObject model, string folder, string key)
    {
        var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers.Length != 1) throw new Exception("Expected one static item renderer: " + key);
        var bounds = renderers[0].bounds;
        var expected = JObject.Parse(File.ReadAllText(folder + "/" + key + ".geometry.json"));
        for (int axis = 0; axis < 3; axis++)
            if (Mathf.Abs(bounds.min[axis] - (float)expected["minimum"][axis]) > .001f ||
                Mathf.Abs(bounds.max[axis] - (float)expected["maximum"][axis]) > .001f)
                throw new Exception("Geometry axis/scale mismatch: " + key);
        if (bounds.center.magnitude > .001f || bounds.size.x > .56f || bounds.size.z > .56f)
            throw new Exception("Invalid item scale or pivot: " + key);
        if (model.GetComponentsInChildren<Component>(true).Any(c => !(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer)))
            throw new Exception("Item has unsupported components: " + key);
        if (renderers.SelectMany(r => r.sharedMaterials).Any(m => m == null || m.shader.name != "Universal Render Pipeline/Lit" || ShaderUtil.ShaderHasError(m.shader)))
            throw new Exception("Invalid item URP mapping: " + key);
        return bounds;
    }

    static void Verify(FactoryContentDraft draft, JToken definition)
    {
        string key = (string)definition["key"], folder = Root + "/Model/" + (string)definition["title"];
        var item = draft.GetItems().Single(x => x.key == key);
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(item.modelPath);
        Bounds bounds = ValidateGeometry(model, folder, key);
        string iconKey = string.IsNullOrEmpty(item.iconKey) ? key : item.iconKey;
        if (!FactoryDatabaseEditor.BuildSpriteIndex(FactoryIconBaker.ItemIconDirectory).ContainsKey(FactoryDatabaseEditor.NormalizeKey(iconKey)))
            throw new Exception("Missing item icon: " + key);
        string report = "ITEM_IMPORT_OK\nKey=" + key + "\nID=" + item.id + "\nModel=" + item.modelPath +
            "\nGUID=" + AssetDatabase.AssetPathToGUID(item.modelPath) + "\nBounds=" + bounds +
            "\nTriangles=" + model.GetComponentInChildren<MeshFilter>().sharedMesh.triangles.Length / 3 +
            "\nRenderers=1\nCollider/Rigidbody/Scripts=none\nGeometry=Blender/Unity bounds within 0.001m\n";
        File.WriteAllText(folder + "/unity-import-report.txt", report);
        Debug.Log(report);
    }

    static void VerifyRecipe(FactoryContentDraft draft, Recipe recipe)
    {
        var r = draft.GetRecipes().Single(x => x.key == recipe.key);
        var inputs = draft.GetIngredients(recipe.key, false);
        var outputs = draft.GetIngredients(recipe.key, true);
        if (r.machine != recipe.machine || inputs.Count != 1 || inputs[0].itemKey != recipe.input || inputs[0].count != 1 ||
            outputs.Count != 1 || outputs[0].itemKey != recipe.output || outputs[0].count != recipe.count)
            throw new Exception("Invalid saved recipe: " + recipe.key);
    }
}
