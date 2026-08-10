using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;

namespace Factory.Tests
{
    public sealed class Phase1UiDataModelTests : FactoryWorldFixture
    {
        private BlobAssetReference<FactoryDatabaseBlob> database;

        public override void TearDownWorld()
        {
            base.TearDownWorld();
            if (database.IsCreated)
                database.Dispose();
        }

        [Test]
        public void MultiInputOutputRecipe_ConsumesAndPublishesAtomically()
        {
            database = CreateMultiRecipeDatabase();
            Entity processor = EntityManager.CreateEntity();
            DynamicBuffer<ProcessorItemSlot> slots =
                EntityManager.AddBuffer<ProcessorItemSlot>(processor);
            ItemProcessState process = new ItemProcessState
            {
                SelectedRecipeIndex = -1,
                ActiveRecipeIndex = -1,
                Status = ItemProcessStatus.Idle
            };
            ref FactoryDatabaseBlob blob = ref database.Value;
            FactoryRecipeRangeBlob range = blob.RecipeRangesByMachine[1];

            Assert.That(ItemProcessUtility.TrySelectRecipe(
                0, range, ref blob, slots, ref process), Is.True);
            Assert.That(slots.Length, Is.EqualTo(4));

            ProcessorItemSlot first = slots[0];
            first.Count = first.RequiredOrProducedCount;
            slots[0] = first;
            Assert.That(ItemProcessUtility.TryStart(
                0, 1000, blob.Recipes[0], slots, ref process), Is.False);
            Assert.That(slots[0].Count, Is.EqualTo(first.Count),
                "A missing later input must not consume an earlier input.");

            ProcessorItemSlot second = slots[1];
            second.Count = second.RequiredOrProducedCount;
            slots[1] = second;
            Assert.That(ItemProcessUtility.TryStart(
                0, 1000, blob.Recipes[0], slots, ref process), Is.True);
            Assert.That(slots[0].Count, Is.Zero);
            Assert.That(slots[1].Count, Is.Zero);

            Assert.That(ItemProcessUtility.AdvanceOneTick(ref process), Is.True);
            Assert.That(
                ItemProcessUtility.PublishCompletedOutputs(slots, ref process),
                Is.True);
            Assert.That(slots[2].Count, Is.EqualTo(2));
            Assert.That(slots[3].Count, Is.EqualTo(3));
            Assert.That(process.Status, Is.EqualTo(ItemProcessStatus.Idle));
        }

        [Test]
        public void MultiOutputCapacityFailure_DoesNotConsumeAnyInput()
        {
            database = CreateMultiRecipeDatabase();
            Entity processor = EntityManager.CreateEntity();
            DynamicBuffer<ProcessorItemSlot> slots =
                EntityManager.AddBuffer<ProcessorItemSlot>(processor);
            ItemProcessState process = new ItemProcessState
            {
                SelectedRecipeIndex = -1,
                ActiveRecipeIndex = -1,
                Status = ItemProcessStatus.Idle
            };
            ref FactoryDatabaseBlob blob = ref database.Value;
            FactoryRecipeRangeBlob range = blob.RecipeRangesByMachine[1];
            ItemProcessUtility.TrySelectRecipe(
                0, range, ref blob, slots, ref process);
            for (int i = 0; i < 2; i++)
            {
                ProcessorItemSlot input = slots[i];
                input.Count = input.RequiredOrProducedCount;
                slots[i] = input;
            }
            ProcessorItemSlot blockedOutput = slots[3];
            blockedOutput.Count = blockedOutput.Capacity;
            slots[3] = blockedOutput;

            Assert.That(ItemProcessUtility.TryStart(
                0, 1000, blob.Recipes[0], slots, ref process), Is.False);
            Assert.That(slots[0].Count, Is.EqualTo(slots[0].RequiredOrProducedCount));
            Assert.That(slots[1].Count, Is.EqualTo(slots[1].RequiredOrProducedCount));
        }

