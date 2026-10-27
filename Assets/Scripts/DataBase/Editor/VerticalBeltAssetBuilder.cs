using UnityEditor;
using UnityEngine;

public static class VerticalBeltAssetBuilder
{
    [MenuItem("Factory/Assets/Rebuild Vertical Belt Prefabs")]
    public static void Rebuild()
    {
        Mesh triangle = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Art/Mesh/VerticalBeltArrow.asset");
        bool createMesh = triangle == null;
        if (createMesh) triangle = new Mesh { name = "Vertical Belt Arrow" };
        triangle.Clear();
        triangle.vertices = new[] {
            new Vector3(-.09f,-.10f,0), new Vector3(.09f,-.10f,0), new Vector3(0,.13f,0),
            new Vector3(-.09f,-.10f,0), new Vector3(.09f,-.10f,0), new Vector3(0,.13f,0) };
        triangle.triangles = new[] { 0,1,2,5,4,3 };
        triangle.RecalculateNormals();
        triangle.RecalculateBounds();
        if (createMesh) AssetDatabase.CreateAsset(triangle, "Assets/Art/Mesh/VerticalBeltArrow.asset");
        else EditorUtility.SetDirty(triangle);
        string[] kinds = { "Entry", "Exit", "Segment" };
        for (int tier = 1; tier <= 4; tier++)
        {
            string beltName = tier == 1 ? "Belt" : "BeltMk" + tier;
            GameObject belt = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Buildings/" + beltName + ".prefab");
            Material material = belt.transform.Find("Connection Edge East").GetComponent<MeshRenderer>().sharedMaterial;
            foreach (string kind in kinds)
            {
                GameObject root = new GameObject("VerticalBelt" + kind + "Mk" + tier);
                try
                {
                    GameObject body = GameObject.CreatePrimitive(kind == "Segment" ? PrimitiveType.Cylinder : PrimitiveType.Cube);
                    Object.DestroyImmediate(body.GetComponent<Collider>());
                    body.name = "Body";
                    body.transform.SetParent(root.transform, false);
                    body.transform.localPosition = new Vector3(0,.5f,0);
                    body.transform.localScale = kind == "Segment" ? new Vector3(.22f,.5f,.22f) : new Vector3(.75f,.75f,.75f);
                    body.GetComponent<MeshRenderer>().sharedMaterial = material;
                    if (kind != "Segment")
                    {
                        GameObject port = new GameObject(kind == "Entry" ? "Input Marker" : "Output Marker");
                        port.transform.SetParent(root.transform, false);
                        port.transform.localPosition = new Vector3(kind == "Entry" ? -.39f : .39f,.89f,0);
                        port.transform.localRotation = Quaternion.AngleAxis(-90, Vector3.right) * Quaternion.AngleAxis(-90, Vector3.forward);
                        port.transform.localScale = Vector3.one * .7f;
                        port.AddComponent<MeshFilter>().sharedMesh = triangle;
                        port.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(
                            "Assets/Art/Materials/" + (kind == "Entry" ? "Green" : "Orange") + ".mat");
                    }
                    if (kind == "Segment")
                    {
                        for (int side = 0; side < 4; side++)
                        {
                            GameObject arrow = new GameObject("Direction Arrow");
                            arrow.transform.SetParent(root.transform, false);
                            arrow.transform.localRotation = Quaternion.Euler(0,side*90,0);
                            arrow.transform.localPosition = Quaternion.Euler(0,side*90,0) * new Vector3(0,.5f,.12f);
                            arrow.AddComponent<MeshFilter>().sharedMesh = triangle;
                            MeshRenderer renderer = arrow.AddComponent<MeshRenderer>();
                            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/White.mat") ?? material;
                            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                            renderer.receiveShadows = false;
                        }
                    }
                    if (!PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Buildings/" + root.name + ".prefab"))
                        throw new System.InvalidOperationException("Could not save vertical belt prefab.");
                }
                finally { Object.DestroyImmediate(root); }
            }
        }
        AssetDatabase.SaveAssets();
        FactoryIconBaker.BakeAll(false);
        Debug.Log("[Vertical Belt Assets] Completed 12 prefabs and database rebuild.");
    }
}
