using Unity.Entities;

public struct Health : IComponentData
{
    public int Current;
    public int Maximum;
}

public enum ZeroHealthOutcome : byte { Death, Disable }
public enum VitalState : byte { Active, Dead, Disabled }
public enum DamageType : byte { Kinetic }
public enum HealthRequestKind : byte { Damage, Heal, Respawn }

public struct HealthPolicy : IComponentData { public ZeroHealthOutcome OnZero; }
public struct VitalStatus : IComponentData { public VitalState Value; }

// Separate from player, building and inventory IDs. Stable for this world's lifetime.
public struct VitalIdentity : IComponentData
{
    public ulong Value;
    public uint LifeGeneration;
}

public struct CombatFaction : IComponentData { public uint Value; }
public struct DamageTargetOwner : IComponentData { public Entity Value; }

public struct DamageSource : IComponentData
{
    public int Amount;
    public DamageType Type;
    public ulong InstigatorIdentity;
    public ItemId SourceItemId;
    public uint FactionSnapshot;
}

public struct PendingVitalTransition : IComponentData
{
    public VitalIdentity Identity;
    public VitalState Outcome;
    public ulong GeneratedTick;
    public ulong EffectiveTick;
    public ulong Sequence; // Zero means no pending transition.
}

public struct HealthInitialization : IComponentData
{
    public int Maximum;
    public ZeroHealthOutcome OnZero;
    public uint Faction;
}

public struct HealthRequest
{
    public HealthRequestKind Kind;
    public Entity Target;
    public VitalIdentity TargetIdentity;
    public int Amount;
    public DamageType DamageType;
    public ulong InstigatorIdentity;
    public Entity SourceEntity;
    public ItemId SourceItemId;
    public uint SourceFactionSnapshot;
    public ulong SourceStableId;
    public ulong RequestId;
    public ulong GeneratedTick;
    public ulong ExecuteTick;
}

// Jobs may append stamped requests via an ECB to the world-owned inbox. Playback must
// complete before the following CombatTickStartGroup; use EndFixedStepSimulation ECB.
public struct HealthRequestInbox : IBufferElementData { public HealthRequest Value; }
public struct CombatClock : IComponentData { public ulong Tick; }

public enum HealthFailure : byte
{
    None, InvalidAmount, InvalidRequest, QueueFull, Duplicate,
    TargetMissing, IdentityMismatch, InvalidHealth, StateNotAllowed,
    FriendlyFire, GenerationExhausted, NoEffect
}

public enum HealthEventKind : byte
{
    RequestResolved, Death, Disabled, Respawned, TransitionDiscarded
}

public struct HealthEvent
{
    public ulong Sequence;
    public HealthEventKind Kind;
    public HealthRequest Request;
    public Entity Target;
    public VitalIdentity Identity;
    public ulong GeneratedTick;
    public ulong EffectiveTick;
    public ulong TransitionSequence;
    public HealthFailure Failure;
    public int PreviousHealth;
    public int CurrentHealth;
    public int ActualAmount;
}

public struct HealthSnapshot
{
    public VitalIdentity Identity;
    public Health Health;
    public VitalState State;
    public bool HasPendingTransition;
    public ulong Tick;
}

public static class VitalActivity
{
    // Pending zero health remains Active. Never gate simulation on Health.Current.
    public static bool CanAct(VitalState state) => state == VitalState.Active;
}