        [Test]
        public void RecipeChange_StopsProductionAndReturnsInputsToPlayerInventory()
        {
            database = CreateMultiRecipeDatabase();
            Entity processor = EntityManager.CreateEntity();
            EntityManager.AddBuffer<ProcessorItemSlot>(processor);
            Entity player = CreateInventoryOwner(1, 2);
            DynamicBuffer<ProcessorItemSlot> processorSlots =
                EntityManager.GetBuffer<ProcessorItemSlot>(processor);
            DynamicBuffer<InventorySlot> playerSlots =
                EntityManager.GetBuffer<InventorySlot>(player);
            playerSlots[0] = new InventorySlot
            {
                ItemType = new ItemId { Value = 1 },
                Count = 63
            };
            playerSlots[1] = new InventorySlot
            {
                ItemType = new ItemId { Value = 2 },
                Count = 64
            };
            PlayerInventory inventory = new PlayerInventory { SlotCount = 2 };
            ItemProcessState process = new ItemProcessState
            {
                SelectedRecipeIndex = -1,
                ActiveRecipeIndex = -1,
                Status = ItemProcessStatus.Idle
            };
            ref FactoryDatabaseBlob blob = ref database.Value;
            FactoryRecipeRangeBlob range = blob.RecipeRangesByMachine[1];
            Assert.That(ItemProcessUtility.TrySelectRecipe(
                0, range, ref blob, processorSlots, ref process), Is.True);

            ProcessorItemSlot firstInput = processorSlots[0];
            firstInput.Count = 5;
            processorSlots[0] = firstInput;
            ProcessorItemSlot secondInput = processorSlots[1];
            secondInput.Count = 4;
            processorSlots[1] = secondInput;
            Assert.That(ItemProcessUtility.TryStart(
                0, 1000, blob.Recipes[0], processorSlots, ref process), Is.True);

            Assert.That(ItemProcessUtility.TryChangeRecipe(
                1,
                range,
                ref blob,
                processorSlots,
                ref process,
                playerSlots,
                ref inventory), Is.True);

            Assert.That(process.Status, Is.EqualTo(ItemProcessStatus.Idle));
            Assert.That(process.ElapsedTicks, Is.Zero);
            Assert.That(process.DurationTicks, Is.Zero);
            Assert.That(process.ActiveRecipeIndex, Is.EqualTo(-1));
            Assert.That(process.SelectedRecipeIndex, Is.EqualTo(1));
            Assert.That(processorSlots.Length, Is.EqualTo(2));
            Assert.That(processorSlots[0].Count, Is.Zero);
            Assert.That(processorSlots[1].Count, Is.Zero);
            Assert.That(SumItem(playerSlots, 1), Is.EqualTo(68));
            Assert.That(SumItem(playerSlots, 2), Is.EqualTo(68));
            Assert.That(inventory.Revision, Is.EqualTo(1));
            Assert.That(inventory.SlotCount, Is.EqualTo(4));
        }

        [Test]
        public void RecipeChange_ReturnsCompletedOutputWithoutRefundingConsumedInputs()
        {
            database = CreateMultiRecipeDatabase();
            Entity processor = EntityManager.CreateEntity();
            EntityManager.AddBuffer<ProcessorItemSlot>(processor);
            Entity player = CreateInventoryOwner(1, 2);
            DynamicBuffer<ProcessorItemSlot> processorSlots =
                EntityManager.GetBuffer<ProcessorItemSlot>(processor);
            DynamicBuffer<InventorySlot> playerSlots =
                EntityManager.GetBuffer<InventorySlot>(player);
            PlayerInventory inventory = new PlayerInventory { SlotCount = 2 };
            ItemProcessState process = new ItemProcessState
            {
                SelectedRecipeIndex = -1,
                ActiveRecipeIndex = -1,
                Status = ItemProcessStatus.Idle
            };
            ref FactoryDatabaseBlob blob = ref database.Value;
            FactoryRecipeRangeBlob range = blob.RecipeRangesByMachine[1];
            ItemProcessUtility.TrySelectRecipe(
                0, range, ref blob, processorSlots, ref process);
            for (int i = 0; i < 2; i++)
            {
                ProcessorItemSlot input = processorSlots[i];
                input.Count = input.RequiredOrProducedCount;
                processorSlots[i] = input;
            }
            ItemProcessUtility.TryStart(
                0, 1000, blob.Recipes[0], processorSlots, ref process);
            ItemProcessUtility.AdvanceOneTick(ref process);
            ItemProcessUtility.PublishCompletedOutputs(processorSlots, ref process);

            Assert.That(ItemProcessUtility.TryChangeRecipe(
                1,
                range,
                ref blob,
                processorSlots,
                ref process,
                playerSlots,
                ref inventory), Is.True);

            Assert.That(SumItem(playerSlots, 1), Is.EqualTo(2));
            Assert.That(SumItem(playerSlots, 2), Is.EqualTo(3));
            Assert.That(process.Status, Is.EqualTo(ItemProcessStatus.Idle));
        }

