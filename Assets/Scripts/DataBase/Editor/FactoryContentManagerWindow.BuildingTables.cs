using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class FactoryContentManagerWindow
{
    [SerializeField] private string selectedBuildingKey = "", newBuildingKey = "";
    [SerializeField] private bool gridExpanded = true;
    private FactoryBuildingImportWindow buildingImportWindow;

    private void AddBuildingToolbar(Toolbar toolbar)
    {
        var key = new TextField { value = newBuildingKey, tooltip = "新增建筑的 Key；留空自动分配。创建后 Key 只读。", name = "new-building-key" };
        key.style.width = 130;
        key.RegisterValueChangedCallback(e => newBuildingKey = e.newValue);
        toolbar.Add(key);
        var import = new Button(() => Run(() =>
        {
            string path = EditorUtility.OpenFilePanel("导入 Factory 建筑", "", "json");
            if (string.IsNullOrEmpty(path)) return;
            PreviewBuildingImport(FactoryBuildingJson.Read(path));
        })) { text = "导入 .factory.json", name = "building-import" };
        import.SetEnabled(Writable);
        toolbar.Add(import);
    }

    // The preview owns no draft: apply changes to this manager only after confirmation.
    public FactoryBuildingImportWindow PreviewBuildingImport(FactoryBuildingJson json)
    {
        if (buildingImportWindow != null) buildingImportWindow.Close();
        bool isNew = !draft.GetBuildings().Any(b => b.key == json.Building.key);
        buildingImportWindow = FactoryBuildingImportWindow.Open(json, isNew, (footprint, ports, colliders) =>
        {
            if (this == null || draft == null) throw new InvalidOperationException("内容管理器已关闭，请重新打开后导入。");
            FactoryDatabaseCsvImporter.EnsureCanWrite();
            selectedBuildingKey = draft.ImportBuildingJson(json, footprint, ports, colliders);
            selectedTab = 0; gridExpanded = true; search = ""; Refresh();
        });
        return buildingImportWindow;
    }

    private void RefreshBuildingList()
    {
        if (list == null) return;
        list.Clear();
        foreach (var building in draft.GetBuildings(search))
        {
            var select = new Button(() => { selectedBuildingKey = building.key; DrawDetail(); RefreshList(); }) { name = "building-row-" + building.row };
            AddIdentityPreview(select, AssetDatabase.LoadAssetAtPath<Sprite>(building.iconPath), building.name, building.key);
            MarkSelected(select, building.key == selectedBuildingKey);
            list.Add(select);
        }
        if (list.childCount == 0) list.Add(new Label("没有匹配的建筑"));
        UpdateDelete();
    }

    private void DrawBuilding(FactoryContentBuilding b)
    {
        string key = b.key;
        detail.Add(Heading("建筑详情"));
        var identity = new TextField("Key（只读）") { name = "building-key", value = key, isReadOnly = true };
        detail.Add(identity);
        Bind(detail, new TextField("名称（zh-CN）") { name = "building-name", value = b.name, multiline = true }, value => draft.SetBuildingName(key, value));
        Bind(detail, new ObjectField("已有 Prefab") { name = "building-prefab", objectType = typeof(GameObject), allowSceneObjects = false, value = AssetDatabase.LoadAssetAtPath<GameObject>(b.prefabPath) },
            value => draft.SetBuildingPrefab(key, value as GameObject));
        Bind(detail, new ObjectField("图标") { name = "building-icon", objectType = typeof(Sprite), allowSceneObjects = false, value = AssetDatabase.LoadAssetAtPath<Sprite>(b.iconPath) },
            value => draft.SetBuildingStat(key, "building_presentation", "icon_path", value == null ? "" : AssetDatabase.GetAssetPath(value)));
        Bind(detail, new IntegerField("菜单顺序") { value = b.menuOrder }, value => draft.SetBuildingStat(key, "building_presentation", "menu_order", Number(value)));
        var bake = new Button(() => Run(() =>
        {
            FactoryDatabaseCsvImporter.EnsureCanWrite();
            if (draft.IsModified) throw new InvalidOperationException("请先保存草稿，再烘焙图标。");
            FactoryIconBaker.BakeChangedFromMenu();
            // The existing baker's manifest is keyed by prefab path, not building Key.
            var manifest = AssetDatabase.LoadAssetAtPath<FactoryIconBakeManifest>(FactoryIconBaker.ManifestAssetPath);
            if (manifest != null)
            {
                var icon = manifest.entries.FirstOrDefault(e => e.prefabPath == b.prefabPath);
                if (!string.IsNullOrEmpty(icon.outputPath)) draft.SetBuildingStat(key, "building_presentation", "icon_path", icon.outputPath);
            }
            RefreshList(); DrawDetail(); UpdateModified();
        })) { text = "烘焙变化的图标并绑定" };
        detail.Add(bake);
        var behaviors = new[] { "请选择行为" }.Concat(Enum.GetNames(typeof(BuildingKind))).ToList();
        var behavior = new PopupField<string>("行为", behaviors, b.Kind == 0 ? behaviors[0] : b.Kind.ToString()) { name = "building-behavior" };
        behavior.RegisterValueChangedCallback(e => BuildingAction(() => draft.SetBuildingBehavior(key, Enum.Parse<BuildingKind>(e.newValue))));
        detail.Add(behavior);
        if (b.Kind == BuildingKind.Processor)
        {
            var types = draft.GetMachineTypes();
            string current = draft.GetBuildingStat(key, "processor_stats", "machine_type_key");
            if (!types.Contains(current)) types.Insert(0, current);
            Bind(detail, new PopupField<string>("加工类型", types, current) { name = "building-machine" }, value => draft.SetBuildingStat(key, "processor_stats", "machine_type_key", value));
        }
        if (b.Kind == BuildingKind.Storage)
        {
            BuildingStatField(key, "storage_stats", "capacity", "仓储总容量");
            BuildingStatField(key, "storage_stats", "slot_count", "仓储槽位数");
        }
        if (b.Kind == BuildingKind.Belt || VerticalBeltUtility.IsVertical(b.Kind))
        {
            BuildingStatField(key, "belt_stats", "tier", "传送带 Tier");
            BuildingStatField(key, "belt_stats", "cells_per_second", "传送速度（格/秒）");
        }
        if (b.Kind == BuildingKind.RampFoundation) BuildingStatField(key, "ramp_stats", "rise_height_units", "坡道上升高度单位");
        DrawBuildingSpace(b);
        var import = draft.GetBuildingImport(key);
        detail.Add(Heading("建筑导入配置"));
        detail.Add(new Label("模型配对：" + (import.modelFile ?? "未导入") + "\n碰撞代理：" + import.colliders.Length + " 个"));
        foreach (var c in import.colliders)
            detail.Add(new Label($"{c.name} · {c.type} · 位置 {c.position} · 旋转 {c.rotation}"));
        detail.Add(new HelpBox("碰撞代理参数随保存写入 Editor 导入配置。模型生成和游戏内碰撞接入属于阶段 4。", HelpBoxMessageType.Info));
    }

    private void BuildingStatField(string key, string table, string column, string label)
    {
        Bind(detail, new TextField(label) { name = "building-" + column, value = draft.GetBuildingStat(key, table, column) },
            value => draft.SetBuildingStat(key, table, column, value));
    }

    private void BuildingAction(Action action) => Run(() => { action(); RefreshList(); DrawDetail(); UpdateModified(); });
}
