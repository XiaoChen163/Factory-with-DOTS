using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Entities;

namespace Factory.Tests
{
    public sealed class HealthResolveTests : FactoryWorldFixture
    {
        private HealthResolveSystem resolver;
        private Entity target;
        private VitalIdentity identity;
        private ulong sequence;

        [SetUp]
        public void SetUpCombat()
        {
            resolver = GetOrCreateManagedSystem<HealthResolveSystem>();
            target = EntityManager.CreateEntity();
            identity = resolver.InitializeTarget(target, 10, ZeroHealthOutcome.Death);
            sequence = 1;
        }

        private HealthRequest Request(int amount, HealthRequestKind kind = HealthRequestKind.Damage,
            ulong source = 1) => new()
        {
            Target = target, TargetIdentity = identity, Amount = amount, Kind = kind,
            SourceStableId = source, RequestId = sequence++
        };

        private HealthSnapshot Snapshot()
        {
            Assert.That(resolver.TryGetSnapshot(target, out HealthSnapshot value), Is.True);
            return value;
        }

        private List<HealthEvent> Drain()
        {
            List<HealthEvent> result = new();
            while (resolver.TryTakeEvent(out HealthEvent value)) result.Add(value);
            return result;
        }

        private void Tick() => UpdateSystem(resolver);

        [TestCase(ZeroHealthOutcome.Death, VitalState.Dead, HealthEventKind.Death)]
        [TestCase(ZeroHealthOutcome.Disable, VitalState.Disabled, HealthEventKind.Disabled)]
        public void LethalRequestUsesTwoDistinctTickBoundaries(ZeroHealthOutcome outcome,
            VitalState expected, HealthEventKind eventKind)
        {
            EntityManager.SetComponentData(target, new HealthPolicy { OnZero = outcome });
            Assert.That(resolver.Submit(Request(15)), Is.EqualTo(HealthFailure.None));
            Assert.That(Snapshot().Health.Current, Is.EqualTo(10));
            Assert.That(Drain(), Is.Empty);
            Tick();
            Assert.That(Snapshot().Health.Current, Is.Zero);
            Assert.That(Snapshot().State, Is.EqualTo(VitalState.Active));
            Assert.That(VitalActivity.CanAct(Snapshot().State), Is.True);
            Assert.That(Snapshot().HasPendingTransition, Is.True);
            var changed = Drain().Single();
            Assert.That(changed.Kind, Is.EqualTo(HealthEventKind.RequestResolved));
            Assert.That(changed.ActualAmount, Is.EqualTo(10));
            Assert.That(changed.GeneratedTick, Is.Zero);
            Assert.That(changed.EffectiveTick, Is.EqualTo(1));
            Tick();
            Assert.That(Snapshot().State, Is.EqualTo(expected));
            Assert.That(VitalActivity.CanAct(Snapshot().State), Is.False);
            Assert.That(Snapshot().HasPendingTransition, Is.False);
            var effect = Drain().Single();
            Assert.That(effect.Kind, Is.EqualTo(eventKind));
            Assert.That(effect.GeneratedTick, Is.EqualTo(1));
            Assert.That(effect.EffectiveTick, Is.EqualTo(2));
            Tick(); Tick();
            Assert.That(Drain(), Is.Empty);
        }

        [Test]
        public void IngressSealsAtFixedTickAndCannotExecuteInThatTick()
        {
            Assert.That(resolver.SubmitIntent(Request(4)), Is.EqualTo(HealthFailure.None));
            Assert.That(resolver.Tick, Is.Zero);
            // Rendering/pumping does not advance simulation or consume pending inputs.
            for (int i = 0; i < 20; i++) Assert.That(Snapshot().Health.Current, Is.EqualTo(10));
            Tick();
            Assert.That(Snapshot().Health.Current, Is.EqualTo(10));
            Tick();
            Assert.That(Snapshot().Health.Current, Is.EqualTo(6));
            var value = Drain().Single();
            Assert.That(value.GeneratedTick, Is.EqualTo(1));
            Assert.That(value.EffectiveTick, Is.EqualTo(2));
        }

