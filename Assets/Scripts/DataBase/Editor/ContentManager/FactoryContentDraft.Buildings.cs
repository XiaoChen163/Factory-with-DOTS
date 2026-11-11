using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed class FactoryContentBuilding
{
    public int row, width, length, menuOrder;
    public string key, name, behavior, modelPath, iconPath, bitmap;
    public bool rectangle;
    public FactoryBuildingPortTableRow[] ports;
    public BuildingKind Kind => Enum.TryParse(behavior, out BuildingKind kind) ? kind : 0;
    public FactoryBuildingDefinitionRow SpatialDefinition()
    {
        if (width < 1 || width > 255 || length < 1 || length > 255) throw new InvalidDataException("占格尺寸必须为 1–255。");
        return new FactoryBuildingDefinitionRow
        {
            key = key, kind = Kind, footprintWidth = (byte)width, footprintHeight = (byte)length,
            isRectangle = rectangle, footprintBitmap = bitmap, ports = ports
        };
    }
}

public sealed partial class FactoryContentDraft
{
    public List<FactoryContentBuilding> GetBuildings(string search = "") => content.buildings.Select((b, row) => new FactoryContentBuilding
    {
        row = row, key = b.key, name = b.displayName, behavior = b.kind == 0 ? "" : b.kind.ToString(),
        width = b.footprintWidth, length = b.footprintHeight, rectangle = b.isRectangle, bitmap = b.footprintBitmap,
        modelPath = UnityEditor.AssetDatabase.GetAssetPath(b.visualPrefab), iconPath = UnityEditor.AssetDatabase.GetAssetPath(b.icon),
        menuOrder = b.menuOrder, ports = (b.ports ?? Array.Empty<FactoryBuildingPortTableRow>()).ToArray()
    }).Where(b => Matches(b.key, search) || Matches(b.name, search)).ToList();
    private FactoryBuildingDefinitionRow Building(string key) => content.buildings.Single(b => b.key == key);
    private void EditBuilding(string key, string label, Func<FactoryBuildingDefinitionRow, FactoryBuildingDefinitionRow> edit) => Edit(label, c =>
    {
        int row = Array.FindIndex(c.buildings, b => b.key == key);
        if (row < 0) throw new InvalidDataException("建筑不存在：" + key);
        c.buildings[row] = edit(c.buildings[row]);
    });
    private static FactoryBuildingDefinitionRow NewBuilding(Content c, string key) => new FactoryBuildingDefinitionRow
    {
        id = NextId(c.buildings.Select(b => b.id)), key = key, displayName = key, isRectangle = true, footprintBitmap = "",
        ports = Array.Empty<FactoryBuildingPortTableRow>(), colliders = Array.Empty<FactoryColliderProxy>()
    };
    public string AddBuilding(string key = null)
    {
        key = key ?? FreeKey(content.buildings.Select(b => b.key), "new_building");
        RequireKey(key);
        if (content.buildings.Any(b => b.key == key)) throw new InvalidDataException("建筑 Key 已存在：" + key);
        string newKey = key;
        Edit("新增建筑草稿", c => c.buildings = c.buildings.Append(NewBuilding(c, newKey)).ToArray());
        return key;
    }
    public void DeleteBuilding(string key)
    {
        Edit("删除建筑配置", c => c.buildings = c.buildings.Where(b => b.key != key).ToArray());
        buildingImports.RemoveAll(i => i.buildingKey == key);
    }
    public void SetBuildingName(string key, string value) => EditBuilding(key, "修改建筑名称", b => { b.displayName = value; return b; });
    public string GetBuildingStat(string key, string section, string column, string fallback = "")
    {
        var b = Building(key);
        switch (section + "/" + column)
        {
            case "processor_stats/machine_type_key": return content.machineTypes.FirstOrDefault(m => m.id == b.machineTypeId).key ?? fallback;
            case "belt_stats/tier": return Num(b.beltTier);
            case "belt_stats/cells_per_second": return b.cellsPerSecond.ToString("R", CultureInfo.InvariantCulture);
            case "storage_stats/slot_count": return Num(b.storageSlotCount);
            case "ramp_stats/rise_height_units": return Num(b.rampRiseHeightUnits);
            case "building_presentation/icon_path": return UnityEditor.AssetDatabase.GetAssetPath(b.icon);
            case "building_presentation/menu_order": return Num(b.menuOrder);
            default: throw new ArgumentException("未知建筑属性：" + section + "/" + column);
        }
    }
    public void SetBuildingStat(string key, string section, string column, string value) => EditBuilding(key, "修改建筑属性", b =>
    {
        switch (section + "/" + column)
        {
            case "processor_stats/machine_type_key":
                var machine = content.machineTypes.SingleOrDefault(m => m.key == value);
                if (machine.id == 0) throw new InvalidDataException("加工类型不存在：" + value);
                b.machineTypeId = machine.id; break;
            case "belt_stats/tier": b.beltTier = byte.Parse(value, CultureInfo.InvariantCulture); break;
            case "belt_stats/cells_per_second": b.cellsPerSecond = float.Parse(value, CultureInfo.InvariantCulture); break;
            case "storage_stats/slot_count": b.storageSlotCount = ushort.Parse(value, CultureInfo.InvariantCulture); break;
            case "ramp_stats/rise_height_units": b.rampRiseHeightUnits = sbyte.Parse(value, CultureInfo.InvariantCulture); break;
            case "building_presentation/menu_order": b.menuOrder = int.Parse(value, CultureInfo.InvariantCulture); break;
            case "building_presentation/icon_path":
                b.icon = string.IsNullOrEmpty(value) ? null : UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(value);
                if (!string.IsNullOrEmpty(value) && b.icon == null) throw new InvalidDataException("图标不存在：" + value);
                break;
            default: throw new ArgumentException("未知建筑属性：" + section + "/" + column);
        }
        return b;
    });
    public void SetBuildingBehavior(string key, BuildingKind kind)
    {
        if (!Enum.IsDefined(typeof(BuildingKind), kind)) throw new InvalidDataException("请选择建筑行为。");
        if (Building(key).kind == kind) return;
        EditBuilding(key, "修改建筑行为", b =>
        {
            b.kind = kind;
            b.machineTypeId = 0; b.beltTier = 0; b.cellsPerSecond = 0; b.storageSlotCount = 0; b.rampRiseHeightUnits = 0;
            if (kind == BuildingKind.Belt || VerticalBeltUtility.IsVertical(kind)) { b.beltTier = 1; b.cellsPerSecond = 1; }
            else if (kind == BuildingKind.Storage) b.storageSlotCount = 8;
            else if (kind == BuildingKind.RampFoundation) b.rampRiseHeightUnits = 4;
            b.ports = NormalizePorts(kind, b.ports, true);
            return b;
        });
    }
    public void SetBuildingFootprint(string key, int width, int length, bool rectangle, string bitmap)
    {
        if (width < 1 || width > 255 || length < 1 || length > 255) throw new InvalidDataException("占格尺寸必须为 1–255。");
        EditBuilding(key, "修改建筑占格", b =>
        {
            b.footprintWidth = (byte)width; b.footprintHeight = (byte)length; b.isRectangle = rectangle; b.footprintBitmap = bitmap;
            FactoryBuildingFootprintUtility.GetOccupiedOffsets(b);
            return b;
        });
    }
    public void ToggleBuildingCell(string key, Vector2Int cell)
    {
        var b = GetBuildings().Single(v => v.key == key);
        var definition = b.SpatialDefinition();
        var occupied = new HashSet<Vector2Int>(FactoryBuildingFootprintUtility.GetOccupiedOffsets(definition).Select(c => new Vector2Int(c.x, c.y)));
        if (cell.x < 0 || cell.y < 0 || cell.x >= b.width || cell.y >= b.length) throw new InvalidDataException("请点击包围矩形内的占格。");
        if (!occupied.Remove(cell)) occupied.Add(cell);
        bool rectangle = occupied.Count == b.width * b.length;
        string bitmap = rectangle ? "" : string.Join("/", Enumerable.Range(0, b.length).Select(z => string.Concat(Enumerable.Range(0, b.width).Select(x => occupied.Contains(new Vector2Int(x, z)) ? '1' : '0'))));
        SetBuildingFootprint(key, b.width, b.length, rectangle, bitmap);
    }

