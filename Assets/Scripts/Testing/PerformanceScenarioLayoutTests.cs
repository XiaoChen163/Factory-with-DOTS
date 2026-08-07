using System.Collections.Generic;
using NUnit.Framework;
using Unity.Mathematics;

namespace Factory.Tests
{
    public sealed class PerformanceScenarioLayoutTests
    {
        [Test]
        public void HalfLoadedScenario_UsesMinimal64By64Grid()
        {
            FactoryPerformanceScenarioDefinition definition =
                FactoryPerformanceScenarioLayout.Create(
                    FactoryPerformanceScenario.Mk4SerpentineHalfLoaded);

            Assert.That(definition.GridSize, Is.EqualTo(new int2(64, 64)));
            Assert.That(definition.Scale, Is.EqualTo(64));
            Assert.That(definition.ScaleUnit, Is.EqualTo("side"));
            Assert.That(definition.BeltCount, Is.EqualTo(4096));
            Assert.That(definition.Mk4BeltCount, Is.EqualTo(4096));
            Assert.That(definition.Mk1BeltCount, Is.Zero);
            Assert.That(definition.InitialItemCells.Length, Is.EqualTo(2048));
            AssertUniquePlacementAnchors(definition);
            AssertInitialItemsUseBeltCells(definition);
        }

        [Test]
        public void HalfLoadedScenario_AcceptsScaleParameter()
        {
            FactoryPerformanceScenarioDefinition definition =
                FactoryPerformanceScenarioLayout.Create(
                    FactoryPerformanceScenario.Mk4SerpentineHalfLoaded,
                    8);

            Assert.That(definition.GridSize, Is.EqualTo(new int2(8, 8)));
            Assert.That(definition.Scale, Is.EqualTo(8));
            Assert.That(definition.ScaleUnit, Is.EqualTo("side"));
            Assert.That(definition.BeltCount, Is.EqualTo(64));
            Assert.That(definition.Mk4BeltCount, Is.EqualTo(64));
            Assert.That(definition.InitialItemCells, Has.Length.EqualTo(32));
            AssertUniquePlacementAnchors(definition);
            AssertInitialItemsUseBeltCells(definition);
        }

        [Test]
        public void F16Scenario_HasSixteenCompact256CellBranches()
        {
            FactoryPerformanceScenarioDefinition definition =
                FactoryPerformanceScenarioLayout.Create(
                    FactoryPerformanceScenario.Mk4F16Branches);

            Assert.That(definition.GridSize, Is.EqualTo(new int2(96, 96)));
            Assert.That(definition.Scale, Is.EqualTo(16));
            Assert.That(definition.ScaleUnit, Is.EqualTo("branches"));
            Assert.That(definition.SplitterCount, Is.EqualTo(16));
            Assert.That(definition.BranchLengths, Has.Length.EqualTo(16));
            Assert.That(definition.BranchLengths, Has.All.EqualTo(256));
            Assert.That(definition.BeltCount, Is.EqualTo(5165));
            Assert.That(definition.Mk4BeltCount, Is.EqualTo(5165));
            Assert.That(definition.InitialItemCells.Length, Is.EqualTo(1024));
            AssertUniquePlacementAnchors(definition);
            AssertInitialItemsUseBeltCells(definition);
        }

        [Test]
        public void F16Scenario_IgnoresScaleParameter()
        {
            FactoryPerformanceScenarioDefinition definition =
                FactoryPerformanceScenarioLayout.Create(
                    FactoryPerformanceScenario.Mk4F16Branches,
                    64,
                    50);

            Assert.That(definition.GridSize, Is.EqualTo(new int2(96, 96)));
            Assert.That(definition.Scale, Is.EqualTo(16));
            Assert.That(definition.ScaleUnit, Is.EqualTo("branches"));
            Assert.That(definition.BranchLengths, Has.Length.EqualTo(16));
            Assert.That(definition.BeltCount, Is.EqualTo(5165));
            AssertUniquePlacementAnchors(definition);
            AssertInitialItemsUseBeltCells(definition);
        }

