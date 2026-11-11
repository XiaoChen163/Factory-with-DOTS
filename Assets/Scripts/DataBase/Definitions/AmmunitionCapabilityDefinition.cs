using UnityEngine;

public sealed class AmmunitionCapabilityDefinition : ItemCapabilityDefinition
{
    public override ItemCapabilityKind Kind => ItemCapabilityKind.Ammunition;
    [Min(0)] public int baseDamage = 10;
}
