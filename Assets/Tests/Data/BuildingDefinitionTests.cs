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
        private string directory;
        private FactoryDatabaseAsset asset;

        [SetUp]
        public void CopyTables()
        {
            directory = Path.GetFullPath(Path.Combine("Temp", "BuildingTables-" + Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(directory);
            foreach (string file in Directory.GetFiles("Assets/Data/FactoryTables/buildings", "*.csv"))
                File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            asset = Object.Instantiate(AssetDatabase.LoadAssetAtPath<FactoryDatabaseAsset>(FactoryDatabaseCsvImporter.DatabaseAssetPath));
        }

        public override void TearDownWorld()
        {
            base.TearDownWorld();
            if (asset != null) Object.DestroyImmediate(asset);
            if (directory != null && Directory.Exists(directory))
            {
                foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }

        private FactoryBuildingDefinitionRow[] Read() =>
            FactoryDatabaseCsvImporter.ReadBuildingDefinitions(directory, asset.machineTypes);

        private void Write(string name, params string[] lines) =>
            File.WriteAllLines(Path.Combine(directory, name + ".csv"), lines);

        [Test]
        public void MainTable_ContainsOnlyUniversalFields()
        {
            Assert.That(File.ReadLines(Path.Combine(directory, "buildings.csv")).First(),
                Is.EqualTo("key,behavior,width,length,prefab_path,is_rectangle,bitmap"));
            var rows = Read();
            Assert.That(rows.Length, Is.EqualTo(25));
            Assert.That(rows.Single(b => b.key == "furnace").kind, Is.EqualTo(BuildingKind.Processor));
            Assert.That(rows.Single(b => b.key == "furnace").displayName, Is.EqualTo("熔炉"));
            Assert.That(rows.Any(b => b.key == "furnace_mk1"), Is.False);
            Assert.That(rows.Where(b => b.kind == BuildingKind.Processor).All(b => b.beltTier == 0), Is.True);
            Assert.That(rows.Single(b => b.key == "storage").storageCapacity, Is.EqualTo(100));
            Assert.That(rows.Single(b => b.key == "ramp_foundation_1_2").rampRiseHeightUnits, Is.EqualTo(4));
        }

        [Test]
        public void ProcessorMappings_AreReadFromExtension_AndCanShareMachineType()
        {
            Write("processor_stats", "key,machine_type_key", "miner,smelting", "furnace,smelting");
            var rows = Read();
            ushort smelting = asset.machineTypes.Single(m => m.key == "smelting").id;
            Assert.That(rows.Single(b => b.key == "miner").machineTypeId, Is.EqualTo(smelting));
            Assert.That(rows.Single(b => b.key == "furnace").machineTypeId, Is.EqualTo(smelting));
        }

        [TestCase("miner,mining")] // Missing furnace.
        [TestCase("miner,mining\nfurnace,smelting\nfurnace,smelting")]
        [TestCase("miner,mining\nfurnace,smelting\nstorage,smelting")]
        [TestCase("miner,mining\nfurnace,smelting\nunknown,smelting")]
        [TestCase("miner,mining\nfurnace,unknown")]
        public void InvalidProcessorExtensions_AreRejected(string rows)
        {
            Write("processor_stats", "key,machine_type_key", rows);
            Assert.Throws<InvalidDataException>(() => Read());
        }

        [TestCase("storage_stats", "key,capacity,slot_count")]
        [TestCase("ramp_stats", "key,rise_height_units")]
        [TestCase("belt_stats", "key,tier,cells_per_second")]
        [TestCase("building_presentation", "key,icon_path,menu_order")]
        [TestCase("building_names", "key,locale,name")]
        public void MissingRequiredExtensions_AreRejected(string table, string header)
        {
            Write(table, header);
            Assert.Throws<InvalidDataException>(() => Read());
        }

        [TestCase("building_ports", "key,type,index,cell_x,cell_z,direction_x,direction_z", "unknown,Output,0,1,0,1,0")]
        [TestCase("building_names", "key,locale,name", "unknown,zh-CN,不存在")]
        [TestCase("storage_stats", "key,capacity,slot_count", "furnace,100,8")]
        [TestCase("ramp_stats", "key,rise_height_units", "furnace,8")]
        public void UnknownOrIncompatibleExtensions_AreRejected(string table, string header, string row)
        {
            Write(table, header, row);
            Assert.Throws<InvalidDataException>(() => Read());
        }

        [Test]
        public void Belts_ShareTierSpeed_AndVerticalPartsMatchByTier()
        {
            asset.buildings = Read();
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
        public void ReorderingMainRows_PreservesKeyAssociations_AndResolvesNewRuntimeIds()
        {
            string path = Path.Combine(directory, "buildings.csv");
            string[] lines = File.ReadAllLines(path);
            File.WriteAllLines(path, new[] { lines[0] }.Concat(lines.Skip(1).Reverse()));
            asset.buildings = Read();
            Assert.That(FactoryDatabaseBakingUtility.TryBuild(asset, null, out var blob), Is.True);
            try
            {
                var furnace = FactoryDatabaseUtility.GetBuildingDefinitionId(ref blob.Value, "furnace");
                Assert.That(furnace.Value, Is.Not.EqualTo(6));
                Assert.That(blob.Value.BuildingDefinitionsById[furnace.Value].Kind, Is.EqualTo(BuildingKind.Processor));
                Assert.That(blob.Value.BuildingDefinitionsById[furnace.Value].MachineType.Value,
                    Is.EqualTo(asset.machineTypes.Single(m => m.key == "smelting").id));
                Assert.That(asset.buildings.Single(b => b.key == "furnace").displayName, Is.EqualTo("熔炉"));
            }
            finally { blob.Dispose(); }
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
