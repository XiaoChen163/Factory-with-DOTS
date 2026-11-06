using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Factory.Tests
{
    public sealed class FactoryModelImportTests
    {
        private FactoryContentDraft draft;
        private const string ItemModel = "Assets/Art/Items/test_a/test_a.fbx";
        private const string BuildingModel = "Assets/Art/Buildings/testMachine/testMachine.fbx";

        [SetUp] public void SetUp() { draft = ScriptableObject.CreateInstance<FactoryContentDraft>(); draft.Load(); }
        [TearDown] public void TearDown() { Object.DestroyImmediate(draft); }

        [Test]
        public void ItemMode_ChangesOnlyItemTableAndPreservesExistingProperties()
        {
            var before = draft.Tables.ToDictionary(t => t.path, t => t.text);
            int row = draft.ImportItemModel("mode_item_fixture", ItemModel);
            var item = draft.GetItems().Single(i => i.row == row);
            Assert.That(item.modelPath, Is.EqualTo(ItemModel));
            Assert.That(item.maxStack, Is.EqualTo(64));
            foreach (var table in draft.Tables.Where(t => !t.path.EndsWith("/items.csv")))
                Assert.That(table.text, Is.EqualTo(before[table.path]), table.path);
            draft.SetItemValue(row, "name_key", "保留名称");
            draft.SetItemValue(row, "max_stack", "32");
            draft.ImportItemModel("mode_item_fixture", "Assets/Art/Items/test_b/test_b.fbx");
            item = draft.GetItems().Single(i => i.key == "mode_item_fixture");
            Assert.That(item.name, Is.EqualTo("保留名称"));
            Assert.That(item.maxStack, Is.EqualTo(32));
            Assert.That(item.modelPath, Does.EndWith("test_b.fbx"));
        }

        [Test]
        public void BuildingMode_ImportsAllSpatialSectionsAndModelInOneUndoStep()
        {
            var json = FactoryBuildingJson.Parse(File.ReadAllText("Tools/Blender/Fixtures/testMachine/testMachine.factory.json")
                .Replace("\"testMachine\"", "\"mode_building_fixture\""));
            draft.ImportBuildingModel(json, BuildingModel, BuildingKind.Storage, "");
            var building = draft.GetBuildings().Single(b => b.key == json.Building.key);
            Assert.That(building.modelPath, Is.EqualTo(BuildingModel));
            Assert.That(building.width, Is.EqualTo(json.Building.width));
            Assert.That(building.ports.Length, Is.EqualTo(json.Ports.Length));
            Assert.That(draft.GetBuildingImport(building.key).colliders.Length, Is.EqualTo(json.Colliders.Length));
            Undo.PerformUndo();
            Assert.That(draft.GetBuildings().Any(b => b.key == json.Building.key), Is.False);
            Assert.That(draft.IsModified, Is.False);
        }

        [Test]
        public void BuildingMode_RejectsIncompleteJsonWithoutChangingTables()
        {
            var json = FactoryBuildingJson.Parse("{\"schema_version\":1,\"building\":{\"key\":\"incomplete\"}}");
            Assert.Throws<InvalidDataException>(() => draft.ImportBuildingModel(json, BuildingModel, BuildingKind.Storage, ""));
            Assert.That(draft.IsModified, Is.False);
        }

        [Test]
        public void Catalogs_OnlyReferenceFbxAndItemsHaveNoPhysicsComponents()
        {
            var candidate = draft.ReadCandidate();
            try
            {
                Assert.That(candidate.buildings.All(b => b.visualPrefabPath.EndsWith(".fbx")), Is.True);
                foreach (var item in candidate.items)
                {
                    Assert.That(item.modelPath, Does.EndWith(".fbx"));
                    Assert.That(item.model.GetComponentsInChildren<Collider>(true), Is.Empty);
                    Assert.That(item.model.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
                    Assert.That(item.model.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty);
                }
            }
            finally { Object.DestroyImmediate(candidate); }
            Assert.Throws<InvalidDataException>(() => FactoryDatabaseCsvImporter.LoadModel("Assets/Prefabs/Ports/InputPortView.prefab"));
        }

        [UnityTest]
        public IEnumerator UnifiedWindow_SwitchesBetweenBuildingAndItemFields()
        {
            var window = FactoryModelImportWindow.Open(draft, FactoryModelKind.Item, (_, __, ___) => { });
            try
            {
                yield return null;
                window.CreateGUI();
                var root = window.rootVisualElement;
                Assert.That(root.Q("model-import-item-key"), Is.Not.Null);
                Assert.That(root.Q("model-import-json"), Is.Null);
                root.Q<PopupField<string>>("model-import-mode").value = "建筑";
                Assert.That(root.Q("model-import-item-key"), Is.Null);
                Assert.That(root.Q("model-import-json"), Is.Not.Null);
                Assert.That(draft.IsModified, Is.False);
            }
            finally { window.Close(); }
        }
    }
}
