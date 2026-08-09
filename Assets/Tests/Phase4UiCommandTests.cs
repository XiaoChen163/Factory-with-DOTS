using NUnit.Framework;
using Unity.Entities;

namespace Factory.Tests
{
    public sealed class Phase4UiCommandTests : FactoryWorldFixture
    {
        public override void TearDownWorld()
        {
            PlayerCommandRuntimeServices.Detach(TestWorld);
            base.TearDownWorld();
        }

        [Test]
        public void Bus_AssignsMonotonicHeaders_AndIngressIsolatesPlayers()
        {
            Entity first = CreatePlayer(1);
            Entity second = CreatePlayer(2);
            PlayerCommandMailbox mailbox =
                PlayerCommandRuntimeServices.GetOrCreateMailbox(TestWorld);
            PlayerCommandBus secondBus = new(new PlayerId { Value = 2 }, mailbox);
            PlayerCommandBus firstBus = new(new PlayerId { Value = 1 }, mailbox);

            ulong firstRequest = firstBus.Submit(new RecipeSelectionCommand
            {
                Recipe = new RecipeId { Value = 3 }
            });
            ulong secondRequest = firstBus.Submit(new MoveItemPlayerCommand
            {
                ExpectedItemType = new ItemId { Value = 1 },
                Amount = 1
            });
            secondBus.Submit(new RecipeSelectionCommand
            {
                Recipe = new RecipeId { Value = 8 }
            });

            UpdateSystem(GetOrCreateManagedSystem<PlayerCommandIngressSystem>());

            RecipeSelectionCommand firstRecipe =
                EntityManager.GetBuffer<RecipeSelectionCommand>(first)[0];
            MoveItemPlayerCommand firstMove =
                EntityManager.GetBuffer<MoveItemPlayerCommand>(first)[0];
            RecipeSelectionCommand secondRecipe =
                EntityManager.GetBuffer<RecipeSelectionCommand>(second)[0];
            Assert.That(firstRequest, Is.EqualTo(1));
            Assert.That(secondRequest, Is.EqualTo(2));
            Assert.That(firstRecipe.Header.ClientSequence, Is.EqualTo(1));
            Assert.That(firstMove.Header.ClientSequence, Is.EqualTo(2));
            Assert.That(firstRecipe.Header.Player.Value, Is.EqualTo(1));
            Assert.That(secondRecipe.Header.Player.Value, Is.EqualTo(2));
            Assert.That(secondRecipe.Recipe.Value, Is.EqualTo(8));
        }

        [Test]
        public void RuntimeServices_SharesSequenceAcrossUiAndBuildEntryPoints()
        {
            CreatePlayer(5);
            PlayerId player = new() { Value = 5 };
            PlayerCommandBus uiEntry =
                PlayerCommandRuntimeServices.GetOrCreateBus(TestWorld, player);
            PlayerCommandBus buildEntry =
                PlayerCommandRuntimeServices.GetOrCreateBus(TestWorld, player);

            uiEntry.Submit(new RecipeSelectionCommand());
            buildEntry.Submit(new GridBuildPlayerCommand());
            UpdateSystem(GetOrCreateManagedSystem<PlayerCommandIngressSystem>());

            Entity entity = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerIdentity>()).GetSingletonEntity();
            Assert.That(EntityManager.GetBuffer<RecipeSelectionCommand>(entity)[0]
                .Header.ClientSequence, Is.EqualTo(1));
            Assert.That(EntityManager.GetBuffer<GridBuildPlayerCommand>(entity)[0]
                .Header.ClientSequence, Is.EqualTo(2));
            Assert.That(uiEntry, Is.SameAs(buildEntry));
        }

        [Test]
        public void Ingress_RejectsDuplicateSequence_AndReturnsOnlyToOwningPlayer()
        {
            Entity player = CreatePlayer(7);
            EntityManager.AddComponentData(player, new PlayerCommandSequenceState
            {
                LastAcceptedSequence = 4
            });
            PlayerCommandMailbox mailbox =
                PlayerCommandRuntimeServices.GetOrCreateMailbox(TestWorld);
            PlayerCommandBus ownerBus = new(new PlayerId { Value = 7 }, mailbox);
            PlayerCommandBus otherBus = new(new PlayerId { Value = 8 }, mailbox);
            PlayerCommandResult? ownerResult = null;
            PlayerCommandResult? otherResult = null;
            ownerBus.ResultReceived += value => ownerResult = value;
            otherBus.ResultReceived += value => otherResult = value;
            ownerBus.Submit(new RecipeSelectionCommand());

            UpdateSystem(GetOrCreateManagedSystem<PlayerCommandIngressSystem>());
            otherBus.PumpResults();
            ownerBus.PumpResults();

            Assert.That(otherResult.HasValue, Is.False);
            Assert.That(ownerResult.HasValue, Is.True);
            Assert.That(ownerResult.Value.FailureReason,
                Is.EqualTo(PlayerCommandFailureReason.SequenceDuplicate));
            Assert.That(EntityManager.GetBuffer<RecipeSelectionCommand>(player).Length, Is.Zero);
        }

        [Test]
        public void DragPrediction_RejectsOutputAndWrongRecipeInput()
        {
            ItemSlotSnapshot iron = new(0, new ItemId { Value = 1 }, 5, 64,
                default, UiSlotAccess.InsertAndExtract);
            ItemSlotSnapshot emptyInventory = new(1, default, 0, 0,
                default, UiSlotAccess.InsertAndExtract);
            ItemSlotSnapshot wrongInput = new(0, default, 0, 64,
                new ItemId { Value = 2 }, UiSlotAccess.Insert);
            ItemSlotSnapshot output = new(0, default, 0, 64,
                new ItemId { Value = 1 }, UiSlotAccess.Extract);

            Assert.That(ItemDragController.CanDrop(iron, emptyInventory), Is.True);
            Assert.That(ItemDragController.CanDrop(iron, wrongInput), Is.False);
            Assert.That(ItemDragController.CanDrop(iron, output), Is.False);
        }

        private Entity CreatePlayer(ulong id)
        {
            Entity entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, new PlayerIdentity
            {
                Value = new PlayerId { Value = id }
            });
            return entity;
        }
    }
}
