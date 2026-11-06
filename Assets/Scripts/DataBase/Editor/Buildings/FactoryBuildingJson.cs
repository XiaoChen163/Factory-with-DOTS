using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Explicit v1 DTOs. Nullable scalars and nullable arrays retain absence vs empty.
public sealed class FactoryBuildingJson
{
    public sealed class BuildingDto
    {
        public string key, model_file, bitmap;
        public int? width, length;
        public bool? is_rectangle;
    }
    public sealed class PortDto
    {
        public string type;
        public int? index, cell_x, cell_z, direction_x, direction_z, number, output_slot;
    }
    public sealed class VectorDto { public float? x, y, z; }
    public sealed class RotationDto { public float? x, y, z, w; }
    public sealed class ColliderDto
    {
        public string name, type, axis;
        public VectorDto position, size;
        public RotationDto rotation;
        public float? radius, height;
    }
    private sealed class DocumentDto
    {
        public int? schema_version;
        public BuildingDto building;
        public PortDto[] ports;
        public ColliderDto[] colliders;
    }

    public string SourcePath { get; private set; }
    public BuildingDto Building { get; private set; }
    public FactoryBuildingPortTableRow[] Ports { get; private set; }
    public FactoryColliderProxy[] Colliders { get; private set; }
    public bool HasFootprint => Building.width.HasValue;
    public bool HasPorts => Ports != null;
    public bool HasColliders => Colliders != null;

    public static FactoryBuildingJson Read(string path) => Parse(File.ReadAllText(path), Path.GetFullPath(path));

    public static FactoryBuildingJson Parse(string json, string sourcePath = "")
    {
        var root = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        foreach (string section in new[] { "building", "ports", "colliders" })
            if (root.TryGetValue(section, out var token) && token.Type == JTokenType.Null)
                throw new InvalidDataException($"JSON '{section}' 不能为 null；省略表示不更新，空数组表示清空。");
        var dto = root.ToObject<DocumentDto>(JsonSerializer.Create(new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None }));
        if (dto.schema_version != 1) throw new InvalidDataException("仅支持 factory.json schema_version = 1。");
        if (dto.building == null || string.IsNullOrWhiteSpace(dto.building.key) || dto.building.key.Any(char.IsWhiteSpace))
            throw new InvalidDataException("JSON 缺少有效 building.key。");
        var building = dto.building;
        var buildingToken = (JObject)root["building"];
        bool anyFootprint = new[] { "width", "length", "is_rectangle", "bitmap" }.Any(f => buildingToken.Property(f) != null);
        if (anyFootprint)
        {
            if (!building.width.HasValue || !building.length.HasValue || !building.is_rectangle.HasValue || building.bitmap == null ||
                building.width < 1 || building.width > 255 || building.length < 1 || building.length > 255)
                throw new InvalidDataException("占格必须完整提供 width、length（1–255）、is_rectangle 和 bitmap。");
            FactoryBuildingFootprintUtility.GetOccupiedOffsets(new FactoryBuildingDefinitionRow
            {
                key = building.key, footprintWidth = (byte)building.width.Value, footprintHeight = (byte)building.length.Value,
                isRectangle = building.is_rectangle.Value, footprintBitmap = building.bitmap
            });
        }
        var ports = dto.ports?.Select(ReadPort).ToArray();
        if (ports != null)
            foreach (var group in ports.GroupBy(p => p.type))
                if (!group.Select(p => (int)p.index).OrderBy(i => i).SequenceEqual(Enumerable.Range(0, group.Count())))
                    throw new InvalidDataException("每类 JSON 端口索引必须从 0 连续编号且不重复。");
        return new FactoryBuildingJson
        {
            SourcePath = sourcePath, Building = building,
            Ports = ports, Colliders = dto.colliders?.Select(ReadCollider).ToArray()
        };
    }

    private static FactoryBuildingPortTableRow ReadPort(PortDto p)
    {
        if (p == null || (p.type != "Input" && p.type != "Output") || !p.index.HasValue || p.index < 0 || p.index > 255 ||
            !p.cell_x.HasValue || !p.cell_z.HasValue || !p.direction_x.HasValue || !p.direction_z.HasValue ||
            (p.number.HasValue && (p.number < 0 || p.number > 65535)) || (p.output_slot.HasValue && (p.output_slot < 0 || p.output_slot > 65535)))
            throw new InvalidDataException("JSON 端口缺少必要字段，或索引/产物槽超出范围。");
        if (Math.Abs((long)p.direction_x.Value) + Math.Abs((long)p.direction_z.Value) != 1)
            throw new InvalidDataException("JSON 端口方向必须为四向单位整数。");
        return new FactoryBuildingPortTableRow
        {
            type = p.type == "Input" ? BuildingPortType.Input : BuildingPortType.Output, index = (byte)p.index.Value,
            cellOffset = new Vector2Int(p.cell_x.Value, p.cell_z.Value), direction = new Vector2Int(p.direction_x.Value, p.direction_z.Value),
            number = (ushort)(p.number ?? 0), outputSlot = (ushort)(p.output_slot ?? 0)
        };
    }

    private static FactoryColliderProxy ReadCollider(ColliderDto c)
    {
        if (c == null || string.IsNullOrWhiteSpace(c.name) || c.position == null || c.rotation == null)
            throw new InvalidDataException("碰撞代理缺少名称、位置或旋转。");
        var q = c.rotation;
        var proxy = new FactoryColliderProxy
        {
            name = c.name, type = c.type, axis = c.axis, position = ReadVector(c.position),
            rotation = new Quaternion(Finite(q.x), Finite(q.y), Finite(q.z), Finite(q.w)),
            size = c.size == null ? Vector3.zero : ReadVector(c.size), radius = c.radius ?? 0, height = c.height ?? 0
        };
        ValidateCollider(proxy);
        return proxy;
    }

    private static Vector3 ReadVector(VectorDto v) => new Vector3(Finite(v.x), Finite(v.y), Finite(v.z));
    private static float Finite(float? value)
    {
        if (!value.HasValue || float.IsNaN(value.Value) || float.IsInfinity(value.Value)) throw new InvalidDataException("代理缺少必要数值或包含非有限数值。");
        return value.Value;
    }

    public static void ValidateCollider(FactoryColliderProxy c) => FactoryBuildingColliderBuilder.Validate(c);
}
