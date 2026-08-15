using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace Factory.Tests
{
    public sealed class MultiLevelStage5Tests : FactoryWorldFixture
    {
        private static readonly int2 East = new int2(1, 0);

        [Test]
        public void ExplicitCrossLevelEdge_TransfersBetweenDisconnectedBelts()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity item = scenario.CreateItem();
            Entity source = scenario.AddBelt(
                new GridCell(2, 0, 3), East, item, 1f);
            Entity target = scenario.AddBelt(
                new GridCell(2, 4, 3), East);
            scenario.AddExplicitEdge(source, target);

            TransportTickResult result = scenario.ResolveTickLinear();

            Assert.That(result.AcceptedTransferCount, Is.EqualTo(1));
            Assert.That(scenario.LinearTopologyEdgeCount, Is.EqualTo(1));
            Assert.That(scenario.BeltStates[0].CurrentItem, Is.EqualTo(Entity.Null));
            Assert.That(scenario.BeltStates[1].CurrentItem, Is.EqualTo(item));
        }

        [Test]
        public void ExplicitVerticalStyleChain_MovesIntermediateNodesLikePlanarBelts()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity firstItem = scenario.CreateItem();
            Entity secondItem = scenario.CreateItem();
            Entity thirdItem = scenario.CreateItem();
            Entity source = scenario.AddBelt(
                new GridCell(4, 0, 4), East, firstItem, 1f);
            Entity middle0 = scenario.AddBelt(
                new GridCell(4, 1, 4), East, secondItem, 1f);
            Entity middle1 = scenario.AddBelt(
                new GridCell(4, 2, 4), East, thirdItem, 1f);
            Entity target = scenario.AddBelt(
                new GridCell(4, 3, 4), East);
            scenario.AddExplicitEdge(source, middle0);
            scenario.AddExplicitEdge(middle0, middle1);
            scenario.AddExplicitEdge(middle1, target);

            TransportTickResult result = scenario.ResolveTickLinear();

            Assert.That(result.AcceptedTransferCount, Is.EqualTo(3));
            Assert.That(scenario.LinearTopologyEdgeCount, Is.EqualTo(3));
            Assert.That(scenario.BeltStates[0].CurrentItem, Is.EqualTo(Entity.Null));
            Assert.That(scenario.BeltStates[1].CurrentItem, Is.EqualTo(firstItem));
            Assert.That(scenario.BeltStates[2].CurrentItem, Is.EqualTo(secondItem));
            Assert.That(scenario.BeltStates[3].CurrentItem, Is.EqualTo(thirdItem));
        }

        [Test]
        public void ExplicitVerticalStyleChain_PropagatesBlockedTail()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity firstItem = scenario.CreateItem();
            Entity secondItem = scenario.CreateItem();
            Entity blockedItem = scenario.CreateItem();
            Entity source = scenario.AddBelt(
                new GridCell(5, 0, 5), East, firstItem, 1f);
            Entity middle = scenario.AddBelt(
                new GridCell(5, 1, 5), East, secondItem, 1f);
            Entity target = scenario.AddBelt(
                new GridCell(5, 2, 5), East, blockedItem, 0.5f);
            scenario.AddExplicitEdge(source, middle);
            scenario.AddExplicitEdge(middle, target);

            TransportTickResult result = scenario.ResolveTickLinear();

            Assert.That(result.AcceptedTransferCount, Is.Zero);
            Assert.That(scenario.BeltStates[0].CurrentItem, Is.EqualTo(firstItem));
            Assert.That(scenario.BeltStates[1].CurrentItem, Is.EqualTo(secondItem));
            Assert.That(scenario.BeltStates[2].CurrentItem, Is.EqualTo(blockedItem));
        }

        [Test]
        public void ExplicitCrossLevelRing_MovesAtomically()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity[] nodes = new Entity[4];
            for (int i = 0; i < nodes.Length; i++)
            {
                nodes[i] = scenario.AddBelt(
                    new GridCell(7, i, 7),
                    East,
                    scenario.CreateItem(),
                    1f);
            }
            for (int i = 0; i < nodes.Length; i++)
            {
                scenario.AddExplicitEdge(nodes[i], nodes[(i + 1) % nodes.Length]);
            }

            TransportTickResult result = scenario.ResolveTickLinear();

            Assert.That(result.LoopCount, Is.EqualTo(1));
            Assert.That(result.AcceptedTransferCount, Is.EqualTo(4));
            Assert.That(scenario.LinearTopologyEdgeCount, Is.EqualTo(4));
            for (int i = 0; i < scenario.BeltStates.Count; i++)
            {
                Assert.That(scenario.BeltStates[i].CurrentItem, Is.Not.EqualTo(Entity.Null));
            }
        }

        [Test]
        public void ExplicitEdge_TakesPriorityOverPlanarAutoConnection()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity planarItem = scenario.CreateItem();
            Entity explicitItem = scenario.CreateItem();
            scenario.AddBelt(
                new GridCell(0, 0, 0), East, planarItem, 1f);
            Entity explicitSource = scenario.AddBelt(
                new GridCell(9, 2, 9), East, explicitItem, 1f);
            Entity target = scenario.AddBelt(new GridCell(1, 0, 0), East);
            scenario.AddExplicitEdge(explicitSource, target);

            TransportTickResult result = scenario.ResolveTickLinear();

            Assert.That(result.AcceptedTransferCount, Is.EqualTo(1));
            Assert.That(scenario.BeltStates[0].CurrentItem, Is.EqualTo(planarItem));
            Assert.That(scenario.BeltStates[1].CurrentItem, Is.EqualTo(Entity.Null));
            Assert.That(scenario.BeltStates[2].CurrentItem, Is.EqualTo(explicitItem));
        }

        [Test]
        public void LongExplicitChain_InspectsCandidatesLinearly()
        {
            const int nodeCount = 256;
            using TransportScenario scenario = new TransportScenario();
            Entity previous = Entity.Null;
            for (int i = 0; i < nodeCount; i++)
            {
                Entity item = i < nodeCount - 1
                    ? scenario.CreateItem()
                    : Entity.Null;
                Entity current = scenario.AddBelt(
                    new GridCell(11, i, 11),
                    East,
                    item,
                    item == Entity.Null ? 0f : 1f);
                if (previous != Entity.Null)
                {
                    scenario.AddExplicitEdge(previous, current);
                }
                previous = current;
            }

            TransportTickResult result = scenario.ResolveTickLinear();

            Assert.That(result.AcceptedTransferCount, Is.EqualTo(nodeCount - 1));
            Assert.That(scenario.LinearTopologyEdgeCount, Is.EqualTo(nodeCount - 1));
            Assert.That(
                scenario.LinearCandidateInspectionCount,
                Is.LessThanOrEqualTo(nodeCount * 3));
        }

        [Test]
        public void ExplicitEdgeBuffer_RebuildsOnlyAfterTransportRevisionChanges()
        {
            Entity grid = EntityManager.CreateEntity();
            EntityManager.AddComponentData(grid, new GridDefinition
            {
                Size = new int2(16, 16),
                CellSize = 1f,
                Revision = 1
            });
            EntityManager.AddComponentData(
                grid,
                new BuildingOccupancyRevision { Value = 1 });
            EntityManager.AddComponentData(
                grid,
                new TransportTopologyRevision { Value = 1 });
            DynamicBuffer<TransportExplicitEdge> edges =
                EntityManager.AddBuffer<TransportExplicitEdge>(grid);
            Entity source = CreateBelt(new GridCell(1, 0, 1), East);
            Entity target = CreateBelt(new GridCell(1, 2, 1), East);

            BeltTransferSystem transfer =
                GetOrCreateManagedSystem<BeltTransferSystem>();
            UpdateSystem(transfer);
            Assert.That(transfer.TransportTopologyEdgeCount, Is.Zero);
            int rebuilds = transfer.TransportTopologyRebuildCount;

            edges = EntityManager.GetBuffer<TransportExplicitEdge>(grid);
            edges.Add(new TransportExplicitEdge
            {
                Source = source,
                Target = target
            });
            UpdateSystem(transfer);
            Assert.That(transfer.TransportTopologyRebuildCount, Is.EqualTo(rebuilds));
            Assert.That(transfer.TransportTopologyEdgeCount, Is.Zero);

            EntityManager.SetComponentData(
                grid,
                new TransportTopologyRevision { Value = 2 });
            UpdateSystem(transfer);
            Assert.That(
                transfer.TransportTopologyRebuildCount,
                Is.EqualTo(rebuilds + 1));
            Assert.That(transfer.TransportTopologyEdgeCount, Is.EqualTo(1));
        }
    }
}
