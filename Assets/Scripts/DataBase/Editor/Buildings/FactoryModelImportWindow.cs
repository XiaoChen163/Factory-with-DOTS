using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class FactoryModelImportWindow : EditorWindow
{
    private FactoryContentDraft draft;
    private FactoryModelKind mode;
    private Action<FactoryModelKind, string, int> applied;
    private string source = "", itemKey = "", error = "";
    private FactoryBuildingJson json;
    private BuildingKind behavior = BuildingKind.Processor;
    private string machine = "";

    public static FactoryModelImportWindow Open(FactoryContentDraft draft, FactoryModelKind mode, Action<FactoryModelKind, string, int> applied)
    {
        var window = CreateInstance<FactoryModelImportWindow>();
        window.draft = draft; window.mode = mode; window.applied = applied;
        window.titleContent = new GUIContent("导入 Factory 模型");
        window.minSize = new Vector2(520, 440);
        window.ShowUtility();
        return window;
    }

    private void OnEnable() => EditorApplication.playModeStateChanged += Changed;
    private void OnDisable() => EditorApplication.playModeStateChanged -= Changed;
    private void Changed(PlayModeStateChange state) => CreateGUI();

    public void CreateGUI()
    {
        var root = rootVisualElement;
        root.Clear();
        root.style.paddingLeft = root.style.paddingRight = 12;
        root.style.paddingTop = root.style.paddingBottom = 12;
        if (draft == null || applied == null)
        {
            root.Add(new Label("导入会话已结束，请从内容管理器重新打开。"));
            root.Add(new Button(Close) { text = "关闭" });
            return;
        }
        var body = new ScrollView(); body.style.flexGrow = 1; root.Add(body);
        var modeField = new PopupField<string>("导入模式", new[] { "建筑", "物品" }.ToList(), mode == FactoryModelKind.Building ? 0 : 1) { name = "model-import-mode" };
        modeField.RegisterValueChangedCallback(e => { mode = e.newValue == "建筑" ? FactoryModelKind.Building : FactoryModelKind.Item; error = ""; CreateGUI(); });
        body.Add(modeField);
        body.Add(new HelpBox(mode == FactoryModelKind.Building
            ? "建筑：配对 JSON 提供占格、端口和碰撞，FBX 提供模型。应用时同时更新四部分；已有建筑的名称、行为和游戏属性保留。"
            : "物品：只导入 FBX 并写入物品表，不生成占格、端口、碰撞体或刚体。相同 Key 更新模型并保留其他属性。", HelpBoxMessageType.Info));
        if (mode == FactoryModelKind.Building)
        {
            body.Add(new Button(() => Try(() =>
            {
                string path = EditorUtility.OpenFilePanel("选择建筑 .factory.json", "", "json");
                if (string.IsNullOrEmpty(path)) return;
                json = FactoryBuildingJson.Read(path);
                string paired = FactoryModelPipeline.Pair(new FactoryBuildingImportData { sourceJsonPath = json.SourcePath, modelFile = json.Building.model_file });
                source = paired ?? "";
            })) { text = "选择建筑 JSON…", name = "model-import-json" });
            if (json != null)
            {
                body.Add(new Label($"建筑 Key：{json.Building.key}\n占格：{json.Building.width} × {json.Building.length} · 端口：{json.Ports?.Length ?? 0} · 碰撞体：{json.Colliders?.Length ?? 0}"));
                bool exists = draft.GetBuildings().Any(b => b.key == json.Building.key);
                if (!exists)
                {
                    var kind = new EnumField("新建筑行为", behavior) { name = "model-import-behavior" };
                    kind.RegisterValueChangedCallback(e => { behavior = (BuildingKind)e.newValue; CreateGUI(); }); body.Add(kind);
                    if (behavior == BuildingKind.Processor)
                    {
                        var machines = draft.GetMachineTypes();
                        if (!machines.Contains(machine)) machine = machines.FirstOrDefault() ?? "";
                        if (machines.Count > 0)
                        {
                            var machineField = new PopupField<string>("加工类型", machines, machine);
                            machineField.RegisterValueChangedCallback(e => machine = e.newValue); body.Add(machineField);
                        }
                    }
                }
            }
        }
        else
        {
            var key = new TextField("物品 Key") { value = itemKey, name = "model-import-item-key" };
            key.RegisterValueChangedCallback(e => itemKey = e.newValue); body.Add(key);
        }
        var file = new TextField("FBX 文件") { value = source, isReadOnly = true, name = "model-import-source" }; body.Add(file);
        var model = new ObjectField("项目内 FBX") { objectType = typeof(GameObject), allowSceneObjects = false, value = source.StartsWith("Assets/", StringComparison.Ordinal) ? AssetDatabase.LoadAssetAtPath<GameObject>(source) : null };
        model.RegisterValueChangedCallback(e => Try(() => SetSource(e.newValue == null ? "" : AssetDatabase.GetAssetPath(e.newValue)))); body.Add(model);
        body.Add(new Button(() => Try(() =>
        {
            string path = EditorUtility.OpenFilePanel("选择 FBX", "", "fbx");
            if (!string.IsNullOrEmpty(path)) SetSource(path);
        })) { text = "选择外部 FBX…", name = "model-import-fbx" });
        var errors = new Label(error) { name = "model-import-error" }; errors.style.whiteSpace = WhiteSpace.Normal; body.Add(errors);
        body.Add(new Button(() =>
        {
            try
            {
                FactoryDatabaseCsvImporter.EnsureCanWrite();
                if (mode == FactoryModelKind.Item)
                {
                    int row = draft.ImportItemModel(itemKey, source);
                    applied(mode, itemKey, row);
                }
                else
                {
                    if (json == null) throw new InvalidDataException("请先选择建筑 JSON。");
                    string key = draft.ImportBuildingModel(json, source, behavior, machine);
                    applied(mode, key, -1);
                }
                Close();
            }
            catch (Exception e) { error = e.Message; CreateGUI(); }
        }) { text = "应用到内容草稿", name = "model-import-apply" });
        body.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);
        root.Add(new HelpBox("FBX 和纹理导入立即写入资产。表格修改可撤销，点击内容管理器“保存并重建”后统一写入 CSV。", HelpBoxMessageType.Info));
        root.Add(new Button(Close) { text = "取消导入" });
    }

    private void SetSource(string path)
    {
        if (!string.IsNullOrEmpty(path)) FactoryModelPipeline.RequireFbx(path);
        source = path;
        if (mode == FactoryModelKind.Item && string.IsNullOrWhiteSpace(itemKey)) itemKey = Path.GetFileNameWithoutExtension(path).Replace(' ', '_');
    }
    private void Try(Action action)
    {
        try { action(); error = ""; } catch (Exception e) { error = e.Message; }
        CreateGUI();
    }
}
