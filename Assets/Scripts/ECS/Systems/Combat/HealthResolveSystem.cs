using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

// A real child group, before all normal fixed simulation (including PhysicsSystemGroup).
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderFirst = true)]
[UpdateAfter(typeof(BeginFixedStepSimulationEntityCommandBufferSystem))]
public partial class CombatTickStartGroup : ComponentSystemGroup { }

// Job damage is committed after this tick's ECB playback, rather than at next tick's start.
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup), OrderLast = true)]
[UpdateAfter(typeof(EndFixedStepSimulationEntityCommandBufferSystem))]
public partial class HealthRequestFlushSystem : SystemBase
{
    protected override void OnUpdate() =>
        World.GetExistingSystemManaged<HealthResolveSystem>()?.FlushInbox();
}

[UpdateInGroup(typeof(CombatTickStartGroup))]
public partial class HealthResolveSystem : SystemBase
{
    public const int QueueCapacity = 4096;
    public const int EventCapacity = QueueCapacity * 2;
    public const int DeduplicationCapacity = 8192;
    public const ulong DeduplicationTicks = 120;

    private readonly List<HealthRequest> requests = new();
    private readonly List<HealthRequest> damageRequests = new();
    private readonly List<HealthRequest> jobRequests = new();
    private readonly Dictionary<Entity, int> damageGroupIndices = new();
    private readonly List<DamageTargetGroup> damageGroups = new();
    private readonly Queue<HealthRequest> intents = new();
    private readonly List<ScheduledTransition> transitions = new();
    private readonly Queue<HealthEvent> events = new();
    private readonly Dictionary<RequestKey, ulong> seen = new();
    private readonly Queue<(RequestKey key, ulong tick)> history = new();
    private ulong nextIdentity = 1;
    private ulong nextTransition = 1;
    private ulong nextEvent = 1;
    private int reservedEvents;
    private bool alive;

    public ulong Tick { get; private set; }
    public Entity InboxEntity { get; private set; }
    public bool AllowFriendlyFire { get; set; }
    public ulong DroppedEventCount { get; private set; }
    public int PendingRequestCount => requests.Count + damageRequests.Count + intents.Count;
    public ulong DamageBatchCount { get; private set; }
    public int LastDamageBatchRequestCount { get; private set; }
    public int LastDamageBatchTargetCount { get; private set; }

    protected override void OnCreate()
    {
        alive = true;
        InboxEntity = EntityManager.CreateEntity(typeof(CombatClock));
        EntityManager.AddBuffer<HealthRequestInbox>(InboxEntity);
    }

    /// <summary>Main-thread lifecycle entry for a new entity, never for reviving an existing life.</summary>
    public VitalIdentity InitializeTarget(Entity target, int maximum, ZeroHealthOutcome outcome,
        uint faction = 0)
    {
        RequireAlive();
        if (!EntityManager.Exists(target) || maximum < 1 ||
            !Enum.IsDefined(typeof(ZeroHealthOutcome), outcome) ||
            EntityManager.HasComponent<Health>(target) || EntityManager.HasComponent<VitalIdentity>(target) ||
            EntityManager.HasComponent<HealthPolicy>(target) || EntityManager.HasComponent<VitalStatus>(target) ||
            EntityManager.HasComponent<PendingVitalTransition>(target))
            throw new ArgumentException("Invalid new health target.");
        var identity = new VitalIdentity { Value = TakeSequence(ref nextIdentity), LifeGeneration = 1 };
        EntityManager.AddComponentData(target, new Health { Current = maximum, Maximum = maximum });
        EntityManager.AddComponentData(target, identity);
        EntityManager.AddComponentData(target, new HealthPolicy { OnZero = outcome });
        EntityManager.AddComponentData(target, new VitalStatus { Value = VitalState.Active });
        EntityManager.AddComponentData(target, new PendingVitalTransition());
        if (EntityManager.HasComponent<CombatFaction>(target))
            EntityManager.SetComponentData(target, new CombatFaction { Value = faction });
        else
            EntityManager.AddComponentData(target, new CombatFaction { Value = faction });
        return identity;
    }

