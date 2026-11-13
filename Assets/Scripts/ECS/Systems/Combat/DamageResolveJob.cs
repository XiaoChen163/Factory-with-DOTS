using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

internal struct DamageTargetGroup
{
    public Entity Target;
    public int Start;
    public int Count;
}

internal struct DamageResolveResult
{
    public HealthFailure Failure;
    public int PreviousHealth;
    public int CurrentHealth;
    public bool CausedZero;
    public VitalState Outcome;
}

/// <summary>One invocation owns an actual Entity, even if requests claim different stable identities.</summary>
[BurstCompile]
internal struct DamageResolveJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<HealthRequest> Requests;
    [ReadOnly] public NativeArray<DamageTargetGroup> Groups;
    [ReadOnly] public NativeArray<int> RequestIndices;
    // Every original request index belongs to exactly one group.
    [NativeDisableParallelForRestriction] public NativeArray<DamageResolveResult> Results;
    // The grouping guarantees that two invocations never write the same live Entity.
    [NativeDisableParallelForRestriction] public ComponentLookup<Health> Healths;
    [ReadOnly] public ComponentLookup<VitalIdentity> Identities;
    [ReadOnly] public ComponentLookup<HealthPolicy> Policies;
    [ReadOnly] public ComponentLookup<VitalStatus> States;
    [ReadOnly] public ComponentLookup<PendingVitalTransition> Pending;
    [ReadOnly] public ComponentLookup<CombatFaction> Factions;
    public bool AllowFriendlyFire;

    public void Execute(int groupIndex)
    {
        DamageTargetGroup group = Groups[groupIndex];
        Entity target = group.Target;
        bool hasLife = Healths.HasComponent(target) && Identities.HasComponent(target) &&
            Policies.HasComponent(target) && States.HasComponent(target) && Pending.HasComponent(target);
        Health health = hasLife ? Healths[target] : default;
        VitalIdentity identity = hasLife ? Identities[target] : default;
        HealthPolicy policy = hasLife ? Policies[target] : default;
        bool locked = hasLife && (States[target].Value != VitalState.Active || Pending[target].Sequence != 0);
        uint faction = Factions.HasComponent(target) ? Factions[target].Value : 0;
        int initialHealth = health.Current;

        for (int i = group.Start; i < group.Start + group.Count; i++)
        {
            int requestIndex = RequestIndices[i];
            HealthRequest request = Requests[requestIndex];
            DamageResolveResult result = default;
            if (!hasLife) result.Failure = HealthFailure.TargetMissing;
            else if (identity.Value != request.TargetIdentity.Value ||
                     identity.LifeGeneration != request.TargetIdentity.LifeGeneration)
                result.Failure = HealthFailure.IdentityMismatch;
            else if ((policy.OnZero != ZeroHealthOutcome.Death && policy.OnZero != ZeroHealthOutcome.Disable) ||
                     health.Maximum < 1 || health.Current < 0 || health.Current > health.Maximum)
                result.Failure = HealthFailure.InvalidHealth;
            else if (locked) result.Failure = HealthFailure.StateNotAllowed;
            else if (!AllowFriendlyFire && request.SourceFactionSnapshot != 0 &&
                     request.SourceFactionSnapshot == faction)
                result.Failure = HealthFailure.FriendlyFire;
            else
            {
                result.PreviousHealth = health.Current;
                long remaining = (long)health.Current - request.Amount;
                health.Current = remaining < 0 ? 0 : (int)remaining;
                result.CurrentHealth = health.Current;
                if (result.PreviousHealth > 0 && health.Current == 0)
                {
                    locked = true;
                    result.CausedZero = true;
                    result.Outcome = policy.OnZero == ZeroHealthOutcome.Death ? VitalState.Dead : VitalState.Disabled;
                }
            }
            Results[requestIndex] = result;
        }
        if (hasLife && health.Current != initialHealth) Healths[target] = health;
    }
}
