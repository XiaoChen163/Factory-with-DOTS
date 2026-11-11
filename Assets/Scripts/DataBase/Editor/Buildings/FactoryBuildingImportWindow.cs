using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class FactoryBuildingImportWindow : EditorWindow
{
    private FactoryBuildingJson json;
    private Action<bool, bool, bool> apply;
    private bool isNew;
    private VisualElement controls;

    public static FactoryBuildingImportWindow Open(FactoryBuildingJson json, bool isNew, Action<bool, bool, bool> apply)
    {
        var window = CreateInstance<FactoryBuildingImportWindow>();
        window.json = json;
        window.isNew = isNew;
        window.apply = apply;
        window.titleContent = new GUIContent("导入 Factory 建筑");
        window.minSize = new Vector2(480, 320);
        window.ShowUtility();
        return window;
    }

    private void OnEnable() => EditorApplication.playModeStateChanged += OnPlayModeChanged;
    private void OnDisable() => EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    private void OnPlayModeChanged(PlayModeStateChange state) => UpdateWritable();
    private void UpdateWritable() => controls?.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);

    public void CreateGUI()
    {
        var root = rootVisualElement;
        root.Clear();
        root.style.paddingLeft = root.style.paddingRight = 12;
        root.style.paddingTop = root.style.paddingBottom = 12;
        if (json == null || apply == null)
        {
            root.Add(new Label("导入预览已失效，请关闭此窗口并重新选择 JSON。"));
            root.Add(new Button(Close) { text = "关闭" });
            return;
        }
        var scroll = new ScrollView { name = "building-import-preview" };
        scroll.style.flexGrow = 1;
        root.Add(scroll);
        scroll.Add(new Label($"建筑：{json.Building.key}"));
        scroll.Add(new Label($"模型配对：{json.Building.model_file ?? "未提供"}"));
        scroll.Add(new HelpBox("只更新勾选部分。已有建筑默认不覆盖；缺失部分保持原样，空数组将清空。名称、图标和游戏属性保持原样。应用后仍需在内容管理器中保存。", HelpBoxMessageType.Info));
        controls = new VisualElement();
        scroll.Add(controls);
        var footprint = new Toggle(json.HasFootprint ? $"占格 {json.Building.width}×{json.Building.length}" : "占格（未提供）") { value = isNew && json.HasFootprint, name = "import-footprint" };
        footprint.SetEnabled(json.HasFootprint);
        controls.Add(footprint);
        var ports = new Toggle(json.HasPorts ? $"端口 {json.Ports.Length} 个" : "端口（未提供）") { value = isNew && json.HasPorts, name = "import-ports" };
        ports.SetEnabled(json.HasPorts);
        controls.Add(ports);
        var colliders = new Toggle(json.HasColliders ? $"碰撞代理 {json.Colliders.Length} 个" : "碰撞代理（未提供）") { value = isNew && json.HasColliders, name = "import-colliders" };
        colliders.SetEnabled(json.HasColliders);
        controls.Add(colliders);
        if (json.HasColliders)
        {
            var preview = new Label(string.Join("、", json.Colliders.Select(c => c.name + " (" + c.type + ")")));
            preview.style.whiteSpace = WhiteSpace.Normal;
            scroll.Add(preview);
        }
        var error = new Label { name = "import-error" };
        error.style.whiteSpace = WhiteSpace.Normal;
        scroll.Add(error);
        controls.Add(new Button(() =>
        {
            try
            {
                FactoryDatabaseEditor.EnsureCanWrite();
                apply(footprint.value, ports.value, colliders.value);
                Close();
            }
            catch (Exception e) { error.text = e.Message; }
        }) { text = "应用到草稿", name = "import-apply" });
        root.Add(new Button(Close) { text = "取消导入", name = "import-cancel" });
        UpdateWritable();
    }
}
