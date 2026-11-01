using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class FactoryContentManagerWindow : EditorWindow
{
    [SerializeField] private FactoryContentDraft draft;
    [SerializeField] private int selectedTable;
    private Label status;
    private Button save;

    [MenuItem("Factory/内容管理器")]
    public static void Open() => GetWindow<FactoryContentManagerWindow>("Factory 内容管理器");

    private void OnEnable()
    {
        minSize = new Vector2(720, 400);
        Undo.undoRedoPerformed += Refresh;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        saveChangesMessage = "Factory 内容有未保存的修改。保存前会校验整组表，并重建数据库。";
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= Refresh;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
    }

    private void OnDestroy()
    {
        if (draft != null) DestroyImmediate(draft);
    }

    private void OnPlayModeChanged(PlayModeStateChange state) => Refresh();

    public void CreateGUI()
    {
        try
        {
            if (draft == null)
            {
                draft = CreateInstance<FactoryContentDraft>();
                draft.hideFlags = HideFlags.HideAndDontSave;
                draft.Load();
            }
            Refresh();
        }
        catch (Exception e) { ShowError(e); }
    }

    private void Refresh()
    {
        if (draft == null || draft.Tables.Count == 0) return;
        rootVisualElement.Clear();
        bool writable = !EditorApplication.isPlayingOrWillChangePlaymode;
        var toolbar = new Toolbar();
        save = new Button(() => SaveChanges()) { text = "保存并重建" };
        toolbar.Add(save);
        var discard = new Button(() =>
        {
            if (!draft.IsModified || EditorUtility.DisplayDialog("放弃修改", "丢弃所有未保存修改，并重新读取 CSV？", "放弃并重新读取", "取消"))
                DiscardChanges();
        }) { text = "放弃修改 / 重新读取" };
        toolbar.Add(discard);
        rootVisualElement.Add(toolbar);
        rootVisualElement.Add(new HelpBox(writable
            ? "阶段 1：源表草稿。修改只影响内存；保存时校验全部表。列表遵循源表行序。支持 Ctrl+Z / Ctrl+Y。"
            : "Play Mode 中禁止写入。退出 Play Mode 后可继续编辑草稿。", HelpBoxMessageType.Info));
        selectedTable = Mathf.Clamp(selectedTable, 0, draft.Tables.Count - 1);
        int editingTable = selectedTable;
        var choices = draft.Tables.Select(t => t.path.Substring(FactoryContentDraft.TableDirectory.Length + 1)).ToList();
        var selector = new PopupField<string>("数据表", choices, selectedTable);
        selector.RegisterValueChangedCallback(e => { selectedTable = choices.IndexOf(e.newValue); Refresh(); });
        rootVisualElement.Add(selector);
        status = new Label();
        status.style.whiteSpace = WhiteSpace.Normal;
        rootVisualElement.Add(status);
        var scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
        scroll.style.flexGrow = 1;
        rootVisualElement.Add(scroll);
        var table = FactoryCsvTable.Parse(draft.Tables[selectedTable].text);
        var header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        foreach (string column in table.Header)
        {
            var label = new Label(column);
            label.style.width = 180;
            label.style.flexShrink = 0;
            header.Add(label);
        }
        scroll.Add(header);
        for (int row = 0; row < table.RowCount; row++)
        {
            int rowIndex = row;
            var line = new VisualElement();
            line.style.flexDirection = FlexDirection.Row;
            foreach (string column in table.Header)
            {
                string name = column.Trim();
                // Track each edit immediately so closing while the field has focus
                // still sees hasUnsavedChanges and offers Save / Discard / Cancel.
                var field = new TextField { value = table.Get(row, name), multiline = true };
                field.style.width = 180;
                field.style.flexShrink = 0;
                field.SetEnabled(writable);
                field.RegisterValueChangedCallback(e =>
                {
                    draft.SetCell(editingTable, rowIndex, name, e.newValue);
                    UpdateModified();
                });
                line.Add(field);
            }
            scroll.Add(line);
        }
        UpdateModified();
    }

    private void UpdateModified()
    {
        hasUnsavedChanges = draft != null && draft.IsModified;
        save?.SetEnabled(hasUnsavedChanges && !EditorApplication.isPlayingOrWillChangePlaymode);
        if (status != null) status.text = hasUnsavedChanges ? "有未保存的修改" : "与源表一致";
    }

    public override void SaveChanges()
    {
        try
        {
            draft.Save();
            base.SaveChanges();
            UpdateModified();
        }
        catch (Exception e)
        {
            UpdateModified();
            ShowError(e);
            // Abort a window-close Save action as well as an ordinary save.
            throw;
        }
    }

    public override void DiscardChanges()
    {
        try
        {
            draft.Load();
            base.DiscardChanges();
            Refresh();
        }
        catch (Exception e) { ShowError(e); }
    }

    private void ShowError(Exception e)
    {
        if (status == null) { status = new Label(); rootVisualElement.Add(status); }
        status.text = e.Message + (e.InnerException == null ? "" : "\n" + e.InnerException.Message);
        Debug.LogError("Factory 内容管理器：" + e);
    }
}