    /// <summary>Fixed simulation main thread: enqueue damage for this tick's batch, other requests for K+1.</summary>
    public HealthFailure Submit(HealthRequest request)
    {
        RequireAlive();
        request.GeneratedTick = Tick;
        request.ExecuteTick = request.Kind == HealthRequestKind.Damage ? Tick : checked(Tick + 1);
        return Accept(request);
    }

    /// <summary>Capture this source at emission; execution never reads the source entity again.</summary>
    public HealthFailure SubmitDamage(HealthRequest request, in DamageSource sourceSnapshot)
    {
        RequireAlive();
        if (sourceSnapshot.Amount < 0) return HealthFailure.InvalidAmount;
        if (sourceSnapshot.Type != DamageType.Kinetic) return HealthFailure.InvalidRequest;
        if (sourceSnapshot.Amount == 0) return HealthFailure.NoEffect;
        request.Kind = HealthRequestKind.Damage;
        request.Amount = sourceSnapshot.Amount;
        request.DamageType = sourceSnapshot.Type;
        request.InstigatorIdentity = sourceSnapshot.InstigatorIdentity;
        request.SourceItemId = sourceSnapshot.SourceItemId;
        request.SourceFactionSnapshot = sourceSnapshot.FactionSnapshot;
        return Submit(request);
    }

    /// <summary>Render/input bridge: all intents acquire their generated tick at the next fixed ingress seal.</summary>
    public HealthFailure SubmitIntent(HealthRequest intent)
    {
        RequireAlive();
        HealthFailure failure = ValidateRequest(intent);
        if (failure != HealthFailure.None) return failure;
        if (PendingRequestCount >= QueueCapacity || !CanReserveResult()) return HealthFailure.QueueFull;
        reservedEvents += 2;
        intents.Enqueue(intent);
        return HealthFailure.None;
    }

    public bool TryTakeEvent(out HealthEvent value)
    {
        RequireAlive();
        if (events.Count > 0) { value = events.Dequeue(); return true; }
        value = default;
        return false;
    }

    public bool TryGetSnapshot(Entity target, out HealthSnapshot snapshot)
    {
        RequireAlive();
        snapshot = default;
        if (!HasLife(target)) return false;
        snapshot = new HealthSnapshot
        {
            Identity = EntityManager.GetComponentData<VitalIdentity>(target),
            Health = EntityManager.GetComponentData<Health>(target),
            State = EntityManager.GetComponentData<VitalStatus>(target).Value,
            HasPendingTransition = EntityManager.GetComponentData<PendingVitalTransition>(target).Sequence != 0,
            Tick = Tick
        };
        return true;
    }

    /// <summary>Collider mapping only; callers must still preserve non-damageable hit occlusion.</summary>
    public bool TryGetDamageTarget(Entity hit, out Entity root, out VitalIdentity identity)
    {
        RequireAlive();
        root = hit;
        identity = default;
        for (int depth = 0; depth < 16 && EntityManager.Exists(root); depth++)
        {
            if (HasLife(root))
            {
                identity = EntityManager.GetComponentData<VitalIdentity>(root);
                return true;
            }
            if (!EntityManager.HasComponent<DamageTargetOwner>(root)) break;
            root = EntityManager.GetComponentData<DamageTargetOwner>(root).Value;
        }
        root = Entity.Null;
        return false;
    }

    protected override void OnUpdate()
    {
        Tick = checked(Tick + 1);
        EntityManager.SetComponentData(InboxEntity, new CombatClock { Tick = Tick });
        ExpireHistory();
        InitializeAuthoredTargets();
        CommitTransitions();
        DiscardMissedDamage();
        FlushInboxCore(true);
        // Damage intents execute at this tick's end; other intents retain their extra boundary.
        while (intents.Count > 0)
        {
            HealthRequest intent = intents.Dequeue();
            reservedEvents -= 2;
            intent.GeneratedTick = Tick;
            intent.ExecuteTick = intent.Kind == HealthRequestKind.Damage ? Tick : checked(Tick + 1);
            HealthFailure failure = Accept(intent);
            if (failure != HealthFailure.None) PublishResult(intent, failure);
        }

        requests.Sort(CompareRequests);
        int consumed = 0;
        while (consumed < requests.Count && requests[consumed].ExecuteTick <= Tick)
            Resolve(requests[consumed++]);
        if (consumed > 0) requests.RemoveRange(0, consumed);
    }

