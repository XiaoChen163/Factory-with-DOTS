using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class VerticalBeltArtBatch
{
    const string Root = "D:/UnityProject/Factory-with-DOTS-Arts/Factory Buildings";
    static readonly string[] Roles = { "entry", "segment", "exit" };

    [MenuItem("Factory/Art/Import Reference Vertical Belts")]
    public static void Run()
    {
        var draft = ScriptableObject.CreateInstance<FactoryContentDraft>();
        try
        {
            draft.Load();
            string beforeContent = EditorJsonUtility.ToJson(draft);
            var settings = AssetDatabase.LoadAssetAtPath<FactoryIconBakeSettings>(FactoryIconBaker.SettingsAssetPath);
            if (settings == null) throw new Exception("Missing icon settings.");
            for (int tier = 1; tier <= 4; tier++)
            {
                string title = "VerticalBeltMk" + tier, folder = Root + "/Model/" + title;
                var models = new GameObject[3];
                for (int index = 0; index < Roles.Length; index++)
                {
                    string key = "vertical_belt_" + Roles[index] + "_mk" + tier;
                    var before = draft.GetBuildings().Single(b => b.key == key);
                    string guid = AssetDatabase.AssetPathToGUID(before.modelPath);
                    var json = FactoryBuildingJson.Read(folder + "/" + key + ".factory.json");
                    if (json.Colliders.Length != 0) throw new Exception("Do not add new collision proxies to vertical belt art.");
                    if (!File.ReadAllBytes(before.modelPath).SequenceEqual(File.ReadAllBytes(folder + "/" + key + ".fbx")))
                        throw new Exception("Stage the source FBX with the Editor closed: " + key);
                    draft.ImportBuildingModel(json, before.modelPath, before.Kind, null);
                    MapMaterials(before.modelPath, folder);
                    models[index] = AssetDatabase.LoadAssetAtPath<GameObject>(before.modelPath);
                    Bounds bounds = ValidateModel(models[index], folder, key, tier);
                    if (AssetDatabase.AssetPathToGUID(before.modelPath) != guid || draft.GetBuildingImport(key).colliders.Length != 0)
                        throw new Exception("Model identity or collider data changed: " + key);
                    int triangles = models[index].GetComponentsInChildren<MeshFilter>().Sum(m => m.sharedMesh.triangles.Length / 3);
                    File.WriteAllText(folder + "/" + Roles[index] + "-unity-import-report.txt", "VERTICAL_PART_IMPORT_OK\nKey=" + key +
                        "\nModel=" + before.modelPath + "\nGUID=" + guid + "\nBounds=" + bounds + "\nTriangles=" + triangles +
                        "\nUnit height=1m; floor pivot at Y=0\nGrade=" + tier + "\nImported collision proxies=0; existing runtime picking behavior preserved\n");
                }
                ValidateSplices(models);
                string iconPath = "Assets/Art/IconSources/" + title + "/" + title + ".fbx";
                FactoryModelPipeline.Configure(iconPath); MapMaterials(iconPath, folder);
                var icon = AssetDatabase.LoadAssetAtPath<GameObject>(iconPath);
                var entry = draft.GetBuildings().Single(b => b.key == "vertical_belt_entry_mk" + tier);
                var matches = settings.overrides.Where(o => o != null && o.prefab == models[0]).ToArray();
                if (matches.Length != 1) throw new Exception("Expected existing combined vertical icon override.");
                matches[0].iconPrefab = icon;
                matches[0].customIcon = null;
                matches[0].viewDirection = new Vector3(4, -2, 6);
                matches[0].padding = 1.12f;
                matches[0].centerOffset = Vector3.zero;
                EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets();
                File.WriteAllBytes(entry.iconPath, FactoryIconRenderer.RenderPng(icon, settings, models[0]));
                AssetDatabase.ImportAsset(entry.iconPath, ImportAssetOptions.ForceSynchronousImport);
                var texture = (TextureImporter)AssetImporter.GetAtPath(entry.iconPath);
                texture.textureType = TextureImporterType.Sprite; texture.spriteImportMode = SpriteImportMode.Single;
                texture.mipmapEnabled = false; texture.alphaIsTransparency = true; texture.SaveAndReimport();
                var preview = UnityEngine.Object.Instantiate(settings);
                try
                {
                    preview.resolution = 1000;
                    File.WriteAllBytes(Root + "/Preview/" + title + "_Unity.png", FactoryIconRenderer.RenderPng(icon, preview, models[0]));
                }
                finally { UnityEngine.Object.DestroyImmediate(preview); }
                File.WriteAllText(folder + "/assembly-check-report.txt", "VERTICAL_ASSEMBLY_OK\nModules=entry + repeatable segment + exit\n" +
                    "Segment bounds Y=0..1\nSplice surface vertices match for repeated modules and both terminal roles\n" +
                    "Downward X-flipped profile matches upward profile\nDefault entry=-X, default exit=+X\n" +
                    "Icon demonstrates a quarter-turned outlet; individual source models retain default directions\n");
            }
            if (EditorJsonUtility.ToJson(draft) != beforeContent) throw new Exception("Gameplay content changed.");
            draft.Save();
            var candidate = draft.ReadCandidate();
            try { FactoryDatabaseEditor.Save(candidate); }
            finally { UnityEngine.Object.DestroyImmediate(candidate); }
            AssetDatabase.SaveAssets();
            Debug.Log("VERTICAL_BELT_ART_IMPORT_OK tiers=4 modules=12 splice_checks=passed");
        }
        finally { UnityEngine.Object.DestroyImmediate(draft); }
    }

    static Vector3[] Vertices(GameObject model) => model.GetComponentsInChildren<MeshFilter>(true)
        .SelectMany(m => m.sharedMesh.vertices.Select(v => m.transform.TransformPoint(v))).ToArray();

    static void ValidateSplices(GameObject[] models)
    {
        var segment = Vertices(models[1]);
        var bottom = segment.Where(v => Mathf.Abs(v.y) < .00005f).ToArray();
        var top = segment.Where(v => Mathf.Abs(v.y - 1) < .00005f).Select(v => v - Vector3.up).ToArray();
        if (bottom.Length == 0 || top.Length == 0) throw new Exception("Missing segment splice surfaces.");
        void RequireMatch(Vector3[] source, Vector3[] target)
        {
            foreach (var v in source)
                if (!target.Any(t => Vector3.Distance(v, t) < .0001f)) throw new Exception("Vertical splice profile mismatch.");
        }
        RequireMatch(bottom, top); RequireMatch(top, bottom);
        RequireMatch(bottom, top.Select(v => new Vector3(v.x, 0, -v.z)).ToArray());
        foreach (var terminal in new[] { models[0], models[2] })
        {
            var points = Vertices(terminal);
            RequireMatch(bottom, points.Where(v => Mathf.Abs(v.y) < .00005f).ToArray());
            RequireMatch(bottom, points.Where(v => Mathf.Abs(v.y - 1) < .00005f).Select(v => v - Vector3.up).ToArray());
        }
    }

    static Bounds ValidateModel(GameObject model, string folder, string key, int tier)
    {
        FactoryDatabaseEditor.LoadModel(AssetDatabase.GetAssetPath(model));
        var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
        var expected = (JArray)JObject.Parse(File.ReadAllText(folder + "/" + key + ".geometry.json"))["meshes"];
        if (renderers.Length != expected.Count) throw new Exception("Mesh count mismatch: " + key);
        foreach (var item in expected)
        {
            var r = renderers.Single(x => x.name == (string)item["name"]);
            for (int axis = 0; axis < 3; axis++)
                if (Mathf.Abs(r.bounds.min[axis] - (float)item["minimum"][axis]) > .001f ||
                    Mathf.Abs(r.bounds.max[axis] - (float)item["maximum"][axis]) > .001f)
                    throw new Exception("Coordinate conversion mismatch: " + key);
        }
        var bounds = renderers[0].bounds;
        foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
        if (Mathf.Abs(bounds.min.y) > .001f || Mathf.Abs(bounds.max.y - 1) > .001f ||
            bounds.min.x < -.501f || bounds.max.x > .501f || bounds.min.z < -.501f || bounds.max.z > .501f)
            throw new Exception("Invalid one-cell vertical module bounds: " + key + " " + bounds);
        var materials = renderers.SelectMany(r => r.sharedMaterials).ToArray();
        if (!materials.Any(m => m.name.StartsWith("Belt_Tier" + tier + "_", StringComparison.Ordinal)) ||
            materials.Any(m => m == null || m.shader.name != "Universal Render Pipeline/Lit" || ShaderUtil.ShaderHasError(m.shader)))
            throw new Exception("Invalid grade/URP mapping: " + key);
        if (((ModelImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(model))).addCollider)
            throw new Exception("Do not generate automatic FBX colliders.");
        return bounds;
    }

    static void MapMaterials(string path, string folder)
    {
        var palette = JObject.Parse(File.ReadAllText(folder + "/materials.json"));
        foreach (var source in FactoryModelPipeline.Sources(path))
        {
            var d = palette[source.name] ?? throw new Exception("Missing palette: " + source.name);
            var m = FactoryModelPipeline.CreateMaterial(path, source); var c = d["color"];
            m.SetColor("_BaseColor", new Color((float)c[0], (float)c[1], (float)c[2]).gamma);
            m.SetFloat("_Metallic", (float)d["metallic"]); m.SetFloat("_Smoothness", (float)d["smoothness"]);
            m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();
    }
}
