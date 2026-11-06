using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class FactoryModelMaterialWindow : EditorWindow
{
    [SerializeField] private GameObject model;
    [SerializeField] private Material selected;
    private UnityEditor.Editor materialEditor;
    private Vector2 scroll;
    private string error;

    public static void Open(GameObject model)
    {
        var window = GetWindow<FactoryModelMaterialWindow>("模型材质适配");
        window.model = model;
        window.minSize = new Vector2(540, 480);
        window.Show();
    }

    private void OnDisable() { if (materialEditor != null) DestroyImmediate(materialEditor); }
    private void Apply(Action action)
    {
        try { FactoryDatabaseCsvImporter.EnsureCanWrite(); action(); error = null; }
        catch (Exception e) { error = e.Message; }
    }
    private void OnGUI()
    {
        using var disabled = new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        model = (GameObject)EditorGUILayout.ObjectField("FBX", model, typeof(GameObject), false);
        string path = AssetDatabase.GetAssetPath(model);
        if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) { EditorGUILayout.EndScrollView(); return; }
        EditorGUILayout.HelpBox("映射按 FBX + 源材质名称保存。在 Assets/Art/Materials 及其子目录按同名匹配；多项同名请手动选择。再次导入保留已绑定材质。", MessageType.Info);
        var sources = FactoryModelPipeline.Sources(path);
        var mappings = importer.GetExternalObjectMap();
        foreach (var stale in mappings.Keys.Where(k => k.type == typeof(Material) && !sources.Contains(k)))
            EditorGUILayout.HelpBox("源材质已删除或改名：" + stale.name + "。请为新名称重新匹配。", MessageType.Warning);
        foreach (var source in sources)
        {
            mappings.TryGetValue(source, out var mapped);
            var current = mapped as Material;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var value = (Material)EditorGUILayout.ObjectField(source.name, current, typeof(Material), false);
            if (value != current) Apply(() => FactoryModelPipeline.Remap(path, source, value));
            EditorGUILayout.BeginHorizontal();
            if (current == null)
            {
                var matches = FactoryModelPipeline.Matches(source.name);
                if (matches.Length == 1 && GUILayout.Button("绑定同名材质")) Apply(() => FactoryModelPipeline.Remap(path, source, matches[0]));
                if (matches.Length > 1) EditorGUILayout.LabelField("存在多个同名材质，请手动选择");
                if (GUILayout.Button("创建 URP/Lit")) Apply(() => selected = FactoryModelPipeline.CreateMaterial(path, source));
                EditorGUILayout.LabelField("待匹配", GUILayout.Width(60));
            }
            else if (GUILayout.Button("调整此材质")) selected = current;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.HelpBox("URP/Lit 使用金属度贴图 R 通道、光滑度 A 通道（或基础色 A）。粗糙度需要反相并放入正确通道，不能直接当光滑度；首版不做通道转换或纹理打包。复杂 Blender 节点需在 Unity 调整。", MessageType.Warning);
        if (selected != null)
        {
            EditorGUILayout.LabelField("材质调整（直接保存 .mat，支持撤销）", EditorStyles.boldLabel);
            EditorGUILayout.ObjectField(selected, typeof(Material), false);
            UnityEditor.Editor.CreateCachedEditor(selected, typeof(MaterialEditor), ref materialEditor);
            materialEditor.OnInspectorGUI();
            if (GUILayout.Button("保存材质")) Apply(() => AssetDatabase.SaveAssetIfDirty(selected));
        }
        if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
        EditorGUILayout.EndScrollView();
    }
}
