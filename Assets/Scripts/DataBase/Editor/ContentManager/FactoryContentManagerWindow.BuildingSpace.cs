using System.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

public sealed partial class FactoryContentManagerWindow
{
    private void DrawBuildingSpace(FactoryContentBuilding b)
    {
        // Built-in one-cell belts and foundations need no spatial diagram.
        if (b.width == 1 && b.length == 1 &&
            (b.Kind == BuildingKind.Belt || VerticalBeltUtility.IsVertical(b.Kind) ||
             b.Kind == BuildingKind.Foundation || b.Kind == BuildingKind.RampFoundation)) return;

        string key = b.key;
        var foldout = new Foldout { text = "占格与端口（只读）", value = gridExpanded, name = "building-space" };
        foldout.RegisterValueChangedCallback(e => gridExpanded = e.newValue);
        detail.Add(foldout);
        foldout.Add(new Label($"X 宽度：{b.width} · Z 长度：{b.length}"));
        foldout.Add(new HelpBox("占格与端口只读，请通过 Blender 插件导出后导入，或修改 CSV 后重新读取。X 向右，Z 向上；坐标位于每格左上角，绿色箭头为输入，橙色为输出，右下角数字为产物槽。", HelpBoxMessageType.Info));
        foldout.Add(new FactoryBuildingGridView(() => draft.GetBuildings().FirstOrDefault(v => v.key == key)) { name = "building-grid" });
        foreach (var p in b.ports)
            foldout.Add(new Label($"{p.type}:{p.index} · 格 {p.cellOffset} · 方向 {p.direction}" + (p.outputSlot > 0 ? " · 槽 " + p.outputSlot : "")));
    }
}
