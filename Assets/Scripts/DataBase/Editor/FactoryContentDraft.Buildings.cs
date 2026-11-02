using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed class FactoryContentBuilding
{
    public int row, width, length, menuOrder;
    public string key, name, behavior, prefabPath, iconPath, bitmap;
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
    public const string BuildingsPath = "buildings/buildings.csv";
    public const string NamesPath = "buildings/building_names.csv";
    public const string PresentationPath = "buildings/building_presentation.csv";
    public const string PortsPath = "buildings/building_ports.csv";
    private static readonly string[] BuildingPaths = { BuildingsPath, NamesPath, PresentationPath, PortsPath,
        "buildings/processor_stats.csv", "buildings/belt_stats.csv", "buildings/storage_stats.csv", "buildings/ramp_stats.csv" };

    public List<FactoryContentBuilding> GetBuildings(string search = "")
    {
        var table = ReadTable(BuildingsPath);
        var names = ReadTable(NamesPath);
        var presentation = ReadTable(PresentationPath);
        var ports = ReadTable(PortsPath);
        return Rows(table).Select(row =>
        {
            string key = table.Get(row, "key");
            int[] localized = Rows(names).Where(r => names.Get(r, "key") == key).ToArray();
            int nameRow = localized.Where(r => names.Get(r, "locale") == "zh-CN").DefaultIfEmpty(-1).First();
            if (nameRow < 0) nameRow = localized.Length == 0 ? -1 : localized[0];
            int p = Find(presentation, key);
            return new FactoryContentBuilding
            {
                row = row, key = key, name = nameRow < 0 ? key : names.Get(nameRow, "name"), behavior = table.Get(row, "behavior"),
                width = Int(table.Get(row, "width")), length = Int(table.Get(row, "length")),
                rectangle = bool.TryParse(table.Get(row, "is_rectangle"), out bool rect) && rect, bitmap = table.Get(row, "bitmap"),
                prefabPath = table.Get(row, "prefab_path"), iconPath = p < 0 ? "" : presentation.Get(p, "icon_path"), menuOrder = p < 0 ? 0 : Int(presentation.Get(p, "menu_order")),
                ports = Rows(ports).Where(r => ports.Get(r, "key") == key).Select(r => ReadDraftPort(ports, r)).ToArray()
            };
        }).Where(b => Matches(b.key, search) || Matches(b.name, search)).ToList();
    }

    private static int Find(FactoryCsvTable table, string key) => Rows(table).Where(r => table.Get(r, "key") == key).DefaultIfEmpty(-1).First();
    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);
    private static FactoryBuildingPortTableRow ReadDraftPort(FactoryCsvTable table, int row) => new FactoryBuildingPortTableRow
    {
        type = Enum.Parse<BuildingPortType>(table.Get(row, "type")), index = byte.Parse(table.Get(row, "index"), CultureInfo.InvariantCulture),
        cellOffset = new Vector2Int(Int(table.Get(row, "cell_x")), Int(table.Get(row, "cell_z"))),
        direction = new Vector2Int(Int(table.Get(row, "direction_x")), Int(table.Get(row, "direction_z"))),
        number = table.TryGet(row, "number", out string n) && !string.IsNullOrEmpty(n) ? ushort.Parse(n, CultureInfo.InvariantCulture) : (ushort)0,
        outputSlot = table.TryGet(row, "output_slot", out string s) && !string.IsNullOrEmpty(s) ? ushort.Parse(s, CultureInfo.InvariantCulture) : (ushort)0
    };

    private static void RemoveKey(FactoryCsvTable table, string key)
    {
        foreach (int row in Rows(table).Where(r => table.Get(r, "key") == key).Reverse().ToArray()) table.RemoveAt(row);
    }
    private static void Upsert(FactoryCsvTable table, string key, Dictionary<string, string> values)
    {
        int row = Find(table, key);
        if (row < 0) { values["key"] = key; table.Append(values); }
        else foreach (var v in values) { table.EnsureColumn(v.Key); table.Set(row, v.Key, v.Value); }
    }

    private static void AppendBuilding(Dictionary<string, FactoryCsvTable> docs, string key)
    {
        docs[BuildingsPath].Append(new Dictionary<string, string>
        {
            ["key"] = key, ["behavior"] = "", ["width"] = "0", ["length"] = "0", ["prefab_path"] = "",
            ["is_rectangle"] = "true", ["bitmap"] = ""
        });
        docs[NamesPath].Append(new Dictionary<string, string> { ["key"] = key, ["locale"] = "zh-CN", ["name"] = key });
        docs[PresentationPath].Append(new Dictionary<string, string> { ["key"] = key, ["icon_path"] = "", ["menu_order"] = "0" });
    }

    public string AddBuilding(string key = null)
    {
        key = key ?? FreeKey(GetBuildings().Select(b => b.key), "new_building");
        RequireKey(key);
        if (GetBuildings().Any(b => b.key == key)) throw new InvalidDataException($"建筑 Key 已存在：{key}");
        string newKey = key;
        Edit("新增建筑草稿", docs => AppendBuilding(docs, newKey), BuildingsPath, NamesPath, PresentationPath);
        return key;
    }

    public void DeleteBuilding(string key)
    {
        Edit("删除建筑配置", docs => { foreach (var table in docs.Values) RemoveKey(table, key); }, BuildingPaths);
        // The undo snapshot above also covers pending import hints. Keep existing assets.
        var import = buildingImports.FirstOrDefault(i => JsonUtility.FromJson<FactoryBuildingImportData>(i.text).buildingKey == key);
        if (import != null)
        {
            if (import.original == "") buildingImports.Remove(import);
            else import.text = import.original;
        }
    }

    public void SetBuildingName(string key, string value)
    {
        Edit("修改建筑名称", docs =>
        {
            var table = docs[NamesPath];
            int row = Rows(table).Where(r => table.Get(r, "key") == key && table.Get(r, "locale") == "zh-CN").DefaultIfEmpty(-1).First();
            if (row < 0) table.Append(new Dictionary<string, string> { ["key"] = key, ["locale"] = "zh-CN", ["name"] = value });
            else table.Set(row, "name", value);
        }, NamesPath);
    }

    public string GetBuildingStat(string key, string tableName, string column, string fallback = "")
    {
        var table = ReadTable("buildings/" + tableName + ".csv");
        int row = Find(table, key);
        return row < 0 ? fallback : table.Get(row, column);
    }

    public void SetBuildingStat(string key, string tableName, string column, string value)
    {
        string path = "buildings/" + tableName + ".csv";
        Edit("修改建筑属性", docs => Upsert(docs[path], key, new Dictionary<string, string> { [column] = value }), path);
    }

    public void SetBuildingPrefab(string key, GameObject prefab)
    {
        string path = prefab == null ? "" : UnityEditor.AssetDatabase.GetAssetPath(prefab);
        if (prefab != null && (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("请选择项目内的 .prefab 资产。");
        Edit("设置建筑 Prefab", docs => docs[BuildingsPath].Set(Find(docs[BuildingsPath], key), "prefab_path", path), BuildingsPath);
    }

    public void SetBuildingBehavior(string key, BuildingKind kind)
    {
        if (!Enum.IsDefined(typeof(BuildingKind), kind)) throw new InvalidDataException("请选择建筑行为。");
        var building = GetBuildings().Single(b => b.key == key);
        if (building.Kind == kind) return;
        Edit("修改建筑行为", docs =>
        {
            docs[BuildingsPath].Set(Find(docs[BuildingsPath], key), "behavior", kind.ToString());
            foreach (string path in BuildingPaths.Skip(4)) RemoveKey(docs[path], key);
            if (kind == BuildingKind.Processor)
                Upsert(docs["buildings/processor_stats.csv"], key, new Dictionary<string, string> { ["machine_type_key"] = "" });
            else if (kind == BuildingKind.Belt || VerticalBeltUtility.IsVertical(kind))
                Upsert(docs["buildings/belt_stats.csv"], key, new Dictionary<string, string> { ["tier"] = "1", ["cells_per_second"] = "1" });
            else if (kind == BuildingKind.Storage)
                Upsert(docs["buildings/storage_stats.csv"], key, new Dictionary<string, string> { ["capacity"] = "100", ["slot_count"] = "8" });
            else if (kind == BuildingKind.RampFoundation)
                Upsert(docs["buildings/ramp_stats.csv"], key, new Dictionary<string, string> { ["rise_height_units"] = "4" });
            WritePorts(docs[PortsPath], key, NormalizePorts(kind, building.ports, true));
        }, BuildingPaths);
    }

    public void SetBuildingFootprint(string key, int width, int length, bool rectangle, string bitmap)
    {
        if (width < 1 || width > 255 || length < 1 || length > 255) throw new InvalidDataException("占格尺寸必须为 1–255。");
        var shape = new FactoryBuildingDefinitionRow { key = key, footprintWidth = (byte)width, footprintHeight = (byte)length, isRectangle = rectangle, footprintBitmap = bitmap };
        FactoryBuildingFootprintUtility.GetOccupiedOffsets(shape);
        Edit("修改建筑占格", docs => WriteFootprint(docs[BuildingsPath], key, width, length, rectangle, bitmap), BuildingsPath);
    }

    private static void WriteFootprint(FactoryCsvTable table, string key, int width, int length, bool rectangle, string bitmap)
    {
        int row = Find(table, key);
        table.Set(row, "width", Num(width)); table.Set(row, "length", Num(length));
        table.Set(row, "is_rectangle", rectangle ? "true" : "false"); table.Set(row, "bitmap", bitmap);
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

    private static void WritePorts(FactoryCsvTable table, string key, FactoryBuildingPortTableRow[] ports)
    {
        int[] existing = Rows(table).Where(r => table.Get(r, "key") == key).ToArray();
        for (int i = 0; i < ports.Length; i++)
        {
            var p = ports[i];
            var values = new Dictionary<string, string>
            {
                ["key"] = key, ["type"] = p.type.ToString(), ["index"] = Num(p.index), ["cell_x"] = Num(p.cellOffset.x), ["cell_z"] = Num(p.cellOffset.y),
                ["direction_x"] = Num(p.direction.x), ["direction_z"] = Num(p.direction.y),
                ["number"] = p.number == 0 ? "" : Num(p.number), ["output_slot"] = p.outputSlot == 0 ? "" : Num(p.outputSlot)
            };
            if (i >= existing.Length) table.Append(values);
            else foreach (var value in values) { table.EnsureColumn(value.Key); table.Set(existing[i], value.Key, value.Value); }
        }
        foreach (int row in existing.Skip(ports.Length).Reverse()) table.RemoveAt(row);
    }

    public void SetBuildingPorts(string key, FactoryBuildingPortTableRow[] ports)
    {
        var b = GetBuildings().Single(v => v.key == key);
        var normalized = NormalizePorts(b.Kind, ports, false);
        var definition = b.SpatialDefinition(); definition.ports = normalized;
        FactoryBuildingFootprintUtility.ValidatePorts(definition, FactoryBuildingFootprintUtility.GetOccupiedOffsets(definition));
        Edit("修改建筑端口", docs => WritePorts(docs[PortsPath], key, normalized), PortsPath);
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
        FactoryDatabaseCsvImporter.EnsureCanWrite();
        footprint &= json.HasFootprint; ports &= json.HasPorts; colliders &= json.HasColliders;
        if (!footprint && !ports && !colliders) throw new InvalidDataException("请选择至少一个 JSON 中包含的导入部分。");
        string key = json.Building.key;
        var docs = BuildingPaths.ToDictionary(p => p, ReadTable, StringComparer.Ordinal);
        var existing = GetBuildings().SingleOrDefault(b => b.key == key);
        if (existing == null) AppendBuilding(docs, key);
        if (footprint) WriteFootprint(docs[BuildingsPath], key, json.Building.width.Value, json.Building.length.Value, json.Building.is_rectangle.Value, json.Building.bitmap);
        if (ports) WritePorts(docs[PortsPath], key, NormalizePorts(existing?.Kind ?? 0, json.Ports, false, false));
        var data = GetBuildingImport(key);
        if (json.Building.model_file != null) data.modelFile = json.Building.model_file;
        data.sourceJsonPath = json.SourcePath;
        if (colliders) data.colliders = json.Colliders;
        UnityEditor.Undo.IncrementCurrentGroup();
        UnityEditor.Undo.RegisterCompleteObjectUndo(this, "导入建筑 JSON 草稿");
        foreach (var document in docs) Source(document.Key).text = document.Value.Text;
        PutBuildingImport(data);
        return key;
    }
}
