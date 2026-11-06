using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

// One row per shape. Quaternions preserve the exact pose exported by Blender.
public static class FactoryBuildingColliderCsv
{
    public const string Header = "building_key,collider_key,type,position_x,position_y,position_z,rotation_x,rotation_y,rotation_z,rotation_w,size_x,size_y,size_z,radius,height";

    public static Dictionary<string, FactoryColliderProxy[]> Read(FactoryCsvTable table)
    {
        var groups = new Dictionary<string, List<FactoryColliderProxy>>(StringComparer.Ordinal);
        var keys = new HashSet<(string, string)>();
        for (int row = 0; row < table.RowCount; row++)
        {
            string building = table.Get(row, "building_key"), key = table.Get(row, "collider_key");
            if (string.IsNullOrWhiteSpace(building) || string.IsNullOrWhiteSpace(key) || !keys.Add((building, key)))
                throw new InvalidDataException($"碰撞体第 {row + 2} 行：建筑 Key / 碰撞体 Key 为空或重复。");
            float Number(string column, bool optional = false)
            {
                string text = table.Get(row, column);
                if (optional && string.IsNullOrWhiteSpace(text)) return 0;
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || float.IsNaN(value) || float.IsInfinity(value))
                    throw new InvalidDataException($"碰撞体 '{building}:{key}' 的 {column} 必须为有限数值。");
                return value;
            }
            string type = table.Get(row, "type");
            var shape = new FactoryColliderProxy
            {
                name = key, type = type, axis = type == "Capsule" || type == "Cylinder" ? "Y" : "",
                position = new Vector3(Number("position_x"), Number("position_y"), Number("position_z")),
                rotation = new Quaternion(Number("rotation_x"), Number("rotation_y"), Number("rotation_z"), Number("rotation_w")),
                size = new Vector3(Number("size_x", true), Number("size_y", true), Number("size_z", true)),
                radius = Number("radius", true), height = Number("height", true)
            };
            FactoryBuildingJson.ValidateCollider(shape);
            if (!groups.TryGetValue(building, out var list)) groups.Add(building, list = new List<FactoryColliderProxy>());
            list.Add(shape);
        }
        return groups.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }

    public static void Write(FactoryCsvTable table, string building, FactoryColliderProxy[] shapes)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var shape in shapes)
        {
            FactoryBuildingJson.ValidateCollider(shape);
            if (string.IsNullOrWhiteSpace(shape.name) || !names.Add(shape.name))
                throw new InvalidDataException("同一建筑的碰撞体 Key 必须非空且唯一。");
        }
        for (int row = table.RowCount - 1; row >= 0; row--)
            if (table.Get(row, "building_key") == building) table.RemoveAt(row);
        string Num(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        foreach (var s in shapes)
            table.Append(new Dictionary<string, string>
            {
                ["building_key"] = building, ["collider_key"] = s.name, ["type"] = s.type,
                ["position_x"] = Num(s.position.x), ["position_y"] = Num(s.position.y), ["position_z"] = Num(s.position.z),
                ["rotation_x"] = Num(s.rotation.x), ["rotation_y"] = Num(s.rotation.y), ["rotation_z"] = Num(s.rotation.z), ["rotation_w"] = Num(s.rotation.w),
                ["size_x"] = Num(s.size.x), ["size_y"] = Num(s.size.y), ["size_z"] = Num(s.size.z),
                ["radius"] = Num(s.radius), ["height"] = Num(s.height)
            });
    }
}