        [Test]
        public void RecipeSelectionCommand_NonPlayerOwner_ConfiguresProcessor()
        {
            database = CreateMultiRecipeDatabase();
            Entity databaseEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(
                databaseEntity,
                new FactoryDatabase { Value = database });

            Entity processor = EntityManager.CreateEntity();
            EntityManager.AddComponentData(processor, new GridPlacement
            {
                AnchorCell = new Unity.Mathematics.int2(0, 0)
            });
            EntityManager.AddComponentData(processor, new ItemProcessor
            {
                MachineType = new MachineTypeId { Value = 1 },
                WorkRatePermille = 1000
            });
            EntityManager.AddComponentData(processor, new ItemProcessState
            {
                SelectedRecipeIndex = -1,
                ActiveRecipeIndex = -1,
                Status = ItemProcessStatus.Idle
            });
            EntityManager.AddBuffer<ProcessorItemSlot>(processor);

            Entity nonPlayerOwner = EntityManager.CreateEntity();
            EntityManager.AddBuffer<RecipeSelectionResult>(nonPlayerOwner);
            DynamicBuffer<RecipeSelectionCommand> commands =
                EntityManager.AddBuffer<RecipeSelectionCommand>(nonPlayerOwner);
            commands.Add(new RecipeSelectionCommand
            {
                BuildingCell = new Unity.Mathematics.int2(0, 0),
                Recipe = new RecipeId { Value = 1 }
            });

            SystemHandle system =
                TestWorld.GetOrCreateSystem<RecipeSelectionCommandSystem>();
            system.Update(TestWorld.Unmanaged);
            EntityManager.CompleteAllTrackedJobs();

            DynamicBuffer<RecipeSelectionResult> results =
                EntityManager.GetBuffer<RecipeSelectionResult>(nonPlayerOwner);
            Assert.That(results.Length, Is.EqualTo(1));
            Assert.That(results[0].Success, Is.EqualTo(1));
            Assert.That(results[0].FailureReason,
                Is.EqualTo(RecipeSelectionFailureReason.None));
            Assert.That(
                EntityManager.GetComponentData<ItemProcessState>(processor)
                    .SelectedRecipeIndex,
                Is.Zero);
        }

        [Test]
        public void Processor_OnlyBlocksWhenOutputStackCannotFitAnotherBatch()
        {
            database = CreateMultiRecipeDatabase();
            Entity processor = EntityManager.CreateEntity();
            DynamicBuffer<ProcessorItemSlot> slots =
                EntityManager.AddBuffer<ProcessorItemSlot>(processor);
            ItemProcessState process = new ItemProcessState
            {
                SelectedRecipeIndex = -1,
                ActiveRecipeIndex = -1,
                Status = ItemProcessStatus.Idle
            };
            ref FactoryDatabaseBlob blob = ref database.Value;
            FactoryRecipeRangeBlob range = blob.RecipeRangesByMachine[1];
            Assert.That(ItemProcessUtility.TrySelectRecipe(
                1, range, ref blob, slots, ref process), Is.True);

            for (int batch = 0; batch < 64; batch++)
            {
                ProcessorItemSlot input = slots[0];
                input.Count = input.RequiredOrProducedCount;
                slots[0] = input;
                Assert.That(ItemProcessUtility.TryStart(
                    1, 1000, blob.Recipes[1], slots, ref process), Is.True);
                while (!ItemProcessUtility.AdvanceOneTick(ref process))
                {
                }
                Assert.That(ItemProcessUtility.PublishCompletedOutputs(
                    slots, ref process), Is.True);
                Assert.That(
                    process.Status,
                    Is.EqualTo(batch == 63
                        ? ItemProcessStatus.OutputBlocked
                        : ItemProcessStatus.Idle));
            }

            Assert.That(slots[1].Count, Is.EqualTo(64));
            ItemProcessUtility.AcknowledgeOutput(
                new ItemId { Value = 1 }, 1, slots, ref process);
            Assert.That(slots[1].Count, Is.EqualTo(63));
            Assert.That(process.Status, Is.EqualTo(ItemProcessStatus.Idle));
        }

