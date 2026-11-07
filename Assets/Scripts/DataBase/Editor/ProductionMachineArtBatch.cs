using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

// Uses the same draft, model pipeline and icon renderer as Factory > 内容管理器.
public static class ProductionMachineArtBatch
{
    const string ArtRoot = "D:/UnityProject/Factory-with-DOTS-Arts/Factory Buildings";
    static readonly string[] Titles = { "Generator", "StampingPress", "AssemblyMachine" };
    static readonly string[] Keys = { "generator", "stamping_press", "assembly_machine" };
    static readonly string[] Names = { "发电机", "冲压机", "装配机" };
    static readonly string[] Machines = { "generation", "stamping", "assembly" };

    [MenuItem("Factory/Art/Import Production Machines")]
    public static void Run()
    {
        var draft = ScriptableObject.CreateInstance<FactoryContentDraft>();
        try
        {
            draft.Load();
            for (int i = 0; i < Keys.Length; i++) Import(draft, i);
            // The normal content manager transaction validates all tables before writing.
            draft.Save();
            AssetDatabase.SaveAssets();
            draft.Load();
            for (int i = 0; i < Keys.Length; i++) Verify(draft, i);
            Debug.Log("PRODUCTION_MACHINE_ART_IMPORT_OK");
        }
        finally { UnityEngine.Object.DestroyImmediate(draft); }
    }