    /// <summary>Batch boundary after this tick's producer dependencies and ECB playback.</summary>
    public void FlushInbox()
    {
        RequireAlive();
        FlushInboxCore(false);
        ResolveDamageBatch();
    }

    private void FlushInboxCore(bool allowPreviousDeferred)
    {
        DynamicBuffer<HealthRequestInbox> inbox = EntityManager.GetBuffer<HealthRequestInbox>(InboxEntity);
        for (int i = 0; i < inbox.Length; i++) jobRequests.Add(inbox[i].Value);
        inbox.Clear();
        // Capacity arbitration must also follow stable keys, never parallel ECB append order.
        jobRequests.Sort(CompareRequests);
        for (int i = 0; i < jobRequests.Count; i++)
        {
            HealthRequest request = jobRequests[i];
            bool current = request.GeneratedTick == Tick &&
                request.ExecuteTick == (request.Kind == HealthRequestKind.Damage ? Tick : checked(Tick + 1));
            bool previousDeferred = allowPreviousDeferred && Tick > 0 &&
                request.Kind != HealthRequestKind.Damage && request.GeneratedTick == Tick - 1 &&
                request.ExecuteTick == Tick;
            HealthFailure failure = current || previousDeferred ? Accept(request) : HealthFailure.InvalidRequest;
            if (failure != HealthFailure.None) PublishResult(request, failure);
        }
        jobRequests.Clear();
    }

    private void InitializeAuthoredTargets()
    {
        using NativeArray<Entity> targets = GetEntityQuery(ComponentType.ReadOnly<HealthInitialization>())
            .ToEntityArray(Allocator.Temp);
        foreach (Entity target in targets)
        {
            HealthInitialization initial = EntityManager.GetComponentData<HealthInitialization>(target);
            InitializeTarget(target, initial.Maximum, initial.OnZero, initial.Faction);
            EntityManager.RemoveComponent<HealthInitialization>(target);
        }
    }

    private void DiscardMissedDamage()
    {
        damageRequests.Sort(CompareRequests);
        int discarded = 0;
        while (discarded < damageRequests.Count && damageRequests[discarded].ExecuteTick < Tick)
        {
            reservedEvents -= 2;
            PublishResult(damageRequests[discarded++], HealthFailure.InvalidRequest);
        }
        if (discarded > 0) damageRequests.RemoveRange(0, discarded);
    }

