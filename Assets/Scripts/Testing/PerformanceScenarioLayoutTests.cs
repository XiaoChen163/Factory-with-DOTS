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
            Assert.That(definition.BeltCount, Is.EqualTo(4096));
            Assert.That(definition.Mk4BeltCount, Is.EqualTo(4096));
            Assert.That(definition.Mk1BeltCount, Is.Zero);
            Assert.That(definition.InitialItemCells.Length, Is.EqualTo(2048));
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
        public void BlockingScenario_Has4096Mk4BeltsThenMk1AndStorage()
        {
            FactoryPerformanceScenarioDefinition definition =
                FactoryPerformanceScenarioLayout.Create(
                    FactoryPerformanceScenario.Mk4SerpentineBlockedByMk1);

            Assert.That(definition.GridSize, Is.EqualTo(new int2(67, 64)));
            Assert.That(definition.BeltCount, Is.EqualTo(4097));
            Assert.That(definition.Mk4BeltCount, Is.EqualTo(4096));
            Assert.That(definition.Mk1BeltCount, Is.EqualTo(1));
            Assert.That(definition.StorageCount, Is.EqualTo(1));
            Assert.That(definition.InitialItemCells.Length, Is.EqualTo(4096));
            AssertUniquePlacementAnchors(definition);
            AssertInitialItemsUseBeltCells(definition);
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
            HashSet<int2> beltCells = new HashSet<int2>();
            for (int i = 0; i < definition.Placements.Length; i++)
            {
                FactoryPerformancePlacement placement =
                    definition.Placements[i];
                if (placement.Kind == BuildingKind.Belt)
                {
                    beltCells.Add(placement.Cell);
                }
            }

            HashSet<int2> itemCells = new HashSet<int2>();
            for (int i = 0; i < definition.InitialItemCells.Length; i++)
            {
                int2 cell = definition.InitialItemCells[i];
                Assert.That(beltCells.Contains(cell), Is.True);
                Assert.That(itemCells.Add(cell), Is.True);
            }
        }
    }
}
