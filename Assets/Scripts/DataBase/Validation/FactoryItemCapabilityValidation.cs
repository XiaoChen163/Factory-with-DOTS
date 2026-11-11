using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class FactoryItemCapabilityValidation
{
    public static T Get<T>(FactoryItemTableRow item) where T : ItemCapabilityDefinition =>
        (item.capabilities ?? Array.Empty<ItemCapabilityDefinition>()).OfType<T>().FirstOrDefault();
    public static void Validate(FactoryItemTableRow[] items)
    {
        var owners = new HashSet<ItemCapabilityDefinition>();
        foreach (var item in items)
        {
            var kinds = new HashSet<ItemCapabilityKind>();
            foreach (var capability in item.capabilities ?? Array.Empty<ItemCapabilityDefinition>())
            {
                if (capability == null || !owners.Add(capability) || !kinds.Add(capability.Kind))
                    throw new InvalidDataException($"Item '{item.key}' has a null, shared or duplicate capability.");
                if (capability is GunCapabilityDefinition gun)
                {
                    if (!Enum.IsDefined(typeof(GunFireMode), gun.fireMode) || !Positive(gun.roundsPerMinute) ||
                        !Positive(gun.reloadSeconds) || !Positive(gun.rangeMeters) || gun.magazineCapacity < 1 || gun.magazineCapacity > ushort.MaxValue)
                        throw new InvalidDataException($"Item '{item.key}' has invalid gun parameters.");
                    var ammunition = items.FirstOrDefault(i => i.id == gun.ammunitionItem.Value);
                    if (!gun.ammunitionItem.IsValid || ammunition.id == 0 || Get<AmmunitionCapabilityDefinition>(ammunition) == null)
                        throw new InvalidDataException($"Gun '{item.key}' must reference an item with an ammunition capability.");
                }
                else if (capability is AmmunitionCapabilityDefinition ammo)
                {
                    if (float.IsNaN(ammo.baseDamage) || float.IsInfinity(ammo.baseDamage) || ammo.baseDamage < 0)
                        throw new InvalidDataException($"Item '{item.key}' has invalid ammunition damage.");
                }
                else if (capability is WeaponPresentationCapabilityDefinition presentation)
                {
                    if (presentation.firstPersonAnimations != null && presentation.firstPersonPrefab == null ||
                        presentation.thirdPersonAnimations != null && presentation.thirdPersonPrefab == null)
                        throw new InvalidDataException($"Item '{item.key}' has animation bindings without a held model.");
                }
                else throw new InvalidDataException($"Item '{item.key}' uses an unsupported capability type.");
            }
        }
    }
    private static bool Positive(float value) => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);
}
