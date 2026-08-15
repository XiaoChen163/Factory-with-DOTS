using System.Linq;
using UnityEditor;
using UnityEngine;

public static class RampAssetBuilder
{
    public const string MeshPath = "Assets/Art/Mesh/Ramp_45_1x1x1.obj";
    public const string PrefabPath = "Assets/Prefabs/Buildings/RampFoundation.prefab";
    public const string HalfPrefabPath =
        "Assets/Prefabs/Buildings/RampFoundationHalf.prefab";
    public const string QuarterPrefabPath =
        "Assets/Prefabs/Buildings/RampFoundationQuarter.prefab";

    [MenuItem("Factory/Assets/Rebuild Ramp Foundation Prefab")]
    public static void RebuildRampFoundationPrefab()
    {
        AssetDatabase.ImportAsset(MeshPath, ImportAssetOptions.ForceSynchronousImport);
        Mesh mesh = AssetDatabase.LoadAllAssetsAtPath(MeshPath)
            .OfType<Mesh>()
            .FirstOrDefault();
        if (mesh == null)
            throw new System.InvalidOperationException(
                $"No mesh was imported from '{MeshPath}'.");

        Material material = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Art/Materials/Floor.mat");
        SaveRampPrefab(mesh, material, PrefabPath, "RampFoundation", 1f);
        SaveRampPrefab(mesh, material, HalfPrefabPath, "RampFoundationHalf", 0.5f);
        SaveRampPrefab(mesh, material, QuarterPrefabPath, "RampFoundationQuarter", 0.25f);

        AssetDatabase.SaveAssets();
        Debug.Log(
            "[Factory Assets] Rebuilt 1/1, 1/2 and 1/4 ramp foundation prefabs " +
            $"from {MeshPath}.");
    }

    private static void SaveRampPrefab(
        Mesh mesh,
        Material material,
        string path,
        string name,
        float yScale)
    {
        GameObject root = new GameObject(name);
        try
        {
            root.transform.localScale = new Vector3(1f, yScale, 1f);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            root.AddComponent<MeshRenderer>().sharedMaterial = material;
            MeshCollider collider = root.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = true;
            if (!PrefabUtility.SaveAsPrefabAsset(root, path))
                throw new System.InvalidOperationException(
                    $"Could not save '{path}'.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

    }
}