    private static FactoryBuildingPortTableRow[] NormalizePorts(BuildingKind kind, IEnumerable<FactoryBuildingPortTableRow> ports, bool assignBindings, bool reindex = true)
    {
        var result = ports.ToArray();
        var usedSlots = new HashSet<ushort>(result.Where(p => p.type == BuildingPortType.Output && p.outputSlot > 0).Select(p => p.outputSlot));
        var usedNumbers = new HashSet<ushort>(result.Where(p => p.type == BuildingPortType.Output && p.number > 0).Select(p => p.number));
        int input = 0, output = 0;
        for (int i = 0; i < result.Length; i++)
        {
            var p = result[i];
            int index = p.type == BuildingPortType.Input ? input++ : output++;
            if (index > 255) throw new InvalidDataException("每类端口最多 256 个。");
            if (reindex) p.index = (byte)index;
            if (p.type != BuildingPortType.Output || (kind != 0 && kind != BuildingKind.Processor)) p.number = p.outputSlot = 0;
            else if (assignBindings)
            {
                if (p.outputSlot == 0) p.outputSlot = FreeBinding(usedSlots);
                if (p.number == 0) p.number = !usedNumbers.Contains(p.outputSlot) ? p.outputSlot : FreeBinding(usedNumbers);
                usedSlots.Add(p.outputSlot); usedNumbers.Add(p.number);
            }
            result[i] = p;
        }
        return result;
    }