        [Test]
        public void BlockingScenario_Has4096Mk4BeltsThenMk1AndStorage()
        {
            FactoryPerformanceScenarioDefinition definition =
                FactoryPerformanceScenarioLayout.Create(
                    FactoryPerformanceScenario.Mk4SerpentineBlockedByMk1);

            Assert.That(definition.GridSize, Is.EqualTo(new int2(67, 64)));
            Assert.That(definition.Scale, Is.EqualTo(64));
            Assert.That(definition.ScaleUnit, Is.EqualTo("side"));
            Assert.That(definition.BeltCount, Is.EqualTo(4097));
            Assert.That(definition.Mk4BeltCount, Is.EqualTo(4096));
            Assert.That(definition.Mk1BeltCount, Is.EqualTo(1));
            Assert.That(definition.StorageCount, Is.EqualTo(1));
            Assert.That(definition.InitialItemCells.Length, Is.EqualTo(4096));
            AssertUniquePlacementAnchors(definition);
            AssertInitialItemsUseBeltCells(definition);
        }

        [Test]
        public void BlockingScenario_AcceptsScaleParameter()
        {
            FactoryPerformanceScenarioDefinition definition =
                FactoryPerformanceScenarioLayout.Create(
                    FactoryPerformanceScenario.Mk4SerpentineBlockedByMk1,
                    8);

            Assert.That(definition.GridSize, Is.EqualTo(new int2(11, 8)));
            Assert.That(definition.Scale, Is.EqualTo(8));
            Assert.That(definition.ScaleUnit, Is.EqualTo("side"));
            Assert.That(definition.BeltCount, Is.EqualTo(65));
            Assert.That(definition.Mk4BeltCount, Is.EqualTo(64));
            Assert.That(definition.Mk1BeltCount, Is.EqualTo(1));
            Assert.That(definition.StorageCount, Is.EqualTo(1));
            Assert.That(definition.InitialItemCells, Has.Length.EqualTo(64));
            AssertUniquePlacementAnchors(definition);
            AssertInitialItemsUseBeltCells(definition);
        }

        [TestCase(0, 0)]
        [TestCase(50, 64)]
        [TestCase(100, 128)]
        public void ScalableStraight_UsesRequestedLoad(
            int loadPercent,
            int expectedItems)
        {
            FactoryPerformanceScenarioDefinition definition =
                FactoryPerformanceScenarioLayout.Create(
                    FactoryPerformanceScenario.ScalableStraight,
                    128,
                    loadPercent);

            Assert.That(definition.GridSize, Is.EqualTo(new int2(128, 1)));
            Assert.That(definition.Scale, Is.EqualTo(128));
            Assert.That(definition.ScaleUnit, Is.EqualTo("belts"));
            Assert.That(definition.BeltCount, Is.EqualTo(128));
            Assert.That(definition.InitialItemCells, Has.Length.EqualTo(expectedItems));
            AssertUniquePlacementAnchors(definition);
            AssertInitialItemsUseTransportCells(definition);
        }

        [Test]
        public void MixedJunctionScenario_Has512NodesAndTenPercentJunctions()
        {
            FactoryPerformanceScenarioDefinition definition =
                FactoryPerformanceScenarioLayout.Create(
                    FactoryPerformanceScenario.MixedJunctions512);

            Assert.That(definition.Scale, Is.EqualTo(5));
            Assert.That(definition.ScaleUnit, Is.EqualTo("modules_per_side"));
            Assert.That(definition.BeltCount, Is.EqualTo(462));
            Assert.That(definition.MergerCount, Is.EqualTo(25));
            Assert.That(definition.SplitterCount, Is.EqualTo(25));
            Assert.That(
                definition.BeltCount + definition.MergerCount +
                definition.SplitterCount,
                Is.EqualTo(512));
            Assert.That(definition.InitialItemCells, Has.Length.EqualTo(256));
            AssertUniquePlacementAnchors(definition);
            AssertInitialItemsUseTransportCells(definition);
            AssertTransportOutputsStayInsideNetwork(definition);
        }