        [Test]
        public void PendingZeroRejectsFurtherDamageAndHealingAndTransitionPrecedesHealing()
        {
            resolver.Submit(Request(10)); resolver.Submit(Request(10)); resolver.Submit(Request(10, HealthRequestKind.Heal));
            Tick();
            Assert.That(Drain().Select(e => e.Failure), Is.EqualTo(new[]
                { HealthFailure.None, HealthFailure.StateNotAllowed, HealthFailure.StateNotAllowed }));
            resolver.Submit(Request(10, HealthRequestKind.Heal));
            Tick();
            var events = Drain();
            Assert.That(events[0].Kind, Is.EqualTo(HealthEventKind.Death));
            Assert.That(events[1].Failure, Is.EqualTo(HealthFailure.StateNotAllowed));
            Assert.That(Snapshot().Health.Current, Is.Zero);
        }

        [TestCase(false, 0)]
        [TestCase(true, 5)]
        public void MixedRequestsSortBySourceBeforeRequestIdRegardlessOfAppendOrder(bool healFirst, int expected)
        {
            EntityManager.SetComponentData(target, new Health { Current = 5, Maximum = 10 });
            var heal = Request(10, HealthRequestKind.Heal, healFirst ? 1UL : 2UL);
            var damage = Request(5, source: healFirst ? 2UL : 1UL);
            resolver.Submit(damage); resolver.Submit(heal);
            Tick();
            Assert.That(Snapshot().Health.Current, Is.EqualTo(expected));
        }

        [Test]
        public void DuplicateAttackIsPerTargetAndExpiresWithBoundedHistory()
        {
            var request = Request(2);
            resolver.Submit(request);
            Assert.That(resolver.Submit(request), Is.EqualTo(HealthFailure.Duplicate));
            Entity second = EntityManager.CreateEntity();
            request.Target = second;
            request.TargetIdentity = resolver.InitializeTarget(second, 10, ZeroHealthOutcome.Death);
            Assert.That(resolver.Submit(request), Is.EqualTo(HealthFailure.None));
            Tick();
            Assert.That(Snapshot().Health.Current, Is.EqualTo(8));
            Assert.That(EntityManager.GetComponentData<Health>(second).Current, Is.EqualTo(8));
            for (int i = 0; i <= (int)HealthResolveSystem.DeduplicationTicks; i++) Tick();
            Assert.That(resolver.Submit(request), Is.EqualTo(HealthFailure.None));
        }

        [TestCase(0)] [TestCase(-1)] [TestCase(int.MinValue)]
        public void NonPositiveRequestsAreRejectedWithoutMutation(int amount)
        {
            Assert.That(resolver.Submit(Request(amount)), Is.EqualTo(HealthFailure.InvalidAmount));
            Assert.That(resolver.Submit(Request(amount, HealthRequestKind.Heal)), Is.EqualTo(HealthFailure.InvalidAmount));
            Tick();
            Assert.That(Snapshot().Health.Current, Is.EqualTo(10));
            Assert.That(Drain(), Is.Empty);
        }

        [TestCase(0)] [TestCase(-1)]
        public void InvalidMaximumDoesNotPartiallyInitialize(int maximum)
        {
            Entity entity = EntityManager.CreateEntity();
            Assert.Throws<ArgumentException>(() => resolver.InitializeTarget(entity, maximum, ZeroHealthOutcome.Death));
            Assert.That(EntityManager.HasComponent<Health>(entity), Is.False);
        }