    private static ushort FreeBinding(HashSet<ushort> used)
    {
        for (int i = 1; i <= ushort.MaxValue; i++) if (!used.Contains((ushort)i)) return (ushort)i;
        throw new InvalidDataException("没有可用的产物槽或端口编号。");
    }

    public void SetBuildingPorts(string key, FactoryBuildingPortTableRow[] ports)
    {
        var b = Building(key);
        var normalized = NormalizePorts(b.kind, ports, false);
        b.ports = normalized;
        FactoryBuildingFootprintUtility.ValidatePorts(b, FactoryBuildingFootprintUtility.GetOccupiedOffsets(b));
        EditBuilding(key, "修改建筑端口", value => { value.ports = normalized; return value; });
    }
    public void AddBuildingPort(string key, Vector2Int cell, Vector2Int direction, BuildingPortType type)
    {
        var b = GetBuildings().Single(v => v.key == key);
        var ports = NormalizePorts(b.Kind, b.ports.Concat(new[] { new FactoryBuildingPortTableRow { type = type, cellOffset = cell, direction = direction } }), true);
        SetBuildingPorts(key, ports);
    }

    public void ReverseBuildingPort(string key, int index)
    {
        var b = GetBuildings().Single(v => v.key == key);
        var ports = b.ports;
        ports[index].type = ports[index].type == BuildingPortType.Input ? BuildingPortType.Output : BuildingPortType.Input;
        ports[index].direction = -ports[index].direction;
        SetBuildingPorts(key, NormalizePorts(b.Kind, ports, true));
    }

    public string ImportBuildingJson(FactoryBuildingJson json, bool footprint, bool ports, bool colliders)
    {
        FactoryDatabaseEditor.EnsureCanWrite();
        footprint &= json.HasFootprint; ports &= json.HasPorts; colliders &= json.HasColliders;
        if (!footprint && !ports && !colliders) throw new InvalidDataException("请选择至少一个 JSON 中包含的导入部分。");
        string key = json.Building.key;
        var data = GetBuildingImport(key);
        if (json.Building.model_file != null) data.modelFile = json.Building.model_file;
        data.sourceJsonPath = json.SourcePath;
        Edit("导入建筑 JSON 草稿", c =>
        {
            int row = Array.FindIndex(c.buildings, b => b.key == key);
            if (row < 0) { row = c.buildings.Length; c.buildings = c.buildings.Append(NewBuilding(c, key)).ToArray(); }
            var b = c.buildings[row];
            if (footprint)
            {
                b.footprintWidth = checked((byte)json.Building.width.Value); b.footprintHeight = checked((byte)json.Building.length.Value);
                b.isRectangle = json.Building.is_rectangle.Value; b.footprintBitmap = json.Building.bitmap;
            }
            if (ports) b.ports = NormalizePorts(b.kind, json.Ports, false, false);
            if (colliders) b.colliders = json.Colliders;
            c.buildings[row] = b;
        });
        PutBuildingImport(data);
        return key;
    }
}
