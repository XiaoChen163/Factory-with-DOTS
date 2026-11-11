using UnityEngine;

public enum ItemCapabilityKind : byte { Gun, WeaponPresentation, Ammunition }
public enum GunFireMode : byte { SemiAutomatic, Automatic }

// Authoring definition only. Gameplay systems own all mutable weapon state.
public abstract class ItemCapabilityDefinition : ScriptableObject
{
    public abstract ItemCapabilityKind Kind { get; }
}
