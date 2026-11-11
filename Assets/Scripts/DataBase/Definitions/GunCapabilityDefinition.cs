using UnityEngine;

public sealed class GunCapabilityDefinition : ItemCapabilityDefinition
{
    public override ItemCapabilityKind Kind => ItemCapabilityKind.Gun;
    public GunFireMode fireMode = GunFireMode.SemiAutomatic;
    [Min(1)] public float roundsPerMinute = 600;
    [Min(1)] public int magazineCapacity = 30;
    [Min(0.01f)] public float reloadSeconds = 2;
    [Min(0.01f)] public float rangeMeters = 100;
    public ItemId ammunitionItem;
}