    private void ResolveDamageBatch()
    {
        LastDamageBatchRequestCount = damageRequests.Count;
        LastDamageBatchTargetCount = 0;
        if (damageRequests.Count == 0) return;

        damageRequests.Sort(CompareRequests);
        damageGroupIndices.Clear();
        damageGroups.Clear();
        int count = damageRequests.Count;
        var batch = new NativeArray<HealthRequest>(count, Allocator.TempJob);
        var indices = new NativeArray<int>(count, Allocator.TempJob);
        var results = new NativeArray<DamageResolveResult>(count, Allocator.TempJob);
        using (batch)
        using (indices)
        using (results)
        {
            for (int i = 0; i < count; i++)
            {
                HealthRequest request = damageRequests[i];
                batch[i] = request;
                // Group by the actual Entity, not the claimed identity: stale or forged identities
                // must not create concurrent writers to the same component.
                if (!damageGroupIndices.TryGetValue(request.Target, out int groupIndex))
                {
                    groupIndex = damageGroups.Count;
                    damageGroupIndices.Add(request.Target, groupIndex);
                    damageGroups.Add(new DamageTargetGroup { Target = request.Target });
                }
                DamageTargetGroup group = damageGroups[groupIndex];
                group.Count++;
                damageGroups[groupIndex] = group;
            }
            var groups = new NativeArray<DamageTargetGroup>(damageGroups.Count, Allocator.TempJob);
            var offsets = new NativeArray<int>(damageGroups.Count, Allocator.Temp);
            using (groups)
            using (offsets)
            {
                int start = 0;
                for (int i = 0; i < damageGroups.Count; i++)
                {
                    DamageTargetGroup group = damageGroups[i];
                    group.Start = start;
                    start += group.Count;
                    groups[i] = group;
                }
                for (int i = 0; i < count; i++)
                {
                    int groupIndex = damageGroupIndices[batch[i].Target];
                    indices[groups[groupIndex].Start + offsets[groupIndex]] = i;
                    offsets[groupIndex]++;
                }

                // One batch boundary, scoped to the components actually read/written. Unrelated
                // factory jobs are not globally completed, and Submit never synchronizes jobs.
                EntityManager.CompleteDependencyBeforeRW<Health>();
                EntityManager.CompleteDependencyBeforeRW<PendingVitalTransition>();
                EntityManager.CompleteDependencyBeforeRO<VitalIdentity>();
                EntityManager.CompleteDependencyBeforeRO<HealthPolicy>();
                EntityManager.CompleteDependencyBeforeRO<VitalStatus>();
                EntityManager.CompleteDependencyBeforeRO<CombatFaction>();
                new DamageResolveJob
                {
                    Requests = batch, Groups = groups, RequestIndices = indices, Results = results,
                    Healths = GetComponentLookup<Health>(),
                    Identities = GetComponentLookup<VitalIdentity>(true),
                    Policies = GetComponentLookup<HealthPolicy>(true),
                    States = GetComponentLookup<VitalStatus>(true),
                    Pending = GetComponentLookup<PendingVitalTransition>(true),
                    Factions = GetComponentLookup<CombatFaction>(true),
                    AllowFriendlyFire = AllowFriendlyFire
                }.Schedule(groups.Length, 1).Complete();
                DamageBatchCount = checked(DamageBatchCount + 1);
                LastDamageBatchTargetCount = groups.Length;

                // Parallel completion order never determines event or transition sequence numbers.
                for (int i = 0; i < count; i++)
                {
                    HealthRequest request = batch[i];
                    DamageResolveResult result = results[i];
                    reservedEvents -= 2;
                    PublishResult(request, result.Failure, result.PreviousHealth, result.CurrentHealth);
                    if (!result.CausedZero) continue;
                    var pending = new PendingVitalTransition
                    {
                        Identity = request.TargetIdentity, Outcome = result.Outcome,
                        GeneratedTick = Tick, EffectiveTick = checked(Tick + 1),
                        Sequence = TakeSequence(ref nextTransition)
                    };
                    EntityManager.SetComponentData(request.Target, pending);
                    reservedEvents++;
                    transitions.Add(new ScheduledTransition { Target = request.Target, Value = pending });
                }
                damageRequests.Clear();
            }
        }
    }

    private HealthFailure Accept(HealthRequest request)
    {
        HealthFailure failure = ValidateRequest(request);
        if (failure != HealthFailure.None) return failure;
        var key = new RequestKey(request);
        if (seen.ContainsKey(key)) return HealthFailure.Duplicate;
        if (PendingRequestCount >= QueueCapacity || seen.Count >= DeduplicationCapacity || !CanReserveResult())
            return HealthFailure.QueueFull;
        seen.Add(key, Tick);
        history.Enqueue((key, Tick));
        reservedEvents += 2; // One result, plus at most one transition/respawn event.
        if (request.Kind == HealthRequestKind.Damage) damageRequests.Add(request);
        else requests.Add(request);
        return HealthFailure.None;
    }