        [Test]
        public void MixedJunctionScenario_AcceptsScaleParameter()
        {
            FactoryPerformanceScenarioDefinition definition =
                FactoryPerformanceScenarioLayout.Create(
                    FactoryPerformanceScenario.MixedJunctions512,
                    2,
                    50);

            Assert.That(definition.GridSize, Is.EqualTo(new int2(29, 9)));
            Assert.That(definition.Scale, Is.EqualTo(2));
            Assert.That(definition.ScaleUnit, Is.EqualTo("modules_per_side"));
            Assert.That(definition.BeltCount, Is.EqualTo(110));
            Assert.That(definition.MergerCount, Is.EqualTo(4));
            Assert.That(definition.SplitterCount, Is.EqualTo(4));
            Assert.That(
                definition.BeltCount + definition.MergerCount +
                definition.SplitterCount,
                Is.EqualTo(118));
            Assert.That(definition.InitialItemCells, Has.Length.EqualTo(59));
            AssertUniquePlacementAnchors(definition);
            AssertInitialItemsUseTransportCells(definition);
            AssertTransportOutputsStayInsideNetwork(definition);
        }

        [Test]
        public void FullLoopScenario_IsClosedAndContains4096LoadedBelts()
        {
            FactoryPerformanceScenarioDefinition definition =
                FactoryPerformanceScenarioLayout.Create(
                    FactoryPerformanceScenario.Mk4FullLoop4096);

            Assert.That(definition.GridSize, Is.EqualTo(new int2(64, 64)));
            Assert.That(definition.Scale, Is.EqualTo(64));
            Assert.That(definition.ScaleUnit, Is.EqualTo("side"));
            Assert.That(definition.BeltCount, Is.EqualTo(4096));
            Assert.That(definition.InitialItemCells, Has.Length.EqualTo(4096));

            AssertTransportOutputsStayInsideNetwork(definition);
        }

        [Test]
        public void FullLoopScenario_AcceptsScaleParameter()
        {
            FactoryPerformanceScenarioDefinition definition =
                FactoryPerformanceScenarioLayout.Create(
                    FactoryPerformanceScenario.Mk4FullLoop4096,
                    8,
                    100);

            Assert.That(definition.GridSize, Is.EqualTo(new int2(8, 8)));
            Assert.That(definition.Scale, Is.EqualTo(8));
            Assert.That(definition.ScaleUnit, Is.EqualTo("side"));
            Assert.That(definition.BeltCount, Is.EqualTo(64));
            Assert.That(definition.InitialItemCells, Has.Length.EqualTo(64));
            AssertUniquePlacementAnchors(definition);
            AssertTransportOutputsStayInsideNetwork(definition);
        }

        [Test]
        public void FullLoopScenario_AcceptsScaleAboveLegacyLimit()
        {
            FactoryPerformanceScenarioDefinition definition =
                FactoryPerformanceScenarioLayout.Create(
                    FactoryPerformanceScenario.Mk4FullLoop4096,
                    130,
                    100);

            Assert.That(definition.GridSize, Is.EqualTo(new int2(130, 130)));
            Assert.That(definition.Scale, Is.EqualTo(130));
            Assert.That(definition.BeltCount, Is.EqualTo(16900));
            Assert.That(definition.InitialItemCells, Has.Length.EqualTo(16900));
            AssertUniquePlacementAnchors(definition);
            AssertTransportOutputsStayInsideNetwork(definition);
        }

