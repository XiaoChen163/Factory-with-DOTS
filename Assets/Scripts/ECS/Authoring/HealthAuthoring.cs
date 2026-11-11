using System;
using Unity.Entities;
using UnityEngine;

/// <summary>Opt-in root health. Actual player/robot and building definitions are wired in A03/B01.</summary>
[DisallowMultipleComponent]
public sealed class HealthAuthoring : MonoBehaviour
{
    [Min(1)] public int maximum = 100;
    public ZeroHealthOutcome onZero;
    public uint faction;

    private sealed class HealthBaker : Baker<HealthAuthoring>
    {
        public override void Bake(HealthAuthoring authoring)
        {
            if (authoring.maximum < 1 || !Enum.IsDefined(typeof(ZeroHealthOutcome), authoring.onZero))
                throw new ArgumentException("Health requires a positive integer maximum and a valid zero outcome.");
            AddComponent(GetEntity(TransformUsageFlags.None), new HealthInitialization
            { Maximum = authoring.maximum, OnZero = authoring.onZero, Faction = authoring.faction });
        }
    }
}