    static void Import(FactoryContentDraft draft, int i)
    {
        string key = Keys[i], folder = ArtRoot + "/Model/" + Titles[i];
        bool isNew = !draft.GetBuildings().Any(b => b.key == key);
        if (i > 0 && !draft.GetMachineTypes().Contains(Machines[i])) draft.AddMachineType(Machines[i]);
        var json = FactoryBuildingJson.Read(folder + "/" + key + ".factory.json");
        // Processor requires an output. Storage keeps the requested single-input art
        // placeable without introducing power gameplay or weakening data validation.
        draft.ImportBuildingModel(json, folder + "/" + key + ".fbx", i == 0 ? BuildingKind.Storage : BuildingKind.Processor, i == 0 ? null : Machines[i]);
        if (isNew)
        {
            draft.SetBuildingName(key, Names[i]);
            draft.SetBuildingStat(key, "building_presentation", "menu_order", (310 + i * 10).ToString());
            if (i == 0) draft.SetBuildingStat(key, "storage_stats", "slot_count", "1");
        }
        string path = AssetDatabase.GetAssetPath(draft.GetBuildingImport(key).model);
        var palette = JObject.Parse(File.ReadAllText(folder + "/materials.json"));
        foreach (var source in FactoryModelPipeline.Sources(path))
        {
            var m = FactoryModelPipeline.CreateMaterial(path, source);
            var data = palette[source.name] ?? throw new InvalidDataException("Missing palette: " + source.name);
            var c = data["color"];
            Color color = new Color((float)c[0], (float)c[1], (float)c[2]).gamma;
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", (float)data["metallic"]);
            m.SetFloat("_Smoothness", (float)data["smoothness"]);
            float emission = (float)data["emission"];
            m.SetColor("_EmissionColor", color * emission);
            if (emission > 0) m.EnableKeyword("_EMISSION"); else m.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var settings = ScriptableObject.CreateInstance<FactoryIconBakeSettings>();
        try
        {
            settings.viewDirection = new Vector3(-6, -4.2f, 8);
            settings.resolution = 1000;
            File.WriteAllBytes(ArtRoot + "/Preview/" + Titles[i] + "_Unity.png", FactoryIconRenderer.RenderPng(model, settings));
            settings.resolution = 256;
            string icon = "Assets/Art/Icons/Buildings/" + Titles[i] + ".png";
            File.WriteAllBytes(icon, FactoryIconRenderer.RenderPng(model, settings));
            AssetDatabase.ImportAsset(icon, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(icon);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            draft.SetBuildingStat(key, "building_presentation", "icon_path", icon);
        }
        finally { UnityEngine.Object.DestroyImmediate(settings); }
        ValidateGeometry(model, folder, key);
    }

    static void ValidateGeometry(GameObject model, string folder, string key)
    {
        var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
        var expected = (JArray)JObject.Parse(File.ReadAllText(folder + "/" + key + ".geometry.json"))["meshes"];
        if (renderers.Length != expected.Count) throw new Exception("Mesh count mismatch: " + key);
        foreach (var record in expected)
        {
            var renderer = renderers.Single(r => r.name == (string)record["name"]);
            for (int axis = 0; axis < 3; axis++)
                if (Mathf.Abs(renderer.bounds.min[axis] - (float)record["minimum"][axis]) > .002f ||
                    Mathf.Abs(renderer.bounds.max[axis] - (float)record["maximum"][axis]) > .002f)
                    throw new Exception("Blender/Unity geometry mismatch: " + renderer.name);
        }
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        if (Mathf.Abs(bounds.min.y) > .002f || bounds.min.x < -1.501f || bounds.max.x > 1.501f || bounds.min.z < -1.501f || bounds.max.z > 1.501f)
            throw new Exception("Invalid 3x3 origin/bounds: " + key + " " + bounds);
        if (renderers.SelectMany(r => r.sharedMaterials).Any(m => m == null || m.shader.name != "Universal Render Pipeline/Lit" || ShaderUtil.ShaderHasError(m.shader)))
            throw new Exception("Invalid URP material: " + key);
        if (model.GetComponentsInChildren<Collider>(true).Length != 0 || model.GetComponentsInChildren<Rigidbody>(true).Length != 0)
            throw new Exception("Collision proxies must be CSV shapes, not FBX components.");
    }

    static void Verify(FactoryContentDraft draft, int i)
    {
        string key = Keys[i], folder = ArtRoot + "/Model/" + Titles[i];
        var building = draft.GetBuildings().Single(b => b.key == key);
        var json = FactoryBuildingJson.Read(folder + "/" + key + ".factory.json");
        if (building.width != 3 || building.length != 3 || !building.rectangle || building.ports.Length != (i == 0 ? 1 : 2))
            throw new Exception("Invalid footprint or ports: " + key);
        var input = building.ports.Single(p => p.type == BuildingPortType.Input);
        if (input.cellOffset != new Vector2Int(-1, 1) || input.direction != Vector2Int.right)
            throw new Exception("Input alignment: " + key);
        if (i > 0)
        {
            var output = building.ports.Single(p => p.type == BuildingPortType.Output);
            if (output.cellOffset != new Vector2Int(3, 1) || output.direction != Vector2Int.right || output.outputSlot != 1)
                throw new Exception("Output alignment: " + key);
        }
        var import = draft.GetBuildingImport(key);
        if (import.colliders.Length != json.Colliders.Length) throw new Exception("Collision shape count: " + key);
        if (AssetDatabase.LoadAssetAtPath<Sprite>(building.iconPath) == null) throw new Exception("Missing building icon.");
        ValidateGeometry(import.model, folder, key);
        var renderers = import.model.GetComponentsInChildren<MeshRenderer>(true);
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        string report = "IMPORT_OK\nKey=" + key + "\nModel=" + building.modelPath +
            "\nFootprint=3x3\nInput=(-1,1), direction=(1,0)\n" + (i == 0 ? "Output=none\n" : "Output=(3,1), direction=(1,0), slot=1\n") +
            "Renderers=" + renderers.Length + "\nTriangles=" + import.model.GetComponentsInChildren<MeshFilter>(true).Sum(f => f.sharedMesh.triangles.Length / 3) +
            "\nColliders=" + import.colliders.Length + "\nBounds=" + bounds + "\nIcon=" + building.iconPath +
            "\nGeometry=Blender evaluated mesh bounds match Unity within 0.002m\n" +
            (i == 0 ? "Behavior=Storage, slot_count=1; art placeholder, no power logic\n" :
                "Behavior=Processor, machine_type=" + Machines[i] + "; no recipes configured\n");
        File.WriteAllText(folder + "/unity-import-report.txt", report);
        Debug.Log(report);
    }
}