        [Test]
        public void ProducerConsumerScenario_Has64CompleteLanes()
        {
            FactoryPerformanceScenarioDefinition definition =
                FactoryPerformanceScenarioLayout.Create(
                    FactoryPerformanceScenario.ProducerConsumer);

            Assert.That(definition.GridSize, Is.EqualTo(new int2(22, 191)));
            Assert.That(definition.Scale, Is.EqualTo(64));
            Assert.That(definition.ScaleUnit, Is.EqualTo("lanes"));
            Assert.That(definition.BeltCount, Is.EqualTo(1024));
            Assert.That(definition.MinerCount, Is.EqualTo(64));
            Assert.That(definition.FurnaceCount, Is.EqualTo(64));
            Assert.That(definition.ProcessorCount, Is.EqualTo(128));
            Assert.That(definition.StorageCount, Is.EqualTo(64));
            Assert.That(definition.InitialItemCells, Is.Empty);
            AssertUniquePlacementAnchors(definition);
        }

        [Test]
        public void ProducerConsumerScenario_AcceptsScaleParameter()
        {
            FactoryPerformanceScenarioDefinition definition =
                FactoryPerformanceScenarioLayout.Create(
                    FactoryPerformanceScenario.ProducerConsumer,
                    4);

            Assert.That(definition.GridSize, Is.EqualTo(new int2(22, 11)));
            Assert.That(definition.Scale, Is.EqualTo(4));
            Assert.That(definition.ScaleUnit, Is.EqualTo("lanes"));
            Assert.That(definition.BeltCount, Is.EqualTo(64));
            Assert.That(definition.MinerCount, Is.EqualTo(4));
            Assert.That(definition.FurnaceCount, Is.EqualTo(4));
            Assert.That(definition.ProcessorCount, Is.EqualTo(8));
            Assert.That(definition.StorageCount, Is.EqualTo(4));
            Assert.That(definition.InitialItemCells, Is.Empty);
            AssertUniquePlacementAnchors(definition);
        }

        [Test]
        public void ProducerConsumerScenario_AcceptsScaleAboveLegacyLimit()
        {
            FactoryPerformanceScenarioDefinition definition =
                FactoryPerformanceScenarioLayout.Create(
                    FactoryPerformanceScenario.ProducerConsumer,
                    257);

            Assert.That(definition.GridSize, Is.EqualTo(new int2(22, 770)));
            Assert.That(definition.Scale, Is.EqualTo(257));
            Assert.That(definition.BeltCount, Is.EqualTo(4112));
            Assert.That(definition.MinerCount, Is.EqualTo(257));
            Assert.That(definition.FurnaceCount, Is.EqualTo(257));
            Assert.That(definition.ProcessorCount, Is.EqualTo(514));
            Assert.That(definition.StorageCount, Is.EqualTo(257));
            AssertUniquePlacementAnchors(definition);
        }

        [Test]
        public void ContinuousBeltBuildScenario_UsesScaleForCountAndLength()
        {
            FactoryPerformanceScenarioDefinition definition =
                FactoryPerformanceScenarioLayout.Create(
                    FactoryPerformanceScenario.ContinuousBeltBuild);

            Assert.That(definition.GridSize, Is.EqualTo(new int2(64, 127)));
            Assert.That(definition.Scale, Is.EqualTo(64));
            Assert.That(definition.ScaleUnit, Is.EqualTo("belts"));
            Assert.That(definition.BeltCount, Is.Zero);
            Assert.That(definition.Placements, Is.Empty);
            Assert.That(definition.InitialItemCells, Is.Empty);
            Assert.That(definition.TimeDelaySeconds, Is.EqualTo(0.25f));
            Assert.That(definition.DragSeconds, Is.EqualTo(0.25f));
            Assert.That(definition.DemolitionOrder, Is.Zero);
        }