        [Test]
        public void ProcessorPortAdapter_PublishesEachRecipeItemOnOnePhysicalPort()
        {
            database = CreateMultiRecipeDatabase();
            Entity databaseEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(
                databaseEntity,
                new FactoryDatabase { Value = database });
            Entity processor = CreatePortOwner(default);
            EntityManager.AddComponentData(processor, new ItemProcessor
            {
                MachineType = new MachineTypeId { Value = 1 },
                WorkRatePermille = 1000
            });
            ItemProcessState process = new ItemProcessState
            {
                SelectedRecipeIndex = -1,
                ActiveRecipeIndex = -1,
                Status = ItemProcessStatus.Idle
            };
            EntityManager.AddComponentData(processor, process);
            DynamicBuffer<ProcessorItemSlot> slots =
                EntityManager.AddBuffer<ProcessorItemSlot>(processor);
            ref FactoryDatabaseBlob blob = ref database.Value;
            ItemProcessUtility.TrySelectRecipe(
                0,
                blob.RecipeRangesByMachine[1],
                ref blob,
                slots,
                ref process);
            EntityManager.SetComponentData(processor, process);
            DynamicBuffer<BuildingPort> ports =
                EntityManager.GetBuffer<BuildingPort>(processor);
            ports.Add(new BuildingPort { Type = BuildingPortType.Input, Index = 0 });
            ports.Add(new BuildingPort { Type = BuildingPortType.Output, Index = 0 });

            SystemHandle adapter =
                TestWorld.GetOrCreateSystem<ItemPortAdapterSystem>();
            adapter.Update(TestWorld.Unmanaged);
            EntityManager.CompleteAllTrackedJobs();

            DynamicBuffer<ItemInputPortNext> inputs =
                EntityManager.GetBuffer<ItemInputPortNext>(processor);
            DynamicBuffer<ItemOutputPortNext> outputs =
                EntityManager.GetBuffer<ItemOutputPortNext>(processor);
            Assert.That(inputs.Length, Is.EqualTo(2));
            Assert.That(outputs.Length, Is.EqualTo(2));
            Assert.That(inputs[0].Value.PortIndex, Is.Zero);
            Assert.That(inputs[1].Value.PortIndex, Is.Zero);
            Assert.That(inputs[0].Value.AcceptedItemType,
                Is.Not.EqualTo(inputs[1].Value.AcceptedItemType));
        }

        [Test]
        public void ManualMove_PlayerToStorage_UpdatesBothStableSlotsAndRevisions()
        {
            database = CreateMultiRecipeDatabase();
            Entity databaseEntity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(
                databaseEntity,
                new FactoryDatabase { Value = database });
            Entity player = CreateInventoryOwner(11, 2);
            EntityManager.AddComponentData(player, new PlayerIdentity
            {
                Value = new PlayerId { Value = 11 }
            });
            EntityManager.AddComponentData(player, new PlayerInventory
            {
                SlotCount = 2
            });
            EntityManager.AddBuffer<MoveItemPlayerCommand>(player).Add(
                new MoveItemPlayerCommand
                {
                    Header = new PlayerCommandHeader
                    {
                        Player = new PlayerId { Value = 11 },
                        RequestId = 7,
                        ClientSequence = 1
                    },
                    Source = InventoryEndpoint(ItemOwnerKind.Player, 11, 0),
                    Destination = InventoryEndpoint(ItemOwnerKind.Storage, 22, 0),
                    ExpectedItemType = new ItemId { Value = 1 },
                    Amount = 4
                });
            EntityManager.AddBuffer<MoveItemPlayerResult>(player);
            DynamicBuffer<InventorySlot> playerSlots =
                EntityManager.GetBuffer<InventorySlot>(player);
            playerSlots[0] = new InventorySlot
            {
                ItemType = new ItemId { Value = 1 },
                Count = 5
            };

            Entity storage = CreateInventoryOwner(22, 2);
            EntityManager.AddComponentData(storage, new StorageState
            {
                Capacity = 10,
                SlotCount = 2
            });

            SystemHandle system =
                TestWorld.GetOrCreateSystem<ManualItemTransferSystem>();
            system.Update(TestWorld.Unmanaged);

            playerSlots = EntityManager.GetBuffer<InventorySlot>(player);
            Assert.That(playerSlots[0].Count, Is.EqualTo(1));
            DynamicBuffer<InventorySlot> storageSlots =
                EntityManager.GetBuffer<InventorySlot>(storage);
            Assert.That(storageSlots[0].Count, Is.EqualTo(4));
            Assert.That(EntityManager.GetComponentData<PlayerInventory>(player).Revision,
                Is.EqualTo(1));
            StorageState storageState =
                EntityManager.GetComponentData<StorageState>(storage);
            Assert.That(storageState.Revision, Is.EqualTo(1));
            Assert.That(storageState.TotalStored, Is.EqualTo(4));
            Assert.That(EntityManager.GetBuffer<MoveItemPlayerResult>(player)[0].Success,
                Is.EqualTo(1));
        }