    private static HealthFailure ValidateRequest(HealthRequest request)
    {
        if (request.Kind != HealthRequestKind.Respawn && request.Amount <= 0)
            return HealthFailure.InvalidAmount;
        if (!Enum.IsDefined(typeof(HealthRequestKind), request.Kind) ||
            request.SourceStableId == 0 || request.RequestId == 0 ||
            request.TargetIdentity.Value == 0 || request.TargetIdentity.LifeGeneration == 0 ||
            request.Kind == HealthRequestKind.Damage && request.DamageType != DamageType.Kinetic ||
            request.Kind == HealthRequestKind.Respawn && request.Amount != 0)
            return HealthFailure.InvalidRequest;
        return HealthFailure.None;
    }

    private void Resolve(HealthRequest request)
    {
        reservedEvents -= 2;
        HealthFailure failure = CheckTarget(request.Target, request.TargetIdentity);
        if (failure != HealthFailure.None) { PublishResult(request, failure); return; }
        Entity target = request.Target;
        Health health = EntityManager.GetComponentData<Health>(target);
        VitalState state = EntityManager.GetComponentData<VitalStatus>(target).Value;
        PendingVitalTransition pending = EntityManager.GetComponentData<PendingVitalTransition>(target);
        if (!ValidHealth(health)) { PublishResult(request, HealthFailure.InvalidHealth); return; }
        if (request.Kind == HealthRequestKind.Respawn)
        {
            if (state != VitalState.Dead || pending.Sequence != 0)
            { PublishResult(request, HealthFailure.StateNotAllowed); return; }
            if (request.TargetIdentity.LifeGeneration == uint.MaxValue)
            { PublishResult(request, HealthFailure.GenerationExhausted); return; }
            VitalIdentity identity = request.TargetIdentity;
            identity.LifeGeneration++;
            int previous = health.Current;
            health.Current = health.Maximum;
            EntityManager.SetComponentData(target, health);
            EntityManager.SetComponentData(target, identity);
            EntityManager.SetComponentData(target, new VitalStatus { Value = VitalState.Active });
            PublishResult(request, HealthFailure.None, previous, health.Current);
            Publish(new HealthEvent { Kind = HealthEventKind.Respawned, Target = target,
                Identity = identity, GeneratedTick = request.GeneratedTick, EffectiveTick = Tick,
                PreviousHealth = previous, CurrentHealth = health.Current, ActualAmount = health.Current - previous });
            return;
        }
        if (!VitalActivity.CanAct(state) || pending.Sequence != 0)
        { PublishResult(request, HealthFailure.StateNotAllowed); return; }
        int before = health.Current;
        health.Current = (int)Math.Min((long)health.Current + request.Amount, health.Maximum);
        EntityManager.SetComponentData(target, health);
        PublishResult(request, HealthFailure.None, before, health.Current);
    }

    private void CommitTransitions()
    {
        // Insertion follows commit order and monotonically increasing ticks.
        int count = 0;
        while (count < transitions.Count && transitions[count].Value.EffectiveTick <= Tick)
        {
            ScheduledTransition entry = transitions[count++];
            reservedEvents--;
            PendingVitalTransition transition = entry.Value;
            HealthFailure failure = CheckTarget(entry.Target, transition.Identity);
            if (failure == HealthFailure.None &&
                EntityManager.GetComponentData<PendingVitalTransition>(entry.Target).Sequence != transition.Sequence)
                failure = HealthFailure.IdentityMismatch;
            if (failure == HealthFailure.None)
            {
                EntityManager.SetComponentData(entry.Target, new VitalStatus { Value = transition.Outcome });
                EntityManager.SetComponentData(entry.Target, new PendingVitalTransition());
            }
            Publish(new HealthEvent
            {
                Kind = failure != HealthFailure.None ? HealthEventKind.TransitionDiscarded :
                    transition.Outcome == VitalState.Dead ? HealthEventKind.Death : HealthEventKind.Disabled,
                Target = entry.Target, Identity = transition.Identity, Failure = failure,
                GeneratedTick = transition.GeneratedTick, EffectiveTick = Tick,
                TransitionSequence = transition.Sequence
            });
        }
        if (count > 0) transitions.RemoveRange(0, count);
    }

