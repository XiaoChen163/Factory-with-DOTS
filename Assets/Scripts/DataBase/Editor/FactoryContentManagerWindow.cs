using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class FactoryContentManagerWindow : EditorWindow
{
    [SerializeField] private FactoryContentDraft draft;
    [SerializeField] private int selectedTab = 1;
    [SerializeField] private int selectedItemRow;
    [SerializeField] private int selectedRecipeRow;
    [SerializeField] private string search = "";
    private Label status;
    private Button save, delete;
    private ScrollView list, detail;
    private VisualElement warnings;
    private Dictionary<string, GameObject> prefabs;
    private Dictionary<string, Sprite> icons;
    private bool Writable => !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Factory/内容管理器")]
    public static void Open() => GetWindow<FactoryContentManagerWindow>("Factory 内容管理器");

    private void OnEnable()
    {
        minSize = new Vector2(900, 500);
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
        if (buildingImportWindow != null) buildingImportWindow.Close();
        if (draft != null) DestroyImmediate(draft);
    }
    private void OnPlayModeChanged(PlayModeStateChange state) => Refresh();

    public void CreateGUI()
    {
        Run(() =>
        {
            if (draft == null)
            {
                draft = CreateInstance<FactoryContentDraft>();
                draft.hideFlags = HideFlags.HideAndDontSave;
                draft.Load();
            }
            Refresh();
        });
    }

    private void Refresh()
    {
        if (draft == null || draft.Tables.Count == 0) return;
        rootVisualElement.Clear();
        prefabs = FactoryDatabaseCsvImporter.BuildPrefabIndex(FactoryDatabaseCsvImporter.ItemPrefabDirectory);
        icons = FactoryDatabaseCsvImporter.BuildSpriteIndex(FactoryDatabaseCsvImporter.ItemIconDirectory);
        var tabs = new Toolbar();
        string[] labels = { "建筑", "物品", "配方" };
        string[] names = { "tab-buildings", "tab-items", "tab-recipes" };
        for (int i = 0; i < labels.Length; i++)
        {
            int tab = i;
            var toggle = new ToolbarToggle { text = labels[i], name = names[i], value = selectedTab == i };
            toggle.RegisterValueChangedCallback(_ => { selectedTab = tab; search = ""; Refresh(); });
            tabs.Add(toggle);
        }
        rootVisualElement.Add(tabs);
        var toolbar = new Toolbar();
        var filter = new ToolbarSearchField { name = "content-search", value = search };
        filter.style.flexGrow = 1;
        filter.RegisterValueChangedCallback(e => { search = e.newValue; RefreshList(); });
        toolbar.Add(filter);
        var add = new Button(() => Run(() =>
        {
            if (selectedTab == 0) { selectedBuildingKey = draft.AddBuilding(string.IsNullOrWhiteSpace(newBuildingKey) ? null : newBuildingKey); gridExpanded = true; }
            else if (selectedTab == 1) selectedItemRow = draft.AddItem();
            else selectedRecipeRow = draft.AddRecipe();
            search = "";
            Refresh();
        })) { text = "新增", name = "content-add" };
        add.SetEnabled(Writable);
        toolbar.Add(add);
        if (selectedTab == 0) AddBuildingToolbar(toolbar);
        delete = new Button(() => Run(DeleteSelected)) { text = "删除", name = "content-delete" };
        toolbar.Add(delete);
        save = new Button(() => { try { SaveChanges(); } catch { /* Error remains in the window. */ } })
            { text = "保存并重建", name = "content-save" };
        toolbar.Add(save);
        toolbar.Add(new Button(() =>
        {
            if (!draft.IsModified || EditorUtility.DisplayDialog("放弃修改", "丢弃所有未保存修改，并重新读取 CSV？", "放弃并重新读取", "取消")) DiscardChanges();
        }) { text = "放弃 / 重新读取" });
        rootVisualElement.Add(toolbar);
        if (!Writable) rootVisualElement.Add(new HelpBox("Play Mode 中禁止写入。退出后可继续处理草稿。", HelpBoxMessageType.Info));
        status = new Label { name = "content-status" };
        status.style.whiteSpace = WhiteSpace.Normal;
        status.style.marginLeft = 6;
        rootVisualElement.Add(status);
        var body = new VisualElement();
        body.style.flexDirection = FlexDirection.Row;
        body.style.flexGrow = 1;
        body.style.minHeight = 0;
        list = new ScrollView { name = "content-list" };
        list.style.width = 330;
        list.style.flexShrink = 0;
        list.style.borderRightWidth = 1;
        list.style.borderRightColor = new Color(.35f, .35f, .35f);
        detail = new ScrollView { name = "content-detail" };
        detail.style.flexGrow = 1;
        detail.style.paddingLeft = 12;
        detail.style.paddingRight = 12;
        body.Add(list);
        body.Add(detail);
        rootVisualElement.Add(body);
        RefreshList();
        DrawDetail();
        UpdateModified();
    }

    private void RefreshList()
    {
        if (selectedTab == 0) { RefreshBuildingList(); return; }
        if (list == null) return;
        list.Clear();
        if (selectedTab == 1)
        {
            foreach (var item in draft.GetItems(search))
            {
                var button = new Button(() => { selectedItemRow = item.row; DrawDetail(); RefreshList(); }) { name = "item-row-" + item.row };
                AddIdentityPreview(button, ItemIcon(item), item.name, item.key);
                MarkSelected(button, item.row == selectedItemRow);
                list.Add(button);
            }
        }
        else
        {
            foreach (var recipe in draft.GetRecipes(search))
            {
                var button = new Button(() => { selectedRecipeRow = recipe.row; DrawDetail(); RefreshList(); }) { name = "recipe-row-" + recipe.row };
                button.style.flexDirection = FlexDirection.Column;
                button.style.alignItems = Align.Stretch;
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                button.style.paddingLeft = button.style.paddingRight = 6;
                button.style.paddingTop = button.style.paddingBottom = 4;
                var header = new VisualElement();
                header.style.flexDirection = FlexDirection.Row;
                header.style.alignItems = Align.FlexStart;
                var identity = ListLabel(recipe.key);
                identity.style.flexGrow = 1;
                identity.style.minWidth = 0;
                header.Add(identity);
                var duration = new Label($"{recipe.duration.ToString("G", CultureInfo.InvariantCulture)} 秒");
                duration.style.unityTextAlign = TextAnchor.UpperRight;
                duration.style.flexShrink = 0;
                duration.style.marginLeft = 8;
                header.Add(duration);
                button.Add(header);
                button.Add(ListLabel(recipe.machine));
                var flow = Row();
                AddIngredientPreview(flow, recipe.key, false);
                flow.Add(new Label(" → "));
                AddIngredientPreview(flow, recipe.key, true);
                button.Add(flow);
                MarkSelected(button, recipe.row == selectedRecipeRow);
                list.Add(button);
            }
        }
        if (list.childCount == 0) list.Add(new Label("没有匹配的内容"));
        UpdateDelete();
    }

    private void AddIngredientPreview(VisualElement parent, string recipe, bool output)
    {
        var items = draft.GetItems();
        foreach (var ingredient in draft.GetIngredients(recipe, output))
        {
            var item = items.FirstOrDefault(i => i.key == ingredient.itemKey);
            parent.Add(Icon(ItemIcon(item), 22, item == null ? ingredient.itemKey : item.name + " (" + item.key + ")"));
            parent.Add(new Label("×" + ingredient.count));
        }
    }

    private void DrawDetail()
    {
        detail.Clear();
        warnings = new VisualElement { name = "entry-warnings" };
        detail.Add(warnings);
        if (selectedTab == 0)
        {
            var buildings = draft.GetBuildings();
            var building = buildings.FirstOrDefault(b => b.key == selectedBuildingKey) ?? buildings.FirstOrDefault();
            if (building == null) { detail.Add(new Label("新增建筑，或导入 .factory.json 创建草稿。")); UpdateDelete(); return; }
            selectedBuildingKey = building.key;
            DrawBuilding(building);
        }
        else if (selectedTab == 1)
        {
            var items = draft.GetItems();
            selectedItemRow = Mathf.Clamp(selectedItemRow, 0, Math.Max(0, items.Count - 1));
            var item = items.FirstOrDefault(i => i.row == selectedItemRow);
            if (item == null) { detail.Add(new Label("点击“新增”创建物品。")); UpdateDelete(); return; }
            DrawItem(item);
        }
        else
        {
            var recipes = draft.GetRecipes();
            selectedRecipeRow = Mathf.Clamp(selectedRecipeRow, 0, Math.Max(0, recipes.Count - 1));
            var recipe = recipes.FirstOrDefault(r => r.row == selectedRecipeRow);
            if (recipe == null) { detail.Add(new Label("点击“新增”创建配方。")); UpdateDelete(); return; }
            DrawRecipe(recipe);
        }
        detail.SetEnabled(Writable);
        RefreshWarnings();
        UpdateDelete();
    }

    private void DrawItem(FactoryContentItem item)
    {
        detail.Add(Heading("物品详情"));
        // Keep incomplete numeric text in the draft while typing (e.g. an ID's
        // first digit may temporarily collide). Validation still runs before save.
        Bind(detail, new TextField("ID") { name = "item-id", value = draft.ReadTable(FactoryContentDraft.ItemsPath).Get(item.row, "id") },
            value => WriteDraftText(FactoryContentDraft.ItemsPath, item.row, "id", value));
        Bind(detail, new TextField("Key") { name = "item-key", value = item.key }, value => draft.SetItemValue(item.row, "key", value));
        Bind(detail, new TextField("名称 / name_key") { name = "item-name", value = item.name, multiline = true }, value => draft.SetItemValue(item.row, "name_key", value));
        Bind(detail, new IntegerField("最大堆叠") { name = "item-max-stack", value = item.maxStack }, value => draft.SetItemValue(item.row, "max_stack", Number(value)));
        Bind(detail, new EnumField("分类", item.category) { name = "item-category" }, value => draft.SetItemValue(item.row, "category", value.ToString()));
        var prefab = new ObjectField("Prefab") { name = "item-prefab", objectType = typeof(GameObject), allowSceneObjects = false, value = ItemPrefab(item) };
        Bind(detail, prefab, value => draft.SetItemValue(item.row, "prefab_key", FactoryItemAssetBindings.PrefabKey(value as GameObject)));
        var icon = new ObjectField("图标（可留空）") { name = "item-icon", objectType = typeof(Sprite), allowSceneObjects = false, value = ExplicitIcon(item) };
        Bind(detail, icon, value => draft.SetItemValue(item.row, "icon_key", FactoryItemAssetBindings.IconKey(value as Sprite)));
        detail.Add(new HelpBox("Prefab 位于 Assets/Prefabs/Items；图标位于 Assets/Art/Icons/Items，按文件名绑定。图标留空时沿用 Prefab Key 的默认图标。name_key 保持现有语义。", HelpBoxMessageType.Info));
    }

    private void DrawRecipe(FactoryContentRecipe recipe)
    {
        detail.Add(Heading("配方详情"));
        Bind(detail, new TextField("Key") { name = "recipe-key", value = recipe.key }, value => draft.SetRecipeValue(recipe.row, "key", value));
        Bind(detail, new TextField("生产时间（秒）") { name = "recipe-duration", value = draft.ReadTable(FactoryContentDraft.RecipesPath).Get(recipe.row, "duration_seconds") },
            value => WriteDraftText(FactoryContentDraft.RecipesPath, recipe.row, "duration_seconds", value));
        var machines = draft.GetMachineTypes();
        if (!machines.Contains(recipe.machine)) machines.Add(recipe.machine);
        var machine = new PopupField<string>("加工类型", machines, recipe.machine) { name = "recipe-machine" };
        Bind(detail, machine, value => draft.SetRecipeValue(recipe.row, "machine_type_key", value));
        var newMachine = Row();
        var key = new TextField("新加工类型") { name = "new-machine-key" };
        key.style.flexGrow = 1;
        newMachine.Add(key);
        newMachine.Add(new Button(() => Run(() =>
        {
            draft.AddMachineType(key.value);
            draft.SetRecipeValue(recipe.row, "machine_type_key", key.value);
            RefreshList(); DrawDetail(); UpdateModified();
        })) { text = "新增并选择", name = "machine-add" });
        detail.Add(newMachine);
        DrawIngredients(recipe.row, false);
        DrawIngredients(recipe.row, true);
    }

    // Keep the active scalar field and caret alive while updating previews and warnings.
    private void Bind<T>(VisualElement parent, BaseField<T> field, Action<T> edit)
    {
        field.RegisterValueChangedCallback(e =>
        {
            if (!Run(() => { edit(e.newValue); RefreshList(); RefreshWarnings(); UpdateModified(); }))
                field.SetValueWithoutNotify(e.previousValue);
        });
        parent.Add(field);
    }

    private void RefreshWarnings()
    {
        if (warnings == null) return;
        warnings.Clear();
        if (selectedTab == 1)
        {
            var item = draft.GetItems().FirstOrDefault(i => i.row == selectedItemRow);
            if (item == null) return;
            var overflow = draft.GetItemReferences(item.key, item.maxStack);
            if (overflow.Count > 0) warnings.Add(new HelpBox("以下配方数量超过最大堆叠，请调整后再保存：\n" + string.Join("\n", overflow), HelpBoxMessageType.Warning));
            if (ItemPrefab(item) == null) warnings.Add(new HelpBox("请选择有效的物品 Prefab 后再保存。", HelpBoxMessageType.Warning));
            if (!string.IsNullOrEmpty(item.iconKey) && ExplicitIcon(item) == null) warnings.Add(new HelpBox("指定图标不存在，请重新选择。", HelpBoxMessageType.Warning));
        }
        FactoryDatabaseAsset candidate = null;
        try { candidate = draft.ReadCandidate(); }
        catch (Exception e) { warnings.Add(new HelpBox("整组草稿校验：" + e.Message, HelpBoxMessageType.Warning)); }
        finally { if (candidate != null) DestroyImmediate(candidate); }
    }

    private void DeleteSelected()
    {
        if (selectedTab == 0) draft.DeleteBuilding(selectedBuildingKey);
        else if (selectedTab == 1) draft.DeleteItem(selectedItemRow);
        else if (selectedTab == 2) draft.DeleteRecipe(selectedRecipeRow);
        RefreshList(); DrawDetail(); RefreshList(); UpdateModified();
    }

    private void UpdateDelete()
    {
        delete?.SetEnabled(Writable && (selectedTab == 0 ? draft.GetBuildings().Count > 0 : selectedTab == 1 ? draft.GetItems().Count > 0 : draft.GetRecipes().Count > 0));
    }

    private void UpdateModified()
    {
        hasUnsavedChanges = draft != null && draft.IsModified;
        save?.SetEnabled(hasUnsavedChanges && Writable);
        if (status != null) status.text = hasUnsavedChanges ? "有未保存的修改 · 保存前会校验所有内容" : "与源表一致";
        UpdateDelete();
    }

    public override void SaveChanges()
    {
        try { draft.Save(); base.SaveChanges(); UpdateModified(); RefreshWarnings(); }
        catch (Exception e) { UpdateModified(); ShowError(e); throw; }
    }

    public override void DiscardChanges() { Run(() => { draft.Load(); base.DiscardChanges(); Refresh(); }); }
    private bool Run(Action action) { try { action(); return true; } catch (Exception e) { ShowError(e); return false; } }

    private void ShowError(Exception e)
    {
        if (status == null) { status = new Label(); rootVisualElement.Add(status); }
        status.text = e.Message + (e.InnerException == null ? "" : "\n" + e.InnerException.Message);
    }

    private GameObject ItemPrefab(FactoryContentItem item) => item != null && prefabs.TryGetValue(FactoryDatabaseCsvImporter.NormalizeKey(item.prefabKey), out var value) ? value : null;
    private void WriteDraftText(string path, int row, string column, string value) =>
        draft.SetCell(draft.Tables.ToList().FindIndex(t => t.path == FactoryContentDraft.TableDirectory + "/" + path), row, column, value);
    private Sprite ExplicitIcon(FactoryContentItem item) => item != null && icons.TryGetValue(FactoryDatabaseCsvImporter.NormalizeKey(item.iconKey), out var value) ? value : null;
    private Sprite ItemIcon(FactoryContentItem item) => item != null && icons.TryGetValue(FactoryDatabaseCsvImporter.NormalizeKey(string.IsNullOrEmpty(item.iconKey) ? item.prefabKey : item.iconKey), out var value) ? value : null;
    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static Label ListLabel(string value)
    {
        var label = new Label(value);
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        label.style.overflow = Overflow.Hidden;
        label.style.textOverflow = TextOverflow.Ellipsis;
        label.style.whiteSpace = WhiteSpace.NoWrap;
        return label;
    }
    private static void AddIdentityPreview(Button button, Sprite icon, string name, string key)
    {
        button.style.flexDirection = FlexDirection.Row;
        button.style.alignItems = Align.Center;
        button.style.justifyContent = Justify.FlexStart;
        button.style.unityTextAlign = TextAnchor.MiddleLeft;
        button.style.minHeight = 44;
        button.style.paddingLeft = button.style.paddingRight = 6;
        button.style.paddingTop = button.style.paddingBottom = 4;
        button.Add(Icon(icon));
        var text = new VisualElement();
        text.style.flexGrow = 1;
        text.style.minWidth = 0;
        text.style.marginLeft = 8;
        text.style.alignItems = Align.Stretch;
        text.Add(ListLabel(name));
        text.Add(ListLabel(key));
        button.Add(text);
    }
    private static VisualElement Row() { var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.flexWrap = Wrap.Wrap; row.style.alignItems = Align.Center; return row; }
    private static Label Heading(string text) { var label = new Label(text); label.style.unityFontStyleAndWeight = FontStyle.Bold; label.style.marginTop = 12; label.style.marginBottom = 6; return label; }
    private static Image Icon(Sprite sprite, int size = 32, string tooltip = "") { var icon = new Image { sprite = sprite, tooltip = tooltip, scaleMode = ScaleMode.ScaleToFit }; icon.style.width = size; icon.style.height = size; icon.style.flexShrink = 0; return icon; }
    private static void MarkSelected(Button button, bool selected) { button.style.marginBottom = 3; if (selected) button.style.backgroundColor = new Color(.2f, .35f, .5f); }
}
