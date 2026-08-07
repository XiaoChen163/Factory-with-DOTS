using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace Factory.Tests
{
    public sealed class BeltTransferResolverTests
    {
        private static readonly int2 East = new int2(1, 0);
        private static readonly int2 South = new int2(0, -1);

        [Test]
        public void StraightLine_DoesNotMoveItemBeforeItIsReady()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity item = scenario.CreateItem();
            scenario.AddBelt(new int2(0, 0), East, item, 0.999f);
            scenario.AddBelt(new int2(1, 0), East);
            TransportStateSnapshot before = scenario.CaptureState();

            TransportTickResult result = scenario.ResolveTick();
            TransportStateSnapshot after = scenario.CaptureState();

            Assert.That(result.AcceptedTransferCount, Is.EqualTo(0));
            Assert.That(
                TransportAssertions.NodeAt(after, new int2(0, 0)).CurrentItem,
                Is.EqualTo(item));
            Assert.That(
                TransportAssertions.NodeAt(after, new int2(1, 0)).CurrentItem,
                Is.EqualTo(Entity.Null));
            TransportAssertions.AssertItemSetPreserved(before, after);
        }

        [Test]
        public void StraightLine_MovesReadyItemIntoEmptyDownstreamNode()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity item = scenario.CreateItem();
            scenario.AddBelt(new int2(0, 0), East, item, 1f);
            scenario.AddBelt(new int2(1, 0), East);
            TransportStateSnapshot before = scenario.CaptureState();

            TransportTickResult result = scenario.ResolveTick();
            TransportStateSnapshot after = scenario.CaptureState();

            TransportNodeSnapshot source =
                TransportAssertions.NodeAt(after, new int2(0, 0));
            TransportNodeSnapshot target =
                TransportAssertions.NodeAt(after, new int2(1, 0));
            Assert.That(result.AcceptedTransferCount, Is.EqualTo(1));
            Assert.That(source.CurrentItem, Is.EqualTo(Entity.Null));
            Assert.That(source.Progress, Is.EqualTo(0f));
            Assert.That(target.CurrentItem, Is.EqualTo(item));
            Assert.That(target.Progress, Is.EqualTo(0f));
            TransportAssertions.AssertItemSetPreserved(before, after);
        }

        [Test]
        public void StraightLine_MovesReadyChainFromOneSnapshot()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity firstItem = scenario.CreateItem(1);
            Entity secondItem = scenario.CreateItem(2);
            scenario.AddBelt(new int2(0, 0), East, firstItem, 1f);
            scenario.AddBelt(new int2(1, 0), East, secondItem, 1f);
            scenario.AddBelt(new int2(2, 0), East);
            TransportStateSnapshot before = scenario.CaptureState();

            TransportTickResult result = scenario.ResolveTick();
            TransportStateSnapshot after = scenario.CaptureState();

            Assert.That(result.AcceptedTransferCount, Is.EqualTo(2));
            Assert.That(
                TransportAssertions.NodeAt(after, new int2(0, 0)).CurrentItem,
                Is.EqualTo(Entity.Null));
            Assert.That(
                TransportAssertions.NodeAt(after, new int2(1, 0)).CurrentItem,
                Is.EqualTo(firstItem));
            Assert.That(
                TransportAssertions.NodeAt(after, new int2(2, 0)).CurrentItem,
                Is.EqualTo(secondItem));
            TransportAssertions.AssertItemSetPreserved(before, after);
        }

        [Test]
        public void BlockedTarget_PreservesSourceAndTargetItems()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity sourceItem = scenario.CreateItem(1);
            Entity targetItem = scenario.CreateItem(2);
            scenario.AddBelt(new int2(0, 0), East, sourceItem, 1f);
            scenario.AddBelt(new int2(1, 0), East, targetItem, 1f);
            TransportStateSnapshot before = scenario.CaptureState();

            TransportTickResult result = scenario.ResolveTick();
            TransportStateSnapshot after = scenario.CaptureState();

            Assert.That(result.AcceptedTransferCount, Is.EqualTo(0));
            Assert.That(
                TransportAssertions.NodeAt(after, new int2(0, 0)).CurrentItem,
                Is.EqualTo(sourceItem));
            Assert.That(
                TransportAssertions.NodeAt(after, new int2(1, 0)).CurrentItem,
                Is.EqualTo(targetItem));
            TransportAssertions.AssertItemSetPreserved(before, after);
        }

        [Test]
        public void BlockedTarget_DoesNotMoveUpstreamWhenTargetIsNotReady()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity sourceItem = scenario.CreateItem(1);
            Entity targetItem = scenario.CreateItem(2);
            scenario.AddBelt(new int2(0, 0), East, sourceItem, 1f);
            scenario.AddBelt(new int2(1, 0), East, targetItem, 0.5f);
            scenario.AddBelt(new int2(2, 0), East);
            TransportStateSnapshot before = scenario.CaptureState();

            TransportTickResult result = scenario.ResolveTick();
            TransportStateSnapshot after = scenario.CaptureState();

            Assert.That(result.AcceptedTransferCount, Is.EqualTo(0));
            Assert.That(
                TransportAssertions.NodeAt(after, new int2(0, 0)).CurrentItem,
                Is.EqualTo(sourceItem));
            Assert.That(
                TransportAssertions.NodeAt(after, new int2(1, 0)).CurrentItem,
                Is.EqualTo(targetItem));
            Assert.That(
                TransportAssertions.NodeAt(after, new int2(2, 0)).CurrentItem,
                Is.EqualTo(Entity.Null));
            TransportAssertions.AssertItemSetPreserved(before, after);
        }

        [Test]
        public void CompetingSources_MergerUsesRoundRobinCursor()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity straightItem = scenario.CreateItem(1);
            Entity sideItem = scenario.CreateItem(2);
            scenario.AddBelt(new int2(0, 0), East, straightItem, 1f);
            scenario.AddBelt(new int2(1, 1), South, sideItem, 1f);
            scenario.AddMerger(new int2(1, 0), East, nextInputIndex: 0);
            TransportStateSnapshot before = scenario.CaptureState();

            TransportTickResult result = scenario.ResolveTick();
            TransportStateSnapshot after = scenario.CaptureState();

            TransportNodeSnapshot merger =
                TransportAssertions.NodeAt(after, new int2(1, 0));
            Assert.That(result.AcceptedTransferCount, Is.EqualTo(1));
            Assert.That(merger.CurrentItem, Is.EqualTo(straightItem));
            Assert.That(merger.Cursor, Is.EqualTo(1));
            Assert.That(
                TransportAssertions.NodeAt(after, new int2(0, 0)).CurrentItem,
                Is.EqualTo(Entity.Null));
            Assert.That(
                TransportAssertions.NodeAt(after, new int2(1, 1)).CurrentItem,
                Is.EqualTo(sideItem));
            TransportAssertions.AssertItemSetPreserved(before, after);
        }

        [Test]
        public void CompetingSources_WinnerDoesNotDependOnInsertionOrder()
        {
            AssertSideInputWins(CreateCompetitionScenario(sideFirst: false));
            AssertSideInputWins(CreateCompetitionScenario(sideFirst: true));
        }

        private static TransportScenario CreateCompetitionScenario(
            bool sideFirst)
        {
            TransportScenario scenario = new TransportScenario();
            Entity straightItem = scenario.CreateItem(1);
            Entity sideItem = scenario.CreateItem(2);
            if (sideFirst)
            {
                scenario.AddBelt(new int2(1, 1), South, sideItem, 1f);
                scenario.AddBelt(new int2(0, 0), East, straightItem, 1f);
            }
            else
            {
                scenario.AddBelt(new int2(0, 0), East, straightItem, 1f);
                scenario.AddBelt(new int2(1, 1), South, sideItem, 1f);
            }

            scenario.AddMerger(new int2(1, 0), East, nextInputIndex: 1);
            return scenario;
        }

        private static void AssertSideInputWins(TransportScenario scenario)
        {
            using (scenario)
            {
                TransportStateSnapshot before = scenario.CaptureState();
                TransportTickResult result = scenario.ResolveTick();
                TransportStateSnapshot after = scenario.CaptureState();

                Entity straightItem = TransportAssertions.NodeAt(
                    before,
                    new int2(0, 0)).CurrentItem;
                Entity sideItem = TransportAssertions.NodeAt(
                    before,
                    new int2(1, 1)).CurrentItem;
                Assert.That(result.AcceptedTransferCount, Is.EqualTo(1));
                Assert.That(
                    TransportAssertions.NodeAt(
                        after,
                        new int2(1, 0)).CurrentItem,
                    Is.EqualTo(sideItem));
                Assert.That(
                    TransportAssertions.NodeAt(
                        after,
                        new int2(0, 0)).CurrentItem,
                    Is.EqualTo(straightItem));
                Assert.That(
                    TransportAssertions.NodeAt(
                        after,
                        new int2(1, 1)).CurrentItem,
                    Is.EqualTo(Entity.Null));
                TransportAssertions.AssertItemSetPreserved(before, after);
            }
        }
    }
}
