using UnityEngine;

public sealed class WeaponPresentationCapabilityDefinition : ItemCapabilityDefinition
{
    public override ItemCapabilityKind Kind => ItemCapabilityKind.WeaponPresentation;
    public GameObject firstPersonPrefab;
    public RuntimeAnimatorController firstPersonAnimations;
    public GameObject thirdPersonPrefab;
    public RuntimeAnimatorController thirdPersonAnimations;
}