        [Test]
        public void ContinuousBeltBuildScenario_AcceptsScaleParameter()
        {
            FactoryPerformanceScenarioDefinition definition =
                FactoryPerformanceScenarioLayout.Create(
                    FactoryPerformanceScenario.ContinuousBeltBuild,
                    8);

            Assert.That(definition.GridSize, Is.EqualTo(new int2(8, 15)));
            Assert.That(definition.Scale, Is.EqualTo(8));
            Assert.That(definition.ScaleUnit, Is.EqualTo("belts"));
            Assert.That(definition.BeltCount, Is.Zero);
            Assert.That(definition.Placements, Is.Empty);
            Assert.That(definition.InitialItemCells, Is.Empty);
        }

        private static void AssertUniquePlacementAnchors(
            FactoryPerformanceScenarioDefinition definition)
        {
            HashSet<int2> cells = new HashSet<int2>();
            for (int i = 0; i < definition.Placements.Length; i++)
            {
                FactoryPerformancePlacement placement =
                    definition.Placements[i];
                Assert.That(
                    cells.Add(placement.Cell),
                    Is.True,
                    "Duplicate placement anchor at " + placement.Cell + ".");
                Assert.That(placement.Cell.x, Is.InRange(0, definition.GridSize.x - 1));
                Assert.That(placement.Cell.y, Is.InRange(0, definition.GridSize.y - 1));
            }
        }

        private static void AssertInitialItemsUseBeltCells(
            FactoryPerformanceScenarioDefinition definition)
        {
            AssertInitialItemsUseTransportCells(definition);
        }

        private static void AssertInitialItemsUseTransportCells(
            FactoryPerformanceScenarioDefinition definition)
        {
            HashSet<int2> transportCells = GetTransportCells(definition);

            HashSet<int2> itemCells = new HashSet<int2>();
            for (int i = 0; i < definition.InitialItemCells.Length; i++)
            {
                int2 cell = definition.InitialItemCells[i];
                Assert.That(transportCells.Contains(cell), Is.True);
                Assert.That(itemCells.Add(cell), Is.True);
            }
        }

        private static HashSet<int2> GetTransportCells(
            FactoryPerformanceScenarioDefinition definition)
        {
            HashSet<int2> result = new HashSet<int2>();
            for (int i = 0; i < definition.Placements.Length; i++)
            {
                FactoryPerformancePlacement placement = definition.Placements[i];
                if (placement.Kind == BuildingKind.Belt ||
                    placement.Kind == BuildingKind.Merger ||
                    placement.Kind == BuildingKind.Splitter)
                {
                    result.Add(placement.Cell);
                }
            }

            return result;
        }

        private static void AssertTransportOutputsStayInsideNetwork(
            FactoryPerformanceScenarioDefinition definition)
        {
            HashSet<int2> transportCells = GetTransportCells(definition);
            for (int i = 0; i < definition.Placements.Length; i++)
            {
                FactoryPerformancePlacement placement = definition.Placements[i];
                if (placement.Kind != BuildingKind.Belt &&
                    placement.Kind != BuildingKind.Merger &&
                    placement.Kind != BuildingKind.Splitter)
                {
                    continue;
                }

                int2 forward = EcsGridUtility.Rotate(
                    new int2(1, 0),
                    placement.QuarterTurns);
                Assert.That(
                    transportCells.Contains(placement.Cell + forward),
                    Is.True,
                    "Transport output is open at " + placement.Cell + ".");

                if (placement.Kind != BuildingKind.Splitter)
                {
                    continue;
                }

                int2 left = EcsGridUtility.Rotate(forward, 1);
                int2 right = EcsGridUtility.Rotate(forward, 3);
                Assert.That(
                    transportCells.Contains(placement.Cell + left),
                    Is.True,
                    "Splitter left output is open at " + placement.Cell + ".");
                Assert.That(
                    transportCells.Contains(placement.Cell + right),
                    Is.True,
                    "Splitter right output is open at " + placement.Cell + ".");
            }
        }
    }
}
