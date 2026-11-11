using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Unity.Entities;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Factory.Tests
{
    public sealed class BuildingDefinitionTests : FactoryWorldFixture
    {
        private FactoryDatabaseAsset asset;
        [SetUp] public void LoadAsset() => asset = FactoryDatabaseEditor.CreateSnapshot(FactoryDatabaseEditor.Load());
        public override void TearDownWorld()
        {
            base.TearDownWorld();
            if (asset != null) Object.DestroyImmediate(asset);
        }

        [Test]
        public void ColliderDefinitions_RejectDuplicateKeysAndInvalidDimensions()
        {
            int row = Array.FindIndex(asset.buildings, b => b.key == "furnace");
            var shape = new FactoryColliderProxy { name = "body", type = "Box", size = Vector3.one };
            asset.buildings[row].colliders = new[] { shape, shape };
            Assert.Throws<InvalidDataException>(() => FactoryDatabaseValidation.Validate(asset));
            shape.size = new Vector3(-1, 1, 1);
            asset.buildings[row].colliders = new[] { shape };
            Assert.Throws<InvalidDataException>(() => FactoryDatabaseValidation.Validate(asset));
        }

        [Test]
        public void ColliderDraft_RoundTripsQuaternionAndPreservesOtherBuildings()
        {
            var draft = ScriptableObject.CreateInstance<FactoryContentDraft>();
            try
            {
                draft.Load();
                var before = draft.GetBuildingImport("storage").colliders.Select(c => JsonUtility.ToJson(c)).ToArray();
                var shape = new FactoryColliderProxy { name = "body", type = "Box", position = new Vector3(.12345679f, 2, 3), rotation = Quaternion.Euler(21, 37, 89), size = Vector3.one };
                draft.SetBuildingColliders("furnace", new[] { shape });
                var candidate = draft.ReadCandidate();
                try
                {
                    var value = candidate.buildings.Single(b => b.key == "furnace").colliders[0];
                    Assert.That(value.rotation, Is.EqualTo(shape.rotation));
                    Assert.That(value.position, Is.EqualTo(shape.position));
                }
                finally { Object.DestroyImmediate(candidate); }
                draft.SetBuildingColliders("furnace", Array.Empty<FactoryColliderProxy>());
                Assert.That(draft.GetBuildingImport("furnace").colliders, Is.Empty);
                Assert.That(draft.GetBuildingImport("storage").colliders.Select(c => JsonUtility.ToJson(c)), Is.EqualTo(before));
            }
            finally { Object.DestroyImmediate(draft); }
        }

        [Test]
        public void Asset_PreservesExistingBuildingContent()
        {
            Assert.That(asset.buildings.Length, Is.EqualTo(28));
            Assert.That(asset.buildings.Single(b => b.key == "furnace").displayName, Is.EqualTo("熔炉"));
            Assert.That(asset.buildings.Single(b => b.key == "storage").storageSlotCount, Is.EqualTo(8));
            Assert.That(asset.buildings.Single(b => b.key == "ramp_foundation_1_2").rampRiseHeightUnits, Is.EqualTo(4));
        }

        [Test]
        public void ProcessorDefinitions_CanShareMachineTypeAndRejectUnknownType()
        {
            int miner = Array.FindIndex(asset.buildings, b => b.key == "miner");
            ushort smelting = asset.machineTypes.Single(m => m.key == "smelting").id;
            asset.buildings[miner].machineTypeId = smelting;
            FactoryDatabaseValidation.Validate(asset);
            asset.buildings[miner].machineTypeId = ushort.MaxValue;
            Assert.Throws<InvalidDataException>(() => FactoryDatabaseValidation.Validate(asset));
        }

        [TestCase(BuildingKind.Storage)]
        [TestCase(BuildingKind.Belt)]
        [TestCase(BuildingKind.RampFoundation)]
        public void RequiredBehaviorParameters_AreValidated(BuildingKind kind)
        {
            int row = Array.FindIndex(asset.buildings, b => b.kind == kind);
            asset.buildings[row].storageSlotCount = 0;
            asset.buildings[row].cellsPerSecond = 0;
            asset.buildings[row].rampRiseHeightUnits = 0;
            Assert.Throws<InvalidDataException>(() => FactoryDatabaseValidation.Validate(asset));
        }

        [Test]
        public void NamedVisualParts_SupportGroupsAndRejectAmbiguousNames()
        {
            var model = new GameObject("FBX model");
            try
            {
                var group = new GameObject("Export group");
                group.transform.SetParent(model.transform, false);
                var edge = new GameObject("Connection Edge East");
                edge.transform.SetParent(group.transform, false);
                Assert.That(FactoryBuildingVisualUtility.FindPart(model, edge.name), Is.EqualTo(edge.transform));
                var duplicate = new GameObject(edge.name);
                duplicate.transform.SetParent(model.transform, false);
                Assert.Throws<InvalidDataException>(() => FactoryBuildingVisualUtility.FindPart(model, edge.name));
            }
            finally { Object.DestroyImmediate(model); }
        }

        [Test]
        public void ProductionContent_HasValidItemModelsAndRequiredRecipes()
        {
            FactoryDatabaseEditor.Validate(asset);
            Assert.That(asset.items.Length, Is.EqualTo(9));
            foreach (var item in asset.items)
            {
                Assert.That(item.model.GetComponentInChildren<MeshFilter>(), Is.Not.Null);
                Assert.That(item.model.GetComponentsInChildren<MonoBehaviour>(), Is.Empty);
                Assert.That(item.icon, Is.Not.Null);
            }
            Assert.That(asset.recipes.Single(r => r.key == "smelt_iron").outputs.Single().itemKey, Is.EqualTo("iron_ingot"));
            Assert.That(asset.recipes.Single(r => r.key == "stamp_iron_nail").outputs.Single().count, Is.EqualTo(4));
            Assert.That(asset.buildings.Single(b => b.key == "stamping_press").machineTypeId,
                Is.EqualTo(asset.machineTypes.Single(m => m.key == "stamping").id));
        }

        [Test]
        public void ProcessorBindings_AreImportedAndBaked_AndRejectDuplicateSlotsOrMissingCoverage()
        {
            var machine = FactoryTestDatabase.AddMultiOutputProcessor(asset);
            FactoryTestDatabase.SetTwoOutputRecipes(asset, machine.machineTypeId);
            var ports = machine.ports.Where(p => p.type == BuildingPortType.Output).ToArray();
            Assert.That(ports.Select(p => p.number), Is.EqualTo(new ushort[] { 1, 2, 3 }));
            Assert.That(ports.Select(p => p.outputSlot), Is.EqualTo(new ushort[] { 1, 2, 3 }));
            Assert.That(FactoryDatabaseBakingUtility.TryBuild(asset, null, out var blob), Is.True);
            try
            {
                var definition = blob.Value.BuildingDefinitionsById[machine.id];
                var output = blob.Value.BuildingPorts[definition.PortStart + 4];
                Assert.That(output.Number, Is.EqualTo(2));
                Assert.That(output.OutputSlot, Is.EqualTo(2));
            }
            finally { blob.Dispose(); }
            int index = Array.FindIndex(asset.buildings, b => b.key == "test_machine_m");
            asset.buildings[index].ports[4].outputSlot = 1;
            Assert.Throws<InvalidDataException>(() => FactoryDatabaseValidation.Validate(asset));
            asset.buildings[index].ports[4].outputSlot = 4;
            Assert.Throws<InvalidDataException>(() => FactoryDatabaseValidation.Validate(asset));
            asset.buildings[index].ports[4].outputSlot = 2;
            asset.buildings[index].ports[4].number = 1;
            Assert.Throws<InvalidDataException>(() => FactoryDatabaseValidation.Validate(asset));
        }

        [Test]
        public void Belts_ShareTierSpeed_AndVerticalPartsMatchByTier()
        {
            Assert.DoesNotThrow(() => FactoryDatabaseValidation.Validate(asset));
            Assert.That(FactoryDatabaseBakingUtility.TryBuild(asset, null, out var blob), Is.True);
            try
            {
                foreach (var entry in asset.buildings.Where(b => b.kind == BuildingKind.VerticalBeltEntry))
                {
                    Assert.That(VerticalBeltUtility.TryGetPathDefinitions(ref blob.Value,
                        new BuildingDefinitionId { Value = entry.id }, out var segment, out var exit), Is.True);
                    Assert.That(blob.Value.BuildingDefinitionsById[segment.Value].BeltTier, Is.EqualTo(entry.beltTier));
                    Assert.That(blob.Value.BuildingDefinitionsById[exit.Value].CellsPerSecond, Is.EqualTo(entry.cellsPerSecond));
                }
            }
            finally { blob.Dispose(); }
            int index = Array.FindIndex(asset.buildings, b => b.key == "vertical_belt_entry_mk2");
            asset.buildings[index].cellsPerSecond = 99f;
            Assert.Throws<InvalidDataException>(() => FactoryDatabaseValidation.Validate(asset));
        }

        [Test]
        public void ParallelProcessors_UseExactRecipeDuration_AndProduceOneBatchEach()
        {
            var recipe = new FactoryRecipeBlob { DurationTicks = 17, OutputCount = 1 };
            for (int machine = 0; machine < 2; machine++)
            {
                Entity entity = EntityManager.CreateEntity();
                var slots = EntityManager.AddBuffer<ProcessorItemSlot>(entity);
                slots.Add(new ProcessorItemSlot
                {
                    Kind = ProcessorSlotKind.Output, AcceptedItemType = new ItemId { Value = 1 },
                    Capacity = 100, RequiredOrProducedCount = 3
                });
                var state = new ItemProcessState { Status = ItemProcessStatus.Idle };
                Assert.That(ItemProcessUtility.TryStart(0, recipe, slots, ref state), Is.True);
                Assert.That(state.DurationTicks, Is.EqualTo(17));
                for (int tick = 0; tick < 16; tick++)
                    Assert.That(ItemProcessUtility.AdvanceOneTick(ref state), Is.False);
                Assert.That(ItemProcessUtility.AdvanceOneTick(ref state), Is.True);
                Assert.That(ItemProcessUtility.PublishCompletedOutputs(slots, ref state), Is.True);
                Assert.That(slots[0].Count, Is.EqualTo(3));
            }
        }
    }
}
