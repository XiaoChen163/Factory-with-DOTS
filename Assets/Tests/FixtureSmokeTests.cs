using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;

namespace Factory.Tests
{
    public sealed class FixtureSmokeTests : FactoryWorldFixture
    {
        [Test]
        public void TransportScenario_AdvancesOneStraightTransfer()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity item = scenario.CreateItem();
            scenario.AddBelt(new int2(0, 0), new int2(1, 0), item, 1f);
            scenario.AddBelt(new int2(1, 0), new int2(1, 0));
            TransportStateSnapshot before = scenario.CaptureState();

            TransportTickResult result = scenario.ResolveTick();
            TransportStateSnapshot after = scenario.CaptureState();

            Assert.That(result.AcceptedTransferCount, Is.EqualTo(1));
            Assert.That(after.Nodes[0].CurrentItem, Is.EqualTo(Entity.Null));
            Assert.That(after.Nodes[1].CurrentItem, Is.EqualTo(item));
            TransportAssertions.AssertItemSetPreserved(before, after);
        }

        [Test]
        public void FactoryWorldFixture_CreatesIsolatedTransportEntities()
        {
            Entity item = CreateItem();
            Entity belt = CreateBelt(
                new int2(2, 3),
                new int2(1, 0),
                item,
                1f);

            Assert.That(EntityManager.Exists(item), Is.True);
            Assert.That(EntityManager.HasComponent<Item>(item), Is.True);
            Assert.That(
                EntityManager.GetComponentData<BeltState>(belt).CurrentItem,
                Is.EqualTo(item));
        }
    }
}
