using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

// Keep stage 1's source editor available until the spatial building editor in stage 3.
public sealed partial class FactoryContentManagerWindow
{
    private ScrollView buildingRows;

    private void DrawBuildingTables()
    {
        rootVisualElement.Add(new HelpBox("建筑专用编辑与 JSON 导入将在阶段 3 提供。当前可继续使用源表草稿编辑；名称默认使用 zh-CN，其他语言保持原样。", HelpBoxMessageType.Info));
        var tables = draft.Tables.Where(t => t.path.Contains("/buildings/")).ToList();
        selectedTable = Mathf.Clamp(selectedTable, 0, tables.Count - 1);
        var choices = tables.Select(t => t.path.Substring(FactoryContentDraft.TableDirectory.Length + 1)).ToList();
        var selector = new PopupField<string>("数据表", choices, selectedTable);
        selector.RegisterValueChangedCallback(e => { selectedTable = choices.IndexOf(e.newValue); Refresh(); });
        rootVisualElement.Add(selector);
        buildingRows = new ScrollView(ScrollViewMode.VerticalAndHorizontal);
        buildingRows.style.flexGrow = 1;
        rootVisualElement.Add(buildingRows);
        RefreshBuildingRows();
    }

    private void RefreshBuildingRows()
    {
        if (buildingRows == null) return;
        buildingRows.Clear();
        var tables = draft.Tables.Where(t => t.path.Contains("/buildings/")).ToList();
        var source = tables[selectedTable];
        int tableIndex = draft.Tables.ToList().IndexOf(source);
        var table = FactoryCsvTable.Parse(source.text);
        var header = Row();
        header.style.flexWrap = Wrap.NoWrap;
        foreach (string column in table.Header)
        {
            var label = new Label(column);
            label.style.width = 180;
            label.style.flexShrink = 0;
            header.Add(label);
        }
        buildingRows.Add(header);
        for (int r = 0; r < table.RowCount; r++)
        {
            int rowIndex = r;
            if (!table.Header.Any(c => FactoryContentDraft.Matches(table.Get(r, c.Trim()), search))) continue;
            var line = Row();
            line.style.flexWrap = Wrap.NoWrap;
            foreach (string column in table.Header)
            {
                string name = column.Trim();
                var field = new TextField { value = table.Get(r, name), multiline = true };
                field.style.width = 180;
                field.style.flexShrink = 0;
                field.SetEnabled(Writable);
                field.RegisterValueChangedCallback(e => Run(() => { draft.SetCell(tableIndex, rowIndex, name, e.newValue); UpdateModified(); }));
                line.Add(field);
            }
            buildingRows.Add(line);
        }
    }
}
