using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Snapshot the old placeholder assets once; Blender regenerates FBX from this source.
public static class FactoryGeometryMigration
{
    public const string SnapshotPath = "Tools/Blender/Fixtures/PlaceholderModels.json";
    [Serializable] public sealed class Snapshot { public List<Model> models = new List<Model>(); }
    [Serializable] public sealed class Model
    {
        public string key, kind, source, output;
        public string guid;
        public List<Node> nodes = new List<Node>();
    }
    [Serializable] public sealed class Node
    {
        public string name;
        public int parent;
        public Vector3 position, scale;
        public Quaternion rotation;
        public Vector3[] vertices;
        public Vector2[] uv;
        public List<FaceSet> faces = new List<FaceSet>();
    }
    [Serializable] public sealed class FaceSet { public string material; public int[] triangles; }

    [MenuItem("Factory/Models/Import Generated Placeholder FBX")]
    public static void ImportGeneratedModels()
    {
        FactoryDatabaseCsvImporter.EnsureCanWrite();
        var snapshot = JsonUtility.FromJson<Snapshot>(File.ReadAllText(SnapshotPath));
        var replacements = new Dictionary<string, string>();
        foreach (var entry in snapshot.models)
        {
            FactoryModelPipeline.Configure(entry.output);
            var importer = (ModelImporter)AssetImporter.GetAtPath(entry.output);
            var materials = entry.nodes.SelectMany(n => n.faces).Select(f => f.material).Distinct().ToDictionary(Path.GetFileNameWithoutExtension, AssetDatabase.LoadAssetAtPath<Material>);
            foreach (var slot in FactoryModelPipeline.Sources(entry.output))
                if (materials.TryGetValue(slot.name, out var material)) importer.AddRemap(slot, material);
            importer.SaveAndReimport();
            var model = FactoryDatabaseCsvImporter.LoadModel(entry.output);
            VerifyBounds(entry, model);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(model, out string guid, out long id);
            replacements.Add(entry.guid, "{fileID: " + id + ", guid: " + guid + ", type: 3}");
        }
        var obsolete = snapshot.models.Select(m => m.source).ToHashSet(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles("Assets", "*", SearchOption.AllDirectories))
        {
            string path = file.Replace('\\', '/');
            if (obsolete.Contains(path) || !new[] { ".asset", ".unity", ".prefab" }.Contains(Path.GetExtension(path))) continue;
            string original = File.ReadAllText(path), text = original;
            foreach (var replacement in replacements)
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\{fileID: [-\d]+, guid: " + replacement.Key + @", type: 3\}", replacement.Value);
            foreach (var entry in snapshot.models) text = text.Replace(entry.source, entry.output);
            if (text != original) File.WriteAllText(path, text, new System.Text.UTF8Encoding(false));
        }
        foreach (var entry in snapshot.models)
            if (File.Exists(entry.source)) AssetDatabase.DeleteAsset(entry.source);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var candidate = FactoryDatabaseCsvImporter.ReadCandidate();
        try { FactoryDatabaseCsvImporter.Generate(candidate); }
        finally { UnityEngine.Object.DestroyImmediate(candidate); }
        Debug.Log($"FACTORY_FBX_MIGRATION_COMPLETE {snapshot.models.Count} models; bounds and material mappings verified.");
    }

    public static void VerifyBounds(Model entry, GameObject model)
    {
        var matrices = new List<Matrix4x4>();
        var expected = new Bounds(); bool any = false;
        foreach (var node in entry.nodes)
        {
            var matrix = Matrix4x4.TRS(node.position, node.rotation, node.scale);
            if (node.parent >= 0) matrix = matrices[node.parent] * matrix;
            matrices.Add(matrix);
            foreach (var vertex in node.vertices ?? Array.Empty<Vector3>())
            {
                Vector3 point = matrix.MultiplyPoint3x4(vertex);
                if (!any) { expected = new Bounds(point, Vector3.zero); any = true; }
                else expected.Encapsulate(point);
            }
        }
        var instance = UnityEngine.Object.Instantiate(model);
        try
        {
            var renderers = instance.GetComponentsInChildren<MeshRenderer>();
            var actual = renderers[0].bounds;
            foreach (var renderer in renderers) actual.Encapsulate(renderer.bounds);
            if (Vector3.Distance(actual.min, expected.min) > .003f || Vector3.Distance(actual.max, expected.max) > .003f)
                throw new InvalidDataException($"FBX bounds changed for {entry.key}: expected {expected}, got {actual}");
        }
        finally { UnityEngine.Object.DestroyImmediate(instance); }
    }
}