        private Entity CreateInventoryOwner(ulong runtimeId, int slotCount)
        {
            Entity entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, new ItemContainerIdentity
            {
                RuntimeId = runtimeId
            });
            DynamicBuffer<InventorySlot> slots =
                EntityManager.AddBuffer<InventorySlot>(entity);
            slots.ResizeUninitialized(slotCount);
            for (int i = 0; i < slotCount; i++)
                slots[i] = default;
            return entity;
        }

        private static ItemEndpoint InventoryEndpoint(
            ItemOwnerKind kind,
            ulong runtimeId,
            ushort slotIndex)
        {
            return new ItemEndpoint
            {
                OwnerKind = kind,
                OwnerRuntimeId = runtimeId,
                Domain = ItemSlotDomain.Inventory,
                SlotIndex = slotIndex
            };
        }

        private static BlobAssetReference<FactoryDatabaseBlob>
            CreateMultiRecipeDatabase()
        {
            using BlobBuilder builder = new BlobBuilder(Allocator.Temp);
            ref FactoryDatabaseBlob root = ref
                builder.ConstructRoot<FactoryDatabaseBlob>();
            BlobBuilderArray<FactoryItemBlob> items =
                builder.Allocate(ref root.ItemsById, 3);
            items[1] = new FactoryItemBlob
            {
                Id = new ItemId { Value = 1 },
                MaxStack = 64
            };
            items[2] = new FactoryItemBlob
            {
                Id = new ItemId { Value = 2 },
                MaxStack = 64
            };
            builder.Allocate(ref root.MachineTypesById, 2);
            builder.Allocate(ref root.BuildingsById, 1);
            builder.Allocate(ref root.BuildingLevelsById, 1);
            builder.Allocate(ref root.BeltLevelsById, 1);
            builder.Allocate(ref root.ProcessorLevelsById, 1);
            builder.Allocate(ref root.StorageLevelsById, 1);
            builder.Allocate(ref root.BuildingLevelMenu, 0);
            builder.Allocate(ref root.BuildingPorts, 0);
            BlobBuilderArray<FactoryRecipeBlob> recipes =
                builder.Allocate(ref root.Recipes, 2);
            recipes[0] = new FactoryRecipeBlob
            {
                Id = new RecipeId { Value = 1 },
                MachineType = new MachineTypeId { Value = 1 },
                DurationTicks = 1,
                InputStart = 0,
                InputCount = 2,
                OutputStart = 0,
                OutputCount = 2
            };
            recipes[1] = new FactoryRecipeBlob
            {
                Id = new RecipeId { Value = 2 },
                MachineType = new MachineTypeId { Value = 1 },
                DurationTicks = 2,
                InputStart = 2,
                InputCount = 1,
                OutputStart = 2,
                OutputCount = 1
            };
            BlobBuilderArray<FactoryRecipeIngredientBlob> inputs =
                builder.Allocate(ref root.Inputs, 3);
            inputs[0] = new FactoryRecipeIngredientBlob
            {
                ItemId = new ItemId { Value = 1 }, Count = 1
            };
            inputs[1] = new FactoryRecipeIngredientBlob
            {
                ItemId = new ItemId { Value = 2 }, Count = 1
            };
            inputs[2] = new FactoryRecipeIngredientBlob
            {
                ItemId = new ItemId { Value = 2 }, Count = 2
            };
            BlobBuilderArray<FactoryRecipeIngredientBlob> outputs =
                builder.Allocate(ref root.Outputs, 3);
            outputs[0] = new FactoryRecipeIngredientBlob
            {
                ItemId = new ItemId { Value = 1 }, Count = 2
            };
            outputs[1] = new FactoryRecipeIngredientBlob
            {
                ItemId = new ItemId { Value = 2 }, Count = 3
            };
            outputs[2] = new FactoryRecipeIngredientBlob
            {
                ItemId = new ItemId { Value = 1 }, Count = 1
            };
            BlobBuilderArray<FactoryRecipeRangeBlob> ranges =
                builder.Allocate(ref root.RecipeRangesByMachine, 2);
            ranges[1] = new FactoryRecipeRangeBlob { Start = 0, Count = 2 };
            return builder.CreateBlobAssetReference<FactoryDatabaseBlob>(
                Allocator.Persistent);
        }

        private static int SumItem(
            in DynamicBuffer<InventorySlot> slots,
            ushort itemId)
        {
            int total = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].ItemType.Value == itemId)
                    total += slots[i].Count;
            }
            return total;
        }
    }
}