    private bool HasLife(Entity target) => EntityManager.Exists(target) &&
        EntityManager.HasComponent<Health>(target) && EntityManager.HasComponent<VitalIdentity>(target) &&
        EntityManager.HasComponent<HealthPolicy>(target) && EntityManager.HasComponent<VitalStatus>(target) &&
        EntityManager.HasComponent<PendingVitalTransition>(target);

    private HealthFailure CheckTarget(Entity target, VitalIdentity expected)
    {
        if (!HasLife(target)) return HealthFailure.TargetMissing;
        VitalIdentity identity = EntityManager.GetComponentData<VitalIdentity>(target);
        if (identity.Value != expected.Value || identity.LifeGeneration != expected.LifeGeneration)
            return HealthFailure.IdentityMismatch;
        if (!Enum.IsDefined(typeof(ZeroHealthOutcome), EntityManager.GetComponentData<HealthPolicy>(target).OnZero))
            return HealthFailure.InvalidHealth;
        return HealthFailure.None;
    }

    private static bool ValidHealth(Health health) =>
        health.Maximum > 0 && health.Current >= 0 && health.Current <= health.Maximum;

    private void PublishResult(HealthRequest request, HealthFailure failure, int previous = 0, int current = 0) =>
        Publish(new HealthEvent { Kind = HealthEventKind.RequestResolved, Request = request,
            Target = request.Target, Identity = request.TargetIdentity, Failure = failure,
            GeneratedTick = request.GeneratedTick, EffectiveTick = Tick,
            PreviousHealth = previous, CurrentHealth = current,
            ActualAmount = request.Kind == HealthRequestKind.Damage ? previous - current : current - previous });

    private void Publish(HealthEvent value)
    {
        value.Sequence = TakeSequence(ref nextEvent);
        // Reserved results and effects always fit. Only unaccepted raw Job/bridge rejection
        // diagnostics can overflow; never evict an accepted request's committed event.
        if (events.Count + reservedEvents >= EventCapacity) { DroppedEventCount++; return; }
        events.Enqueue(value);
    }

    private bool CanReserveResult() => events.Count + reservedEvents <= EventCapacity - 2;

    private void ExpireHistory()
    {
        while (history.Count > 0 && Tick - history.Peek().tick > DeduplicationTicks)
            seen.Remove(history.Dequeue().key);
    }

    private static int CompareRequests(HealthRequest a, HealthRequest b)
    {
        int result = a.ExecuteTick.CompareTo(b.ExecuteTick);
        if (result == 0) result = a.SourceStableId.CompareTo(b.SourceStableId);
        if (result == 0) result = a.RequestId.CompareTo(b.RequestId);
        if (result == 0) result = a.TargetIdentity.Value.CompareTo(b.TargetIdentity.Value);
        if (result == 0) result = a.TargetIdentity.LifeGeneration.CompareTo(b.TargetIdentity.LifeGeneration);
        return result;
    }

    private static ulong TakeSequence(ref ulong next)
    {
        if (next == ulong.MaxValue) throw new InvalidOperationException("Combat sequence exhausted; no wrapping is allowed.");
        return next++;
    }

    private void RequireAlive()
    {
        if (!alive) throw new ObjectDisposedException(nameof(HealthResolveSystem));
    }

    protected override void OnDestroy()
    {
        alive = false;
        requests.Clear(); damageRequests.Clear(); jobRequests.Clear(); damageGroupIndices.Clear(); damageGroups.Clear();
        intents.Clear(); transitions.Clear(); events.Clear(); seen.Clear(); history.Clear();
    }

    private struct ScheduledTransition { public Entity Target; public PendingVitalTransition Value; }

    private readonly struct RequestKey : IEquatable<RequestKey>
    {
        private readonly ulong source, request, target;
        private readonly uint generation;
        public RequestKey(HealthRequest value)
        { source = value.SourceStableId; request = value.RequestId; target = value.TargetIdentity.Value; generation = value.TargetIdentity.LifeGeneration; }
        public bool Equals(RequestKey other) => source == other.source && request == other.request &&
            target == other.target && generation == other.generation;
        public override bool Equals(object obj) => obj is RequestKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(source, request, target, generation);
    }
}
