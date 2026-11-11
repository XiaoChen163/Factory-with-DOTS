using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public sealed partial class FactoryContentDraft
{
    // Serialized draft payloads keep component edits in the same Undo transaction as
    // item edits, without mutating persistent sub-assets or leaking transient objects.
    [Serializable]
    private sealed class CapabilityRecord
    {
        public ushort itemId;
        public ItemCapabilityKind kind;
        public string json;
    }
    private static ItemCapabilityDefinition CreateCapability(CapabilityRecord record)
    {
        ItemCapabilityDefinition value;
        switch (record.kind)
        {
            case ItemCapabilityKind.Gun: value = CreateInstance<GunCapabilityDefinition>(); break;
            case ItemCapabilityKind.Ammunition: value = CreateInstance<AmmunitionCapabilityDefinition>(); break;
            case ItemCapabilityKind.WeaponPresentation: value = CreateInstance<WeaponPresentationCapabilityDefinition>(); break;
            default: throw new InvalidDataException("不支持此物品能力组件。");
        }
        JsonUtility.FromJsonOverwrite(record.json, value);
        return value;
    }
    public List<ItemCapabilityKind> GetItemCapabilities(int row) => content.capabilityRecords.Where(r => r.itemId == content.items[row].id).Select(r => r.kind).ToList();
    public void AddItemCapability(int row, ItemCapabilityKind kind)
    {
        if (GetItemCapabilities(row).Contains(kind)) throw new InvalidDataException("此物品已附加同类型的能力组件。");
        var definition = CreateCapability(new CapabilityRecord { kind = kind, json = "{}" });
        try
        {
            string json = JsonUtility.ToJson(definition);
            Edit("添加物品能力组件", c => c.capabilityRecords.Add(new CapabilityRecord { itemId = c.items[row].id, kind = kind, json = json }));
        }
        finally { DestroyImmediate(definition); }
    }
    public void RemoveItemCapability(int row, ItemCapabilityKind kind) => Edit("移除物品能力组件", c =>
        c.capabilityRecords.RemoveAll(r => r.itemId == c.items[row].id && r.kind == kind));

    // Caller owns this disposable editor preview and must DestroyImmediate it.
    public ItemCapabilityDefinition ReadItemCapability(int row, ItemCapabilityKind kind) =>
        CreateCapability(content.capabilityRecords.Single(r => r.itemId == content.items[row].id && r.kind == kind));
    public void EditItemCapability<T>(int row, Action<T> edit) where T : ItemCapabilityDefinition
    {
        Edit("编辑物品能力组件", c =>
        {
            var record = c.capabilityRecords.Single(r => r.itemId == c.items[row].id && CapabilityType(r.kind) == typeof(T));
            var definition = (T)CreateCapability(record);
            try { edit(definition); record.json = JsonUtility.ToJson(definition); }
            finally { DestroyImmediate(definition); }
        });
    }
    private static Type CapabilityType(ItemCapabilityKind kind) => kind == ItemCapabilityKind.Gun ? typeof(GunCapabilityDefinition) :
        kind == ItemCapabilityKind.Ammunition ? typeof(AmmunitionCapabilityDefinition) : typeof(WeaponPresentationCapabilityDefinition);

    private static void RemapCapabilityItemId(Content content, ushort previous, ushort current)
    {
        foreach (var record in content.capabilityRecords)
        {
            if (record.itemId == previous) record.itemId = current;
            if (record.kind != ItemCapabilityKind.Gun) continue;
            var gun = (GunCapabilityDefinition)CreateCapability(record);
            try
            {
                if (gun.ammunitionItem.Value == previous)
                {
                    gun.ammunitionItem = new ItemId { Value = current };
                    record.json = JsonUtility.ToJson(gun);
                }
            }
            finally { DestroyImmediate(gun); }
        }
    }
}
