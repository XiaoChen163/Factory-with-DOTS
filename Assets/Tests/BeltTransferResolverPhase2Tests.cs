using System;
using System.Diagnostics;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace Factory.Tests
{
    public sealed class BeltTransferResolverPhase2Tests
    {
        private static readonly int2 East = new int2(1, 0);
        private static readonly int2 North = new int2(0, 1);
        private static readonly int2 West = new int2(-1, 0);
        private static readonly int2 South = new int2(0, -1);

        [Test]
        public void StraightReadyChain_MatchesLegacyResolver()
        {
            AssertResolversEquivalent(scenario =>
            {
                Entity first = scenario.CreateItem(1);
                Entity second = scenario.CreateItem(2);
                scenario.AddBelt(new int2(0, 0), East, first, 1f);
                scenario.AddBelt(new int2(1, 0), East, second, 1f);
                scenario.AddBelt(new int2(2, 0), East);
            });
        }

        [Test]
        public void BlockedChain_MatchesLegacyResolver()
        {
            AssertResolversEquivalent(scenario =>
            {
                Entity first = scenario.CreateItem(1);
                Entity second = scenario.CreateItem(2);
                scenario.AddBelt(new int2(0, 0), East, first, 1f);
                scenario.AddBelt(new int2(1, 0), East, second, 0.5f);
                scenario.AddBelt(new int2(2, 0), East);
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
                    new int2(0, 0),
                    East,
                    straight,
                    1f);
                scenario.AddBelt(
                    new int2(1, 1),
                    South,
                    side,
                    1f);
                scenario.AddMerger(
                    new int2(1, 0),
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
                    int2.zero,
                    East,
                    item,
                    nextOutputIndex: 1);
                scenario.AddBelt(new int2(1, 0), East);
                scenario.AddBelt(new int2(0, 1), North);
                scenario.AddBelt(new int2(0, -1), South);
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
                    int2.zero,
                    East,
                    splitterItem,
                    nextOutputIndex: 0);
                scenario.AddBelt(
                    new int2(1, 1),
                    South,
                    sideItem,
                    1f);
                scenario.AddBelt(new int2(0, -1), South);
                scenario.AddMerger(
                    new int2(1, 0),
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
                    new int2(0, 0),
                    East,
                    scenario.CreateItem(1),
                    1f);
                scenario.AddBelt(
                    new int2(1, 0),
                    North,
                    scenario.CreateItem(2),
                    1f);
                scenario.AddBelt(
                    new int2(1, 1),
                    West,
                    scenario.CreateItem(3),
                    1f);
                scenario.AddBelt(
                    new int2(0, 1),
                    South,
                    scenario.CreateItem(4),
                    1f);
            });
        }

        [Test]
        public void Topology_RebuildsOnlyWhenRevisionChanges()
        {
            using TransportScenario scenario = new TransportScenario();
            scenario.AddBelt(new int2(0, 0), East);
            scenario.AddBelt(new int2(1, 0), East);

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
                        new int2(i, 0),
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
                    $"Phase2 resolver {nodeCount}: " +
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
    }
}