        [Test]
        public void IntegerLimitsClampUsingWideArithmetic()
        {
            EntityManager.SetComponentData(target, new Health { Current = int.MaxValue - 1, Maximum = int.MaxValue });
            resolver.Submit(Request(int.MaxValue, HealthRequestKind.Heal));
            Tick();
            Assert.That(Snapshot().Health.Current, Is.EqualTo(int.MaxValue));
            Assert.That(Drain().Single().ActualAmount, Is.EqualTo(1));
            resolver.Submit(Request(int.MaxValue)); Tick();
            Assert.That(Snapshot().Health.Current, Is.Zero);
            Assert.That(Drain().Single().ActualAmount, Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void DestroyedSourceDoesNotCancelSavedRequest()
        {
            var request = Request(4);
            request.SourceEntity = EntityManager.CreateEntity();
            resolver.Submit(request);
            EntityManager.DestroyEntity(request.SourceEntity);
            Tick();
            Assert.That(Snapshot().Health.Current, Is.EqualTo(6));
        }

        [Test]
        public void SourceZeroDamageIsNoEffectWhileZeroRequestIsInvalid()
        {
            Assert.That(resolver.SubmitDamage(Request(5), new DamageSource()), Is.EqualTo(HealthFailure.NoEffect));
            Assert.That(resolver.SubmitDamage(Request(5), new DamageSource { Amount = -1 }), Is.EqualTo(HealthFailure.InvalidAmount));
            Assert.That(resolver.PendingRequestCount, Is.Zero);
            Assert.That(resolver.SubmitDamage(Request(0), new DamageSource { Amount = 3, InstigatorIdentity = 99 }), Is.EqualTo(HealthFailure.None));
            Tick();
            var result = Drain().Single();
            Assert.That(result.Request.InstigatorIdentity, Is.EqualTo(99));
            Assert.That(Snapshot().Health.Current, Is.EqualTo(7));
        }

        [Test]
        public void TargetDestructionAndLifeChangeReturnDiagnosticFailures()
        {
            resolver.Submit(Request(4));
            identity.LifeGeneration++;
            EntityManager.SetComponentData(target, identity);
            Tick();
            Assert.That(Drain().Single().Failure, Is.EqualTo(HealthFailure.IdentityMismatch));
            resolver.Submit(Request(10)); Tick(); Drain();
            EntityManager.DestroyEntity(target);
            Tick();
            Assert.That(Drain().Single().Kind, Is.EqualTo(HealthEventKind.TransitionDiscarded));
        }

        [Test]
        public void DestroyedTargetRequestCannotHitReusedEntityIndex()
        {
            resolver.Submit(Request(4));
            EntityManager.DestroyEntity(target);
            target = EntityManager.CreateEntity();
            resolver.InitializeTarget(target, 10, ZeroHealthOutcome.Death);
            Tick();
            Assert.That(Drain().Single().Failure, Is.EqualTo(HealthFailure.TargetMissing));
            Assert.That(Snapshot().Health.Current, Is.EqualTo(10));
        }

        [Test]
        public void FactionIsEvaluatedAtExecutionUsingSavedSourceFaction()
        {
            var request = Request(4); request.SourceFactionSnapshot = 2;
            EntityManager.SetComponentData(target, new CombatFaction { Value = 1 });
            resolver.Submit(request);
            EntityManager.SetComponentData(target, new CombatFaction { Value = 2 });
            Tick();
            Assert.That(Drain().Single().Failure, Is.EqualTo(HealthFailure.FriendlyFire));
            resolver.AllowFriendlyFire = true;
            request.RequestId = sequence++;
            resolver.Submit(request); Tick();
            Assert.That(Snapshot().Health.Current, Is.EqualTo(6));
        }

        [Test]
        public void RespawnCommitsAtRequestBoundaryAndInvalidatesPreviousLife()
        {
            resolver.Submit(Request(10)); Tick(); Tick(); Drain();
            resolver.Submit(Request(0, HealthRequestKind.Respawn));
            resolver.Submit(Request(4));
            Assert.That(Snapshot().State, Is.EqualTo(VitalState.Dead));
            Tick();
            Assert.That(Snapshot().State, Is.EqualTo(VitalState.Active));
            Assert.That(Snapshot().Health.Current, Is.EqualTo(10));
            Assert.That(Snapshot().Identity.LifeGeneration, Is.EqualTo(2));
            Assert.That(Drain().Last().Failure, Is.EqualTo(HealthFailure.IdentityMismatch));
        }

        [Test]
        public void CollisionChildrenResolveToOneRootAndCyclesDoNotHang()
        {
            Entity first = EntityManager.CreateEntity(); Entity second = EntityManager.CreateEntity();
            EntityManager.AddComponentData(first, new DamageTargetOwner { Value = target });
            EntityManager.AddComponentData(second, new DamageTargetOwner { Value = target });
            Assert.That(resolver.TryGetDamageTarget(first, out Entity a, out _), Is.True);
            Assert.That(resolver.TryGetDamageTarget(second, out Entity b, out _), Is.True);
            Assert.That(a, Is.EqualTo(b));
            var request = Request(3); request.Target = a;
            resolver.Submit(request); request.Target = b;
            Assert.That(resolver.Submit(request), Is.EqualTo(HealthFailure.Duplicate));
            EntityManager.SetComponentData(first, new DamageTargetOwner { Value = second });
            EntityManager.SetComponentData(second, new DamageTargetOwner { Value = first });
            Assert.That(resolver.TryGetDamageTarget(first, out _, out _), Is.False);
        }

        [Test]
        public void JobInboxRequiresPreviousTickStampAndNeverExecutesFutureWorkEarly()
        {
            Tick();
            var valid = Request(2); valid.GeneratedTick = 1; valid.ExecuteTick = 2;
            var invalid = Request(3); invalid.GeneratedTick = 2; invalid.ExecuteTick = 2;
            var buffer = EntityManager.GetBuffer<HealthRequestInbox>(resolver.InboxEntity);
            buffer.Add(new HealthRequestInbox { Value = valid });
            buffer.Add(new HealthRequestInbox { Value = invalid });
            Tick();
            Assert.That(Snapshot().Health.Current, Is.EqualTo(8));
            Assert.That(Drain().Count(e => e.Failure == HealthFailure.InvalidRequest), Is.EqualTo(1));
        }

        [Test]
        public void EventBackpressurePreservesAcceptedResultsAndEventsDrainOnce()
        {
            for (int i = 0; i < HealthResolveSystem.QueueCapacity; i++)
                Assert.That(resolver.Submit(Request(1, HealthRequestKind.Heal)), Is.EqualTo(HealthFailure.None));
            Assert.That(resolver.Submit(Request(1)), Is.EqualTo(HealthFailure.QueueFull));
            Tick();
            int accepted = HealthResolveSystem.QueueCapacity;
            while (resolver.Submit(Request(1, HealthRequestKind.Heal)) == HealthFailure.None) accepted++;
            Tick();
            Assert.That(resolver.DroppedEventCount, Is.Zero);
            var values = Drain();
            Assert.That(values.Count, Is.EqualTo(accepted));
            Assert.That(values.Count, Is.LessThanOrEqualTo(HealthResolveSystem.EventCapacity));
            Assert.That(values.Select(e => e.Sequence), Is.Ordered.Ascending);
            Assert.That(Drain(), Is.Empty);
            Assert.That(resolver.Submit(Request(1)), Is.EqualTo(HealthFailure.None));
        }

        [Test]
        public void DifferentWorldDoesNotInheritRequestsEventsOrSequenceContext()
        {
            resolver.Submit(Request(10));
            using var other = new World("Fresh combat");
            var fresh = other.GetOrCreateSystemManaged<HealthResolveSystem>();
            Assert.That(fresh.PendingRequestCount, Is.Zero);
            Assert.That(fresh.Tick, Is.Zero);
            Assert.That(fresh.TryTakeEvent(out _), Is.False);
        }

        [Test]
        public void PresentationExportsEveryFixedTickEventOnceAndReadsCommittedSnapshot()
        {
            var export = GetOrCreateManagedSystem<HealthPresentationExportSystem>();
            List<HealthEvent> observed = new();
            export.EventReceived += observed.Add;
            resolver.Submit(Request(10)); Tick(); Tick();
            UpdateSystem(export); UpdateSystem(export);
            Assert.That(observed.Select(e => e.Kind), Is.EqualTo(new[]
                { HealthEventKind.RequestResolved, HealthEventKind.Death }));
            Assert.That(export.TryGetSnapshot(target, out HealthSnapshot snapshot), Is.True);
            Assert.That(snapshot.State, Is.EqualTo(VitalState.Dead));
        }

        [Test]
        public void CombatGroupCommitsBeforeNormalFixedSimulationAndMultipleTicksRetainAllEvents()
        {
            var fixedGroup = GetOrCreateManagedSystem<FixedStepSimulationSystemGroup>();
            fixedGroup.RateManager = null;
            fixedGroup.AddSystemToUpdateList(GetOrCreateManagedSystem<BeginFixedStepSimulationEntityCommandBufferSystem>());
            var combatGroup = GetOrCreateManagedSystem<CombatTickStartGroup>();
            var probe = GetOrCreateManagedSystem<HealthActionProbeSystem>();
            probe.Target = target;
            combatGroup.AddSystemToUpdateList(resolver);
            // Deliberately append in the wrong order; group attributes must correct it.
            fixedGroup.AddSystemToUpdateList(probe);
            fixedGroup.AddSystemToUpdateList(combatGroup);
            fixedGroup.SortSystems();
            resolver.Submit(Request(10));
            fixedGroup.Update(); fixedGroup.Update();
            Assert.That(probe.Actions, Is.EqualTo(1));
            Assert.That(Drain().Select(e => e.Kind), Is.EqualTo(new[]
                { HealthEventKind.RequestResolved, HealthEventKind.Death }));
        }
    }

    [DisableAutoCreation]
    [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
    public partial class HealthActionProbeSystem : SystemBase
    {
        public Entity Target;
        public int Actions;
        protected override void OnUpdate()
        {
            if (VitalActivity.CanAct(EntityManager.GetComponentData<VitalStatus>(Target).Value)) Actions++;
        }
    }
}
