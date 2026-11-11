using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

public sealed partial class FactoryContentManagerWindow
{
    private static string CapabilityLabel(ItemCapabilityKind kind) => kind == ItemCapabilityKind.Gun ? "枪械" :
        kind == ItemCapabilityKind.Ammunition ? "弹药" : "武器表现";

    private void DrawItemCapabilities(FactoryContentItem item)
    {
        detail.Add(Heading("能力组件"));
        var kinds = draft.GetItemCapabilities(item.row);
        foreach (var kind in kinds)
        {
            var section = new Foldout { text = CapabilityLabel(kind), value = true, name = "item-capability-" + kind };
            detail.Add(section);
            var capability = draft.ReadItemCapability(item.row, kind);
            try
            {
                if (capability is GunCapabilityDefinition gun) DrawGunCapability(section, item.row, gun);
                else if (capability is AmmunitionCapabilityDefinition ammo)
                    Bind(section, new IntegerField("基础伤害") { value = ammo.baseDamage, isDelayed = true, name = "ammo-damage" },
                        value => draft.EditItemCapability<AmmunitionCapabilityDefinition>(item.row, c => c.baseDamage = value));
                else if (capability is WeaponPresentationCapabilityDefinition presentation) DrawWeaponPresentation(section, item.row, presentation);
            }
            finally { DestroyImmediate(capability); }
            section.Add(new Button(() => Run(() =>
            {
                draft.RemoveItemCapability(item.row, kind); DrawDetail(); RefreshList(); UpdateModified();
            })) { text = "移除组件", name = "item-capability-remove-" + kind });
        }
        var add = new ToolbarMenu { text = "+ 添加能力组件", name = "item-capability-add" };
        foreach (ItemCapabilityKind kind in Enum.GetValues(typeof(ItemCapabilityKind)))
        {
            var selected = kind;
            add.menu.AppendAction(CapabilityLabel(selected), _ => Run(() =>
            {
                draft.AddItemCapability(item.row, selected); DrawDetail(); RefreshList(); UpdateModified();
            }), _ => Writable && !kinds.Contains(selected) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }
        detail.Add(add);
        if (kinds.Count == 0) detail.Add(new Label("此物品尚未附加能力组件。"));
    }

    private void DrawGunCapability(VisualElement parent, int row, GunCapabilityDefinition gun)
    {
        Bind(parent, new EnumField("射击模式", gun.fireMode), value => draft.EditItemCapability<GunCapabilityDefinition>(row, c => c.fireMode = (GunFireMode)value));
        Bind(parent, new FloatField("射速（发/分钟）") { value = gun.roundsPerMinute, isDelayed = true, name = "gun-rpm" },
            value => draft.EditItemCapability<GunCapabilityDefinition>(row, c => c.roundsPerMinute = value));
        Bind(parent, new IntegerField("弹匣容量") { value = gun.magazineCapacity, isDelayed = true, name = "gun-magazine" },
            value => draft.EditItemCapability<GunCapabilityDefinition>(row, c => c.magazineCapacity = value));
        Bind(parent, new FloatField("换弹时间（秒）") { value = gun.reloadSeconds, isDelayed = true },
            value => draft.EditItemCapability<GunCapabilityDefinition>(row, c => c.reloadSeconds = value));
        Bind(parent, new FloatField("射程（米）") { value = gun.rangeMeters, isDelayed = true },
            value => draft.EditItemCapability<GunCapabilityDefinition>(row, c => c.rangeMeters = value));
        var ammunition = draft.GetItems().Where(i => draft.GetItemCapabilities(i.row).Contains(ItemCapabilityKind.Ammunition)).ToList();
        var choices = new[] { "未配置" }.Concat(ammunition.Select(i => i.name + " (" + i.key + ", ID " + i.id + ")")).ToList();
        int selected = ammunition.FindIndex(i => i.id == gun.ammunitionItem.Value) + 1;
        if (selected == 0 && gun.ammunitionItem.IsValid)
        {
            choices.Add("无效弹药 ID " + gun.ammunitionItem.Value);
            selected = choices.Count - 1;
        }
        var picker = new PopupField<string>("兼容弹药", choices, selected) { name = "gun-ammunition" };
        Bind(parent, picker, value =>
        {
            int index = choices.IndexOf(value) - 1;
            ushort id = index >= 0 && index < ammunition.Count ? (ushort)ammunition[index].id : (ushort)0;
            draft.EditItemCapability<GunCapabilityDefinition>(row, c => c.ammunitionItem = new ItemId { Value = id });
        });
    }

    private void DrawWeaponPresentation(VisualElement parent, int row, WeaponPresentationCapabilityDefinition presentation)
    {
        Bind(parent, ModelField("第一人称模型 / Prefab", presentation.firstPersonPrefab),
            value => draft.EditItemCapability<WeaponPresentationCapabilityDefinition>(row, c => c.firstPersonPrefab = value as GameObject));
        Bind(parent, AnimationField("第一人称动画控制器", presentation.firstPersonAnimations),
            value => draft.EditItemCapability<WeaponPresentationCapabilityDefinition>(row, c => c.firstPersonAnimations = value as RuntimeAnimatorController));
        Bind(parent, ModelField("第三人称模型 / Prefab", presentation.thirdPersonPrefab),
            value => draft.EditItemCapability<WeaponPresentationCapabilityDefinition>(row, c => c.thirdPersonPrefab = value as GameObject));
        Bind(parent, AnimationField("第三人称动画控制器", presentation.thirdPersonAnimations),
            value => draft.EditItemCapability<WeaponPresentationCapabilityDefinition>(row, c => c.thirdPersonAnimations = value as RuntimeAnimatorController));
        parent.Add(new HelpBox("模型和动画均可暂时留空。可引用 AnimatorController 或 AnimatorOverrideController；此处只保存配置，尚未接入动画播放。", HelpBoxMessageType.Info));
    }
    private static ObjectField ModelField(string label, GameObject value) => new ObjectField(label)
        { objectType = typeof(GameObject), allowSceneObjects = false, value = value };
    private static ObjectField AnimationField(string label, RuntimeAnimatorController value) => new ObjectField(label)
        { objectType = typeof(RuntimeAnimatorController), allowSceneObjects = false, value = value };
}
