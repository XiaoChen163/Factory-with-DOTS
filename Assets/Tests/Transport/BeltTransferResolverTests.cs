using System;
using System.Diagnostics;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace Factory.Tests
{
    public sealed class BeltTransferResolverTests
    {
        private static readonly int2 East = new int2(1, 0);
        private static readonly int2 North = new int2(0, 1);
        private static readonly int2 West = new int2(-1, 0);
        private static readonly int2 South = new int2(0, -1);

        [Test]
        public void StraightLine_DoesNotMoveItemBeforeItIsReady()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity item = scenario.CreateItem();
            scenario.AddBelt(new GridCell(0, 0, 0), East, item, 0.999f);
            scenario.AddBelt(new GridCell(1, 0, 0), East);
            TransportStateSnapshot before = scenario.CaptureState();

            TransportTickResult result = scenario.ResolveTick();
            TransportStateSnapshot after = scenario.CaptureState();

            Assert.That(result.AcceptedTransferCount, Is.EqualTo(0));
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(0, 0, 0)).CurrentItem,
                Is.EqualTo(item));
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(1, 0, 0)).CurrentItem,
                Is.EqualTo(Entity.Null));
            TransportAssertions.AssertItemSetPreserved(before, after);
        }

        [Test]
        public void StraightLine_MovesReadyItemIntoEmptyDownstreamNode()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity item = scenario.CreateItem();
            scenario.AddBelt(new GridCell(0, 0, 0), East, item, 1f);
            scenario.AddBelt(new GridCell(1, 0, 0), East);
            TransportStateSnapshot before = scenario.CaptureState();

            TransportTickResult result = scenario.ResolveTick();
            TransportStateSnapshot after = scenario.CaptureState();

            TransportNodeSnapshot source =
                TransportAssertions.NodeAt(after, new GridCell(0, 0, 0));
            TransportNodeSnapshot target =
                TransportAssertions.NodeAt(after, new GridCell(1, 0, 0));
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
            scenario.AddBelt(new GridCell(0, 0, 0), East, firstItem, 1f);
            scenario.AddBelt(new GridCell(1, 0, 0), East, secondItem, 1f);
            scenario.AddBelt(new GridCell(2, 0, 0), East);
            TransportStateSnapshot before = scenario.CaptureState();

            TransportTickResult result = scenario.ResolveTick();
            TransportStateSnapshot after = scenario.CaptureState();

            Assert.That(result.AcceptedTransferCount, Is.EqualTo(2));
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(0, 0, 0)).CurrentItem,
                Is.EqualTo(Entity.Null));
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(1, 0, 0)).CurrentItem,
                Is.EqualTo(firstItem));
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(2, 0, 0)).CurrentItem,
                Is.EqualTo(secondItem));
            TransportAssertions.AssertItemSetPreserved(before, after);
        }

        [Test]
        public void BlockedTarget_PreservesSourceAndTargetItems()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity sourceItem = scenario.CreateItem(1);
            Entity targetItem = scenario.CreateItem(2);
            scenario.AddBelt(new GridCell(0, 0, 0), East, sourceItem, 1f);
            scenario.AddBelt(new GridCell(1, 0, 0), East, targetItem, 1f);
            TransportStateSnapshot before = scenario.CaptureState();

            TransportTickResult result = scenario.ResolveTick();
            TransportStateSnapshot after = scenario.CaptureState();

            Assert.That(result.AcceptedTransferCount, Is.EqualTo(0));
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(0, 0, 0)).CurrentItem,
                Is.EqualTo(sourceItem));
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(1, 0, 0)).CurrentItem,
                Is.EqualTo(targetItem));
            TransportAssertions.AssertItemSetPreserved(before, after);
        }

        [Test]
        public void BlockedTarget_DoesNotMoveUpstreamWhenTargetIsNotReady()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity sourceItem = scenario.CreateItem(1);
            Entity targetItem = scenario.CreateItem(2);
            scenario.AddBelt(new GridCell(0, 0, 0), East, sourceItem, 1f);
            scenario.AddBelt(new GridCell(1, 0, 0), East, targetItem, 0.5f);
            scenario.AddBelt(new GridCell(2, 0, 0), East);
            TransportStateSnapshot before = scenario.CaptureState();

            TransportTickResult result = scenario.ResolveTick();
            TransportStateSnapshot after = scenario.CaptureState();

            Assert.That(result.AcceptedTransferCount, Is.EqualTo(0));
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(0, 0, 0)).CurrentItem,
                Is.EqualTo(sourceItem));
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(1, 0, 0)).CurrentItem,
                Is.EqualTo(targetItem));
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(2, 0, 0)).CurrentItem,
                Is.EqualTo(Entity.Null));
            TransportAssertions.AssertItemSetPreserved(before, after);
        }

        [Test]
        public void CompetingSources_MergerUsesRoundRobinCursor()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity straightItem = scenario.CreateItem(1);
            Entity sideItem = scenario.CreateItem(2);
            scenario.AddBelt(new GridCell(0, 0, 0), East, straightItem, 1f);
            scenario.AddBelt(new GridCell(1, 0, 1), South, sideItem, 1f);
            scenario.AddMerger(new GridCell(1, 0, 0), East, nextInputIndex: 0);
            TransportStateSnapshot before = scenario.CaptureState();

            TransportTickResult result = scenario.ResolveTick();
            TransportStateSnapshot after = scenario.CaptureState();

            TransportNodeSnapshot merger =
                TransportAssertions.NodeAt(after, new GridCell(1, 0, 0));
            Assert.That(result.AcceptedTransferCount, Is.EqualTo(1));
            Assert.That(merger.CurrentItem, Is.EqualTo(straightItem));
            Assert.That(merger.Cursor, Is.EqualTo(1));
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(0, 0, 0)).CurrentItem,
                Is.EqualTo(Entity.Null));
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(1, 0, 1)).CurrentItem,
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
                scenario.AddBelt(new GridCell(1, 0, 1), South, sideItem, 1f);
                scenario.AddBelt(new GridCell(0, 0, 0), East, straightItem, 1f);
            }
            else
            {
                scenario.AddBelt(new GridCell(0, 0, 0), East, straightItem, 1f);
                scenario.AddBelt(new GridCell(1, 0, 1), South, sideItem, 1f);
            }

            scenario.AddMerger(new GridCell(1, 0, 0), East, nextInputIndex: 1);
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
                    new GridCell(0, 0, 0)).CurrentItem;
                Entity sideItem = TransportAssertions.NodeAt(
                    before,
                    new GridCell(1, 0, 1)).CurrentItem;
                Assert.That(result.AcceptedTransferCount, Is.EqualTo(1));
                Assert.That(
                    TransportAssertions.NodeAt(
                        after,
                        new GridCell(1, 0, 0)).CurrentItem,
                    Is.EqualTo(sideItem));
                Assert.That(
                    TransportAssertions.NodeAt(
                        after,
                        new GridCell(0, 0, 0)).CurrentItem,
                    Is.EqualTo(straightItem));
                Assert.That(
                    TransportAssertions.NodeAt(
                        after,
                        new GridCell(1, 0, 1)).CurrentItem,
                    Is.EqualTo(Entity.Null));
                TransportAssertions.AssertItemSetPreserved(before, after);
            }
        }

        [Test]
        public void StraightReadyChain_MatchesLegacyResolver()
        {
            AssertResolversEquivalent(scenario =>
            {
                Entity first = scenario.CreateItem(1);
                Entity second = scenario.CreateItem(2);
                scenario.AddBelt(new GridCell(0, 0, 0), East, first, 1f);
                scenario.AddBelt(new GridCell(1, 0, 0), East, second, 1f);
                scenario.AddBelt(new GridCell(2, 0, 0), East);
            });
        }

        [Test]
        public void BlockedChain_MatchesLegacyResolver()
        {
            AssertResolversEquivalent(scenario =>
            {
                Entity first = scenario.CreateItem(1);
                Entity second = scenario.CreateItem(2);
                scenario.AddBelt(new GridCell(0, 0, 0), East, first, 1f);
                scenario.AddBelt(new GridCell(1, 0, 0), East, second, 0.5f);
                scenario.AddBelt(new GridCell(2, 0, 0), East);
            });
        }

        [Test]
        public void MergerRoundRobin_MatchesLegacyResolver()
        {
            AssertResolversEquivalent(scenario =>
            {
                Entity straight = scenario.CreateItem(1);
                Entity side = scenario.CreateItem(2);
                scenario.AddBelt(
                    new GridCell(0, 0, 0),
                    East,
                    straight,
                    1f);
                scenario.AddBelt(
                    new GridCell(1, 0, 1),
                    South,
                    side,
                    1f);
                scenario.AddMerger(
                    new GridCell(1, 0, 0),
                    East,
                    nextInputIndex: 1);
            });
        }

        [Test]
        public void SplitterRoundRobin_MatchesLegacyResolver()
        {
            AssertResolversEquivalent(scenario =>
            {
                Entity item = scenario.CreateItem();
                scenario.AddSplitter(
                    new GridCell(int2.zero),
                    East,
                    item,
                    nextOutputIndex: 1);
                scenario.AddBelt(new GridCell(1, 0, 0), East);
                scenario.AddBelt(new GridCell(0, 0, 1), North);
                scenario.AddBelt(new GridCell(0, 0, -1), South);
            });
        }

        [Test]
        public void SplitterConflictFallback_MatchesLegacyResolver()
        {
            AssertResolversEquivalent(scenario =>
            {
                Entity splitterItem = scenario.CreateItem(1);
                Entity sideItem = scenario.CreateItem(2);
                scenario.AddSplitter(
                    new GridCell(int2.zero),
                    East,
                    splitterItem,
                    nextOutputIndex: 0);
                scenario.AddBelt(
                    new GridCell(1, 0, 1),
                    South,
                    sideItem,
                    1f);
                scenario.AddBelt(new GridCell(0, 0, -1), South);
                scenario.AddMerger(
                    new GridCell(1, 0, 0),
                    East,
                    nextInputIndex: 1);
            });
        }

        [Test]
        public void FullBeltLoop_MatchesLegacyResolver()
        {
            AssertResolversEquivalent(scenario =>
            {
                scenario.AddBelt(
                    new GridCell(0, 0, 0),
                    East,
                    scenario.CreateItem(1),
                    1f);
                scenario.AddBelt(
                    new GridCell(1, 0, 0),
                    North,
                    scenario.CreateItem(2),
                    1f);
                scenario.AddBelt(
                    new GridCell(1, 0, 1),
                    West,
                    scenario.CreateItem(3),
                    1f);
                scenario.AddBelt(
                    new GridCell(0, 0, 1),
                    South,
                    scenario.CreateItem(4),
                    1f);
            });
        }

        [Test]
        public void Topology_RebuildsOnlyWhenRevisionChanges()
        {
            using TransportScenario scenario = new TransportScenario();
            scenario.AddBelt(new GridCell(0, 0, 0), East);
            scenario.AddBelt(new GridCell(1, 0, 0), East);

            scenario.ResolveTickLinear(revision: 7);
            Assert.That(scenario.LinearTopologyRebuildCount, Is.EqualTo(1));

            scenario.ResolveTickLinear(revision: 7);
            Assert.That(scenario.LinearTopologyRebuildCount, Is.EqualTo(1));

            scenario.ResolveTickLinear(revision: 8);
            Assert.That(scenario.LinearTopologyRebuildCount, Is.EqualTo(2));
        }

        [Test]
        public void CandidateSelection_WorkScalesLinearlyTo4096Nodes()
        {
            int[] nodeCounts = { 128, 512, 1024, 4096 };
            for (int scaleIndex = 0;
                 scaleIndex < nodeCounts.Length;
                 scaleIndex++)
            {
                int nodeCount = nodeCounts[scaleIndex];
                using TransportScenario scenario = new TransportScenario(
                    $"Linear scale {nodeCount}");
                for (int i = 0; i < nodeCount; i++)
                {
                    scenario.AddBelt(
                        new GridCell(i, 0, 0),
                        East,
                        scenario.CreateItem(),
                        1f);
                }

                scenario.ResolveTickLinear();
                Stopwatch stopwatch = Stopwatch.StartNew();
                const int sampleTicks = 5;
                for (int tick = 0; tick < sampleTicks; tick++)
                {
                    TransportTickResult result =
                        scenario.ResolveTickLinear();
                    Assert.That(
                        result.AcceptedTransferCount,
                        Is.EqualTo(0));
                }
                stopwatch.Stop();

                int inspectedInputs =
                    scenario.LinearCandidateInspectionCount;
                Assert.That(
                    inspectedInputs,
                    Is.EqualTo(nodeCount - 1));
                Assert.That(
                    scenario.LinearRoutingPassCount,
                    Is.EqualTo(1));
                TestContext.Out.WriteLine(
                    $"Linear resolver {nodeCount}: " +
                    $"{stopwatch.Elapsed.TotalMilliseconds / sampleTicks:F4} " +
                    $"ms/tick, inspected={inspectedInputs}");
            }
        }

        private static void AssertResolversEquivalent(
            Action<TransportScenario> arrange)
        {
            using TransportScenario legacy =
                new TransportScenario("Legacy resolver");
            using TransportScenario linear =
                new TransportScenario("Linear resolver");
            arrange(legacy);
            arrange(linear);

            TransportStateSnapshot legacyBefore = legacy.CaptureState();
            TransportStateSnapshot linearBefore = linear.CaptureState();
            AssertScenarioStatesEquivalent(
                legacy,
                legacyBefore,
                linear,
                linearBefore);

            TransportTickResult legacyResult = legacy.ResolveTick();
            TransportTickResult linearResult = linear.ResolveTickLinear();
            TransportStateSnapshot legacyAfter = legacy.CaptureState();
            TransportStateSnapshot linearAfter = linear.CaptureState();

            Assert.That(
                linearResult.LoopCount,
                Is.EqualTo(legacyResult.LoopCount));
            Assert.That(
                linearResult.AcceptedTransferCount,
                Is.EqualTo(legacyResult.AcceptedTransferCount));
            AssertScenarioStatesEquivalent(
                legacy,
                legacyAfter,
                linear,
                linearAfter);
        }

        private static void AssertScenarioStatesEquivalent(
            TransportScenario expectedScenario,
            TransportStateSnapshot expected,
            TransportScenario actualScenario,
            TransportStateSnapshot actual)
        {
            Assert.That(actual.Nodes.Length, Is.EqualTo(expected.Nodes.Length));
            for (int i = 0; i < expected.Nodes.Length; i++)
            {
                TransportNodeSnapshot expectedNode = expected.Nodes[i];
                TransportNodeSnapshot actualNode = actual.Nodes[i];
                Assert.That(actualNode.Kind, Is.EqualTo(expectedNode.Kind));
                Assert.That(actualNode.Cell, Is.EqualTo(expectedNode.Cell));
                Assert.That(
                    actualScenario.GetItemOrdinal(actualNode.CurrentItem),
                    Is.EqualTo(expectedScenario.GetItemOrdinal(
                        expectedNode.CurrentItem)));
                Assert.That(
                    actualNode.Progress,
                    Is.EqualTo(expectedNode.Progress).Within(0.00001f));
                Assert.That(actualNode.Cursor, Is.EqualTo(expectedNode.Cursor));
            }
        }

        [Test]
        public void ArbitrationJob_ReadyChainReportsCountsAndAdvances()
        {
            using TransportScenario scenario = new TransportScenario();
            scenario.AddBelt(
                new GridCell(0, 0, 0),
                East,
                scenario.CreateItem(1),
                1f);
            scenario.AddBelt(
                new GridCell(1, 0, 0),
                East,
                scenario.CreateItem(2),
                1f);
            scenario.AddBelt(new GridCell(2, 0, 0), East);

            TransportTickResult result = scenario.ResolveTickLinear();
            TransportStateSnapshot after = scenario.CaptureState();

            Assert.That(result.AcceptedTransferCount, Is.EqualTo(2));
            Assert.That(result.ReadyRequestCount, Is.EqualTo(2));
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(0, 0, 0))
                    .CurrentItem,
                Is.EqualTo(Entity.Null));
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(2, 0, 0))
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
            scenario.AddBelt(new GridCell(0, 0, 0), East, first, 1f);
            scenario.AddBelt(new GridCell(1, 0, 0), North, second, 1f);
            scenario.AddBelt(new GridCell(1, 0, 1), West, third, 1f);
            scenario.AddBelt(new GridCell(0, 0, 1), South, fourth, 1f);

            TransportStateSnapshot before = scenario.CaptureState();
            TransportTickResult result = scenario.ResolveTickLinear();
            TransportStateSnapshot after = scenario.CaptureState();

            Assert.That(result.LoopCount, Is.EqualTo(1));
            Assert.That(result.AcceptedTransferCount, Is.EqualTo(4));
            TransportAssertions.AssertItemSetPreserved(before, after);
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(1, 0, 0))
                    .CurrentItem,
                Is.EqualTo(first));
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(1, 0, 1))
                    .CurrentItem,
                Is.EqualTo(second));
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(0, 0, 1))
                    .CurrentItem,
                Is.EqualTo(third));
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(0, 0, 0))
                    .CurrentItem,
                Is.EqualTo(fourth));
        }

        [Test]
        public void ArbitrationJob_SplitterRoundRobinAdvancesCursor()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity item = scenario.CreateItem(1);
            scenario.AddSplitter(
                new GridCell(int2.zero),
                East,
                item,
                nextOutputIndex: 1);
            scenario.AddBelt(new GridCell(1, 0, 0), East);
            scenario.AddBelt(new GridCell(0, 0, 1), North);
            scenario.AddBelt(new GridCell(0, 0, -1), South);

            TransportTickResult result = scenario.ResolveTickLinear();
            TransportStateSnapshot after = scenario.CaptureState();

            Assert.That(result.AcceptedTransferCount, Is.EqualTo(1));
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(0, 0, 1))
                    .CurrentItem,
                Is.EqualTo(item));
            Assert.That(
                TransportAssertions.NodeAt(after, new GridCell(int2.zero)).Cursor,
                Is.EqualTo(2));
        }
    }
}
