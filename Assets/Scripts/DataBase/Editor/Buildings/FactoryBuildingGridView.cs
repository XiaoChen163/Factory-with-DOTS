using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// IMGUI drawing hosted inside the UI Toolkit page keeps even large footprints light.
public sealed class FactoryBuildingGridView : IMGUIContainer
{
    public FactoryBuildingGridView(Func<FactoryContentBuilding> read, int height = 320)
    {
        style.height = height;
        onGUIHandler = () =>
        {
            var b = read();
            Rect rect = GUILayoutUtility.GetRect(1, height, GUILayout.ExpandWidth(true));
            if (b == null || b.width <= 0 || b.length <= 0 || b.width > 255 || b.length > 255)
            { GUI.Label(rect, "待补全占格"); return; }
            HashSet<Vector2Int> occupied;
            try { occupied = new HashSet<Vector2Int>(FactoryBuildingFootprintUtility.GetOccupiedOffsets(b.SpatialDefinition()).Select(c => new Vector2Int(c.x, c.y))); }
            catch { GUI.Label(rect, "占格无效，请修改 CSV 或重新导入"); return; }
            float size = Mathf.Min(rect.width / (b.width + 2), rect.height / (b.length + 2));
            var origin = new Vector2(rect.x, rect.y);
            var coordinates = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperLeft,
                padding = new RectOffset(2, 0, 1, 0)
            };
            var arrows = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(size * .65f), 18, 48),
                fontStyle = FontStyle.Bold,
                padding = new RectOffset()
            };
            var slots = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.LowerRight,
                padding = new RectOffset(0, 3, 0, 2)
            };
            for (int z = -1; z <= b.length; z++)
            for (int x = -1; x <= b.width; x++)
            {
                Rect cell = new Rect(origin.x + (x + 1) * size, origin.y + (b.length - z) * size, Mathf.Max(.1f, size - 1), Mathf.Max(.1f, size - 1));
                EditorGUI.DrawRect(cell, occupied.Contains(new Vector2Int(x, z)) ? new Color(.3f, .55f, .75f) : new Color(.18f, .18f, .18f));
                if (size > 22) GUI.Label(cell, x + "," + z, coordinates);
            }
            foreach (var p in b.ports)
            {
                var cell = new Rect(origin.x + (p.cellOffset.x + 1) * size, origin.y + (b.length - p.cellOffset.y) * size, size, size);
                var color = GUI.color;
                GUI.color = p.type == BuildingPortType.Input ? Color.green : new Color(1, .65f, .2f);
                string arrow = p.direction.x > 0 ? "→" : p.direction.x < 0 ? "←" : p.direction.y > 0 ? "↑" : "↓";
                GUI.Label(cell, arrow, arrows);
                if (p.outputSlot > 0) GUI.Label(cell, p.outputSlot.ToString(), slots);
                GUI.color = color;
            }
        };
    }
}
