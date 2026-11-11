using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Only this transient draft participates in Undo. Saving commits the referenced libraries.
public sealed partial class FactoryContentDraft : ScriptableObject
{
    [Serializable]
    private sealed class Content
    {
        public int version = 1;
        public System.Collections.Generic.List<CapabilityRecord> capabilityRecords = new System.Collections.Generic.List<CapabilityRecord>();
        public FactoryItemTableRow[] items = Array.Empty<FactoryItemTableRow>();
        public FactoryMachineTypeTableRow[] machineTypes = Array.Empty<FactoryMachineTypeTableRow>();
        public FactoryBuildingDefinitionRow[] buildings = Array.Empty<FactoryBuildingDefinitionRow>();
        public FactoryRecipeTableRow[] recipes = Array.Empty<FactoryRecipeTableRow>();
    }

    [SerializeField] private Content content;
    [SerializeField] private string original;
    [SerializeField] private string sourceJson;
    [SerializeField] private string sourceBytes;
    public bool IsLoaded => content != null;
    public bool IsModified => IsLoaded && JsonUtility.ToJson(content) != original;

    public void Load()
    {
        var asset = FactoryDatabaseEditor.Load();
        content = new Content { version = asset.version, items = asset.items, machineTypes = asset.machineTypes,
            buildings = asset.buildings, recipes = asset.recipes };
        content = JsonUtility.FromJson<Content>(JsonUtility.ToJson(content));
        foreach (var item in content.items)
            foreach (var capability in item.capabilities ?? Array.Empty<ItemCapabilityDefinition>())
                content.capabilityRecords.Add(new CapabilityRecord { itemId = item.id, kind = capability.Kind, json = JsonUtility.ToJson(capability) });
        for (int i = 0; i < content.items.Length; i++) content.items[i].capabilities = Array.Empty<ItemCapabilityDefinition>();
        original = JsonUtility.ToJson(content);
        sourceJson = FactoryDatabaseEditor.SourceJson();
        sourceBytes = FactoryDatabaseEditor.SourceBytes();
        LoadBuildingImports();
        Undo.ClearUndo(this);
    }

    private void Edit(string label, Action<Content> edit)
    {
        FactoryDatabaseEditor.EnsureCanWrite();
        var staged = JsonUtility.FromJson<Content>(JsonUtility.ToJson(content));
        edit(staged);
        if (JsonUtility.ToJson(staged) == JsonUtility.ToJson(content)) return;
        Undo.IncrementCurrentGroup();
        Undo.RegisterCompleteObjectUndo(this, label);
        content = JsonUtility.FromJson<Content>(JsonUtility.ToJson(staged));
    }

    public FactoryDatabaseAsset ReadCandidate()
    {
        var copy = JsonUtility.FromJson<Content>(JsonUtility.ToJson(content));
        var candidate = CreateInstance<FactoryDatabaseAsset>();
        candidate.version = copy.version;
        candidate.items = copy.items;
        candidate.machineTypes = copy.machineTypes;
        candidate.buildings = copy.buildings;
        candidate.recipes = copy.recipes;
        candidate.ownsInlineCapabilities = true;
        try
        {
            for (int i = 0; i < candidate.items.Length; i++)
                candidate.items[i].capabilities = copy.capabilityRecords.Where(r => r.itemId == candidate.items[i].id).Select(CreateCapability).ToArray();
            FactoryDatabaseEditor.Validate(candidate); return candidate;
        }
        catch { DestroyImmediate(candidate); throw; }
    }

    public void Save()
    {
        FactoryDatabaseEditor.EnsureCanWrite();
        if (!IsModified) return;
        if (FactoryDatabaseEditor.SourceJson() != sourceJson || FactoryDatabaseEditor.SourceBytes() != sourceBytes)
            throw new IOException("数据资产已被其他操作修改，请放弃草稿并重新读取。未覆盖外部修改。");
        var candidate = ReadCandidate();
        try { FactoryDatabaseEditor.Save(candidate); Load(); }
        finally { DestroyImmediate(candidate); }
    }

    private static ushort NextId(System.Collections.Generic.IEnumerable<ushort> ids)
    {
        int next = ids.Select(id => (int)id).DefaultIfEmpty(0).Max() + 1;
        if (next > ushort.MaxValue) throw new InvalidDataException("没有可用的 ID。");
        return (ushort)next;
    }
}
