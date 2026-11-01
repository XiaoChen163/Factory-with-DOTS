using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class FactoryItemSearchPopup : PopupWindowContent
{
    private readonly List<FactoryContentItem> items;
    private readonly Func<FactoryContentItem, Sprite> icon;
    private readonly Action<FactoryContentItem> select;

    public FactoryItemSearchPopup(List<FactoryContentItem> items, Func<FactoryContentItem, Sprite> icon, Action<FactoryContentItem> select)
    { this.items = items; this.icon = icon; this.select = select; }

    public override Vector2 GetWindowSize() => new Vector2(420, 360);
    public override void OnGUI(Rect rect)
    {
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape) editorWindow.Close();
    }

    public override void OnOpen()
    {
        var root = editorWindow.rootVisualElement;
        root.style.paddingLeft = 6;
        root.style.paddingRight = 6;
        var search = new TextField("搜索物品") { name = "item-picker-search" };
        root.Add(search);
        var error = new Label { name = "item-picker-error" };
        error.style.whiteSpace = WhiteSpace.Normal;
        root.Add(error);
        var list = new ScrollView { name = "item-picker-list" };
        list.style.flexGrow = 1;
        root.Add(list);
        void Refresh(string query)
        {
            list.Clear();
            foreach (var item in items.Where(i => FactoryContentDraft.Matches(i.key, query) || FactoryContentDraft.Matches(i.name, query)))
            {
                var button = new Button(() =>
                {
                    try { select(item); editorWindow.Close(); }
                    catch (Exception e) { error.text = e.Message; }
                }) { name = "pick-item-" + item.row };
                button.style.flexDirection = FlexDirection.Row;
                var image = new Image { sprite = icon(item), scaleMode = ScaleMode.ScaleToFit };
                image.style.width = 28;
                image.style.height = 28;
                button.Add(image);
                button.Add(new Label(item.name + "\n" + item.key));
                list.Add(button);
            }
            if (list.childCount == 0) list.Add(new Label("没有匹配的物品"));
        }
        search.RegisterValueChangedCallback(e => Refresh(e.newValue));
        Refresh("");
        root.schedule.Execute(() => search.Focus());
    }
}
