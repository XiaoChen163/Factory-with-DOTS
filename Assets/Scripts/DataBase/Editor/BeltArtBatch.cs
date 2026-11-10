using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class BeltArtBatch
{
    const string Root = "D:/UnityProject/Factory-with-DOTS-Arts/Factory Buildings";
    static readonly string[] Edges = { "East", "North", "West", "South" };

    [MenuItem("Factory/Art/Import Reference Belts")]
    public static void Run()
    {
        var draft = ScriptableObject.CreateInstance<FactoryContentDraft>();
        try
        {
            draft.Load();
            var tables = draft.Tables.ToDictionary(t => t.path, t => t.text);
            var settings = AssetDatabase.LoadAssetAtPath<FactoryIconBakeSettings>(FactoryIconBaker.SettingsAssetPath);
            if (settings == null) throw new Exception("Missing icon bake settings.");
            var overrides = settings.overrides.ToList();
            for (int tier = 1; tier <= 4; tier++)
            {
                string key = "belt_mk" + tier, title = "BeltMk" + tier, folder = Root + "/Model/" + title;
                var before = draft.GetBuildings().Single(b => b.key == key);
                string guid = AssetDatabase.AssetPathToGUID(before.modelPath);
                string iconGuid = AssetDatabase.AssetPathToGUID(before.iconPath);
                string stats = draft.GetBuildingStat(key, "belt_stats", "cells_per_second");
                var json = FactoryBuildingJson.Read(folder + "/" + key + ".factory.json");
                if (json.Colliders.Length != 0) throw new Exception("Belts must not have collision proxies.");
                // Files are staged while the Editor is closed, preserving existing .meta.
                if (!File.ReadAllBytes(before.modelPath).SequenceEqual(File.ReadAllBytes(folder + "/" + key + ".fbx")))
                    throw new Exception("Stage the updated FBX with the Editor closed: " + key);
                draft.ImportBuildingModel(json, before.modelPath, BuildingKind.Belt, null);
                MapMaterials(before.modelPath, folder);
                string iconPath = "Assets/Art/IconSources/" + title + "/" + key + "_icon.fbx";
                FactoryModelPipeline.Configure(iconPath);
                MapMaterials(iconPath, folder);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(before.modelPath);
                var icon = AssetDatabase.LoadAssetAtPath<GameObject>(iconPath);
                Validate(model, icon, folder, key, tier);
                // This persists the two-rail source for subsequent normal icon bakes.
                overrides.RemoveAll(o => o != null && o.prefab == model);
                overrides.Add(new FactoryIconBakeOverride
                {
                    prefab = model, iconPrefab = icon,
                    viewDirection = new Vector3(-1.7f, -2.1f, 2.3f), padding = 1.12f
                });
                settings.overrides = overrides.ToArray();
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                Render(icon, model, before.iconPath, settings, title);
                var after = draft.GetBuildings().Single(b => b.key == key);
                if (after.behavior != before.behavior || after.width != before.width || after.length != before.length ||
                    after.modelPath != before.modelPath || after.iconPath != before.iconPath ||
                    AssetDatabase.AssetPathToGUID(after.modelPath) != guid || AssetDatabase.AssetPathToGUID(after.iconPath) != iconGuid ||
                    stats != draft.GetBuildingStat(key, "belt_stats", "cells_per_second") || draft.GetBuildingImport(key).colliders.Length != 0)
                    throw new Exception("Unexpected identity, gameplay or collision change: " + key);
                int triangles = model.GetComponentsInChildren<MeshFilter>(true).Sum(m => m.sharedMesh.triangles.Length / 3);
                File.WriteAllText(folder + "/unity-import-report.txt", "BELT_IMPORT_OK\nKey=" + key + "\nGUID=" + guid +
                    "\nRenderers=6, all four rails independently hideable\nTriangle=independent, pivot at centre, default +X\n" +
                    "Icon=body, triangle, North/South rails only; persistent icon bake override\nColliders=0\nTriangles=" + triangles +
                    "\nGrade color=" + model.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "Direction Triangle").sharedMaterial.name +
                    "\nSpeed=" + stats + " cells/s, unchanged\nGeometry=Blender/Unity bounds match within 0.001m\n");
            }
            foreach (var t in draft.Tables)
                if (t.text != tables[t.path]) throw new Exception("Gameplay table changed: " + t.path);
            draft.Save();
            var candidate = draft.ReadCandidate();
            try { FactoryDatabaseCsvImporter.Generate(candidate); }
            finally { UnityEngine.Object.DestroyImmediate(candidate); }
            AssetDatabase.SaveAssets();
            Debug.Log("REFERENCE_BELT_ART_IMPORT_OK tiers=4 colliders=0");
        }
        finally { UnityEngine.Object.DestroyImmediate(draft); }
    }

    static void MapMaterials(string path, string folder)
    {
        var palette = JObject.Parse(File.ReadAllText(folder + "/materials.json"));
        foreach (var source in FactoryModelPipeline.Sources(path))
        {
            var d = palette[source.name] ?? throw new Exception("Missing palette: " + source.name);
            var material = FactoryModelPipeline.CreateMaterial(path, source);
            var c = d["color"];
            material.SetColor("_BaseColor", new Color((float)c[0], (float)c[1], (float)c[2]).gamma);
            material.SetFloat("_Metallic", (float)d["metallic"]);
            material.SetFloat("_Smoothness", (float)d["smoothness"]);
            material.DisableKeyword("_EMISSION"); material.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(material);
        }
        AssetDatabase.SaveAssets();
    }

    static void Validate(GameObject model, GameObject icon, string folder, string key, int tier)
    {
        FactoryDatabaseCsvImporter.LoadModel(AssetDatabase.GetAssetPath(model));
        var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
        if (renderers.Length != 6) throw new Exception("Expected body, four edges and independent triangle.");
        var expected = (JArray)JObject.Parse(File.ReadAllText(folder + "/" + key + ".geometry.json"))["meshes"];
        foreach (var record in expected)
        {
            var r = renderers.Single(x => x.name == (string)record["name"]);
            for (int axis = 0; axis < 3; axis++)
                if (Mathf.Abs(r.bounds.min[axis] - (float)record["minimum"][axis]) > .001f ||
                    Mathf.Abs(r.bounds.max[axis] - (float)record["maximum"][axis]) > .001f)
                    throw new Exception("Geometry axes/units differ: " + r.name);
        }
        foreach (string edge in Edges)
        {
            var part = FactoryBuildingVisualUtility.FindPart(model, "Connection Edge " + edge);
            var r = part.GetComponent<MeshRenderer>();
            if (part.GetComponentsInChildren<MeshRenderer>(true).Length != 1 || r == null)
                throw new Exception("Whole rail assembly must use a single independently controlled renderer.");
            Vector3 direction = edge == "East" ? Vector3.right : edge == "West" ? Vector3.left : edge == "North" ? Vector3.forward : Vector3.back;
            if (Vector3.Dot(r.bounds.center, direction) < .42f) throw new Exception("Rail assigned to wrong direction.");
            if (!r.sharedMaterials.Any(m => m.name.StartsWith("Belt_Tier" + tier + "_", StringComparison.Ordinal)))
                throw new Exception("Missing tier color on rail.");
        }
        var triangle = FactoryBuildingVisualUtility.FindPart(model, "Direction Triangle");
        if (Vector3.Distance(triangle.TransformPoint(Vector3.zero), new Vector3(0, .185f, 0)) > .001f)
            throw new Exception("Triangle must rotate around the belt centre.");
        var tip = triangle.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v => triangle.TransformPoint(v)).OrderByDescending(v => v.x).First();
        if (tip.x < .15f || Mathf.Abs(tip.z) > .001f) throw new Exception("Default triangle must face +X.");
        foreach (var asset in new[] { model, icon })
        {
            if (asset.GetComponentsInChildren<Component>(true).Any(c => !(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer)))
                throw new Exception("Belts must not contain collision, rigidbody or script components.");
            if (((ModelImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(asset))).addCollider)
                throw new Exception("Automatic colliders must be disabled.");
            if (asset.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials).Any(m => m == null || m.shader.name != "Universal Render Pipeline/Lit" || ShaderUtil.ShaderHasError(m.shader)))
                throw new Exception("Invalid URP material.");
        }
        var iconParts = icon.GetComponentsInChildren<MeshRenderer>().Select(r => r.name).OrderBy(n => n).ToArray();
        if (!iconParts.SequenceEqual(new[] { "Belt Body", "Connection Edge North", "Connection Edge South", "Direction Triangle" }.OrderBy(n => n)))
            throw new Exception("Icon source must have exactly two lateral rails.");
    }

    static void Render(GameObject icon, GameObject model, string iconPath, FactoryIconBakeSettings settings, string title)
    {
        File.WriteAllBytes(iconPath, FactoryIconRenderer.RenderPng(icon, settings, model));
        AssetDatabase.ImportAsset(iconPath, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(iconPath);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.SaveAndReimport();
        var preview = UnityEngine.Object.Instantiate(settings);
        try
        {
            preview.resolution = 900;
            File.WriteAllBytes(Root + "/Preview/" + title + "_Unity.png", FactoryIconRenderer.RenderPng(icon, preview, model));
        }
        finally { UnityEngine.Object.DestroyImmediate(preview); }
    }
}
