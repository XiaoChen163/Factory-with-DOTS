using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// Same import and save APIs as Factory > 内容管理器; existing identities stay intact.
public static class LogisticsNodeArtBatch
{
    const string Root = "D:/UnityProject/Factory-with-DOTS-Arts/Factory Buildings";
    static readonly string[] Keys = { "merger", "splitter" };
    static readonly string[] Titles = { "Merger", "Splitter" };
    static string Ports(FactoryContentBuilding building) => string.Join(";", building.ports.Select(p => JsonUtility.ToJson(p)));

    [MenuItem("Factory/Art/Import Splitter and Merger")]
    public static void Run()
    {
        var draft = ScriptableObject.CreateInstance<FactoryContentDraft>();
        try
        {
            draft.Load();
            var previous = Keys.Select(k => draft.GetBuildings().Single(b => b.key == k)).ToArray();
            var guids = previous.Select(b => AssetDatabase.AssetPathToGUID(b.modelPath)).ToArray();
            var iconGuids = previous.Select(b => AssetDatabase.AssetPathToGUID(b.iconPath)).ToArray();
            string buildingsBefore = string.Join(";", draft.GetBuildings().Select(b => JsonUtility.ToJson(b)));
            var database = AssetDatabase.LoadAssetAtPath<FactoryDatabaseAsset>("Assets/Data/FactoryDatabase.asset");
            var ids = database.buildings.ToDictionary(b => b.key, b => b.id);
            for (int i = 0; i < Keys.Length; i++)
            {
                string folder = Root + "/Model/" + Titles[i], key = Keys[i];
                var json = FactoryBuildingJson.Read(folder + "/" + key + ".factory.json");
                string source = folder + "/" + key + ".fbx";
                // A closed-editor staging copy avoids Windows mapped-file locks.
                // If already staged, use the importer's supported project-FBX path.
                if (File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(previous[i].modelPath))) source = previous[i].modelPath;
                draft.ImportBuildingModel(json, source, previous[i].Kind, null);
                var imported = draft.GetBuildings().Single(b => b.key == key);
                if (Ports(imported) != Ports(previous[i])) throw new Exception("Default ports changed: " + key);
                MapMaterials(imported.modelPath, folder);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(imported.modelPath);
                ValidateGeometry(model, folder, key);
                Render(model, imported.iconPath, Titles[i], key);
            }
            // Only collision data should need a table write: paths and gameplay stay identical.
            if (string.Join(";", draft.GetBuildings().Select(b => JsonUtility.ToJson(b))) != buildingsBefore)
                throw new Exception("Unexpected building definition change.");
            draft.Save();
            AssetDatabase.SaveAssets();
            draft.Load();
            for (int i = 0; i < Keys.Length; i++)
            {
                string key = Keys[i], folder = Root + "/Model/" + Titles[i];
                var b = draft.GetBuildings().Single(x => x.key == key);
                if (b.width != 1 || b.length != 1 || !b.rectangle || b.behavior != previous[i].behavior ||
                    b.modelPath != previous[i].modelPath || b.iconPath != previous[i].iconPath || b.name != previous[i].name ||
                    b.menuOrder != previous[i].menuOrder || Ports(b) != Ports(previous[i]) ||
                    AssetDatabase.AssetPathToGUID(b.modelPath) != guids[i] || AssetDatabase.AssetPathToGUID(b.iconPath) != iconGuids[i])
                    throw new Exception("Identity, footprint or direction changed: " + key);
                var data = draft.GetBuildingImport(key);
                if (data.colliders.Length != 1 || data.colliders[0].name != "UC_LogisticsHousing")
                    throw new Exception("Missing simple collision proxy: " + key);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(b.modelPath);
                Bounds bounds = ValidateGeometry(model, folder, key);
                int triangles = model.GetComponentsInChildren<MeshFilter>(true).Sum(m => m.sharedMesh.triangles.Length / 3);
                string report = "LOGISTICS_IMPORT_OK\nKey=" + key + "\nModel=" + b.modelPath + "\nGUID=" + guids[i] +
                    "\nFootprint=1x1, unchanged\nPorts=" + Ports(b) + "\nDefaultDirection=unchanged\nRoofArrows=actual FBX tip vertices and flow directions verified\n" +
                    "ApertureWidth=0.62m\nRollerWidth=0.586m\nFasteners=28; cap planes and actual FBX vertices verified against mounting normals\n" +
                    "Colliders=1 Box\nRenderers=6\nTriangles=" + triangles + "\nBounds=" + bounds +
                    "\nModel and icon GUIDs preserved; all other definition fields unchanged\n";
                File.WriteAllText(folder + "/unity-import-report.txt", report);
                Debug.Log(report);
            }
            database = AssetDatabase.LoadAssetAtPath<FactoryDatabaseAsset>("Assets/Data/FactoryDatabase.asset");
            foreach (var entry in database.buildings)
                if (!ids.TryGetValue(entry.key, out var oldId) || oldId != entry.id) throw new Exception("Building ID changed: " + entry.key);
            Debug.Log("SPLITTER_MERGER_ART_IMPORT_OK");
        }
        finally { UnityEngine.Object.DestroyImmediate(draft); }
    }

    static void MapMaterials(string path, string folder)
    {
        var palette = JObject.Parse(File.ReadAllText(folder + "/materials.json"));
        foreach (var source in FactoryModelPipeline.Sources(path))
        {
            var d = palette[source.name] ?? throw new Exception("Missing palette: " + source.name);
            var m = FactoryModelPipeline.CreateMaterial(path, source);
            var c = d["color"];
            m.SetColor("_BaseColor", new Color((float)c[0], (float)c[1], (float)c[2]).gamma);
            m.SetFloat("_Metallic", (float)d["metallic"]);
            m.SetFloat("_Smoothness", (float)d["smoothness"]);
            m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();
    }

    static Bounds ValidateGeometry(GameObject model, string folder, string key)
    {
        var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
        var meshes = model.GetComponentsInChildren<MeshFilter>(true);
        var expected = (JArray)JObject.Parse(File.ReadAllText(folder + "/" + key + ".geometry.json"))["meshes"];
        if (renderers.Length != 6 || renderers.Length != expected.Count) throw new Exception("Invalid renderer count.");
        foreach (var record in expected)
        {
            var r = renderers.Single(x => x.name == (string)record["name"]);
            for (int axis = 0; axis < 3; axis++)
                if (Mathf.Abs(r.bounds.min[axis] - (float)record["minimum"][axis]) > .001f ||
                    Mathf.Abs(r.bounds.max[axis] - (float)record["maximum"][axis]) > .001f)
                    throw new Exception("FBX coordinate mismatch: " + r.name);
        }
        var bounds = renderers[0].bounds;
        foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
        if (Mathf.Abs(bounds.min.y) > .001f || bounds.min.x < -.5001f || bounds.max.x > .5001f || bounds.min.z < -.5001f || bounds.max.z > .5001f)
            throw new Exception("Model exceeds one-cell footprint or invalid ground pivot: " + bounds);
        if (renderers.SelectMany(r => r.sharedMaterials).Any(m => m == null || m.shader.name != "Universal Render Pipeline/Lit" || ShaderUtil.ShaderHasError(m.shader)))
            throw new Exception("Invalid URP mapping.");
        if (model.GetComponentsInChildren<Collider>(true).Length != 0 || model.GetComponentsInChildren<Rigidbody>(true).Length != 0)
            throw new Exception("Proxy must remain definition-only.");
        var metadata = (JArray)JObject.Parse(File.ReadAllText(folder + "/orientation.json"))["ports"];
        foreach (var port in metadata)
        {
            var start = port["roof_arrow_start"]; var end = port["roof_arrow_end"]; var flow = port["flow_unity"];
            Vector3 arrowDirection = new Vector3((float)end[0] - (float)start[0], 0, (float)end[1] - (float)start[1]).normalized;
            if (Vector3.Dot(arrowDirection, new Vector3((float)flow[0], 0, (float)flow[2])) < .999f)
                throw new Exception("Arrow direction differs from logistics flow.");
            bool output = (string)port["type"] == "Output";
            var filter = meshes.Single(m => m.name.EndsWith(output ? "_OutputMint" : "_InputOrange", StringComparison.Ordinal));
            Vector3 tip = new Vector3((float)end[0], (float)end[2] + .003f, (float)end[1]);
            if (!filter.sharedMesh.vertices.Any(v => Vector3.Distance(filter.transform.TransformPoint(v), tip) < .0005f))
                throw new Exception("Imported arrow tip or color does not match default face: " + key);
        }
        ValidateFasteners(meshes, folder);
        return bounds;
    }

    static void ValidateFasteners(MeshFilter[] meshes, string folder)
    {
        var checks = JObject.Parse(File.ReadAllText(folder + "/construction-checks.json"));
        if (Mathf.Abs((float)checks["aperture_width"] - .62f) > .00001f ||
            Mathf.Abs((float)checks["roller_width"] - .586f) > .00001f)
            throw new Exception("Incorrect widened conveyor dimensions.");
        var fasteners = (JArray)checks["fasteners"];
        if (fasteners.Count != 28) throw new Exception("Expected all 28 fasteners per model.");
        var metal = meshes.Single(m => m.name.EndsWith("_RollerSteel", StringComparison.Ordinal));
        var vertices = metal.sharedMesh.vertices.Select(v => metal.transform.TransformPoint(v)).ToArray();
        Vector3 ReadVector(JToken v) => new Vector3((float)v[0], (float)v[1], (float)v[2]);
        foreach (var screw in fasteners)
        {
            Vector3 centre = ReadVector(screw["centre_unity"]);
            Vector3 normal = ReadVector(screw["normal_unity"]);
            if (Mathf.Abs(normal.magnitude - 1) > .0001f) throw new Exception("Invalid mounting normal.");
            foreach (var capPoint in screw["cap_witnesses_unity"])
            {
                Vector3 point = ReadVector(capPoint);
                // A head's outer flat cap must be parallel to the mounting face,
                // with the full head thickness projecting along its normal.
                if (Mathf.Abs(Vector3.Dot(point - centre, normal) - .006f) > .00005f ||
                    !vertices.Any(v => Vector3.Distance(v, point) < .00005f))
                    throw new Exception("Misrotated or missing exported screw cap: " + (string)screw["name"]);
            }
        }
    }

    static void Render(GameObject model, string iconPath, string title, string key)
    {
        var settings = ScriptableObject.CreateInstance<FactoryIconBakeSettings>();
        try
        {
            settings.viewDirection = key == "merger" ? new Vector3(-1.6f, -1.54f, 2.3f) : new Vector3(1.6f, -1.54f, 2.3f);
            settings.resolution = 1000;
            File.WriteAllBytes(Root + "/Preview/" + title + "_Unity.png", FactoryIconRenderer.RenderPng(model, settings));
            settings.resolution = 256;
            File.WriteAllBytes(iconPath, FactoryIconRenderer.RenderPng(model, settings));
            AssetDatabase.ImportAsset(iconPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(iconPath);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false; importer.alphaIsTransparency = true; importer.SaveAndReimport();
        }
        finally { UnityEngine.Object.DestroyImmediate(settings); }
    }
}
