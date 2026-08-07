using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace Factory.Tests
{
    /// <summary>
    /// Phase 3 resolver-level tests for the Burst arbitration job path.
    /// </summary>
    public sealed class BeltTransferResolverPhase3Tests
    {
        private static readonly int2 East = new int2(1, 0);
        private static readonly int2 North = new int2(0, 1);
        private static readonly int2 West = new int2(-1, 0);
        private static readonly int2 South = new int2(0, -1);

        [Test]
        public void ArbitrationJob_ReadyChainReportsCountsAndAdvances()
        {
            using TransportScenario scenario = new TransportScenario();
            scenario.AddBelt(
                new int2(0, 0),
                East,
                scenario.CreateItem(1),
                1f);
            scenario.AddBelt(
                new int2(1, 0),
                East,
                scenario.CreateItem(2),
                1f);
            scenario.AddBelt(new int2(2, 0), East);

            TransportTickResult result = scenario.ResolveTickLinear();
            TransportStateSnapshot after = scenario.CaptureState();

            Assert.That(result.AcceptedTransferCount, Is.EqualTo(2));
            Assert.That(result.ReadyRequestCount, Is.EqualTo(2));
            Assert.That(
                TransportAssertions.NodeAt(after, new int2(0, 0))
                    .CurrentItem,
                Is.EqualTo(Entity.Null));
            Assert.That(
                TransportAssertions.NodeAt(after, new int2(2, 0))
                    .CurrentItem,
                Is.Not.EqualTo(Entity.Null));
        }

        [Test]
        public void ArbitrationJob_ClosedLoopRotatesAtomically()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity first = scenario.CreateItem(1);
            Entity second = scenario.CreateItem(2);
            Entity third = scenario.CreateItem(3);
            Entity fourth = scenario.CreateItem(4);
            scenario.AddBelt(new int2(0, 0), East, first, 1f);
            scenario.AddBelt(new int2(1, 0), North, second, 1f);
            scenario.AddBelt(new int2(1, 1), West, third, 1f);
            scenario.AddBelt(new int2(0, 1), South, fourth, 1f);

            TransportStateSnapshot before = scenario.CaptureState();
            TransportTickResult result = scenario.ResolveTickLinear();
            TransportStateSnapshot after = scenario.CaptureState();

            Assert.That(result.LoopCount, Is.EqualTo(1));
            Assert.That(result.AcceptedTransferCount, Is.EqualTo(4));
            TransportAssertions.AssertItemSetPreserved(before, after);
            Assert.That(
                TransportAssertions.NodeAt(after, new int2(1, 0))
                    .CurrentItem,
                Is.EqualTo(first));
            Assert.That(
                TransportAssertions.NodeAt(after, new int2(1, 1))
                    .CurrentItem,
                Is.EqualTo(second));
            Assert.That(
                TransportAssertions.NodeAt(after, new int2(0, 1))
                    .CurrentItem,
                Is.EqualTo(third));
            Assert.That(
                TransportAssertions.NodeAt(after, new int2(0, 0))
                    .CurrentItem,
                Is.EqualTo(fourth));
        }

        [Test]
        public void ArbitrationJob_SplitterRoundRobinAdvancesCursor()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity item = scenario.CreateItem(1);
            scenario.AddSplitter(
                int2.zero,
                East,
                item,
                nextOutputIndex: 1);
            scenario.AddBelt(new int2(1, 0), East);
            scenario.AddBelt(new int2(0, 1), North);
            scenario.AddBelt(new int2(0, -1), South);

            TransportTickResult result = scenario.ResolveTickLinear();
            TransportStateSnapshot after = scenario.CaptureState();

            Assert.That(result.AcceptedTransferCount, Is.EqualTo(1));
            Assert.That(
                TransportAssertions.NodeAt(after, new int2(0, 1))
                    .CurrentItem,
                Is.EqualTo(item));
            Assert.That(
                TransportAssertions.NodeAt(after, int2.zero).Cursor,
                Is.EqualTo(2));
        }
    }
}
