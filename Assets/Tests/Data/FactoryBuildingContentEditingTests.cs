using System;
using System.Collections;
using System.Collections.Generic;
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
    public sealed class FactoryBuildingContentEditingTests
    {
        private FactoryContentDraft draft;
        private const string Full = @"{
          ""schema_version"":1,
          ""building"": { ""key"":""fixture_building"", ""model_file"":""fixture.Model.fbx"",
            ""width"":4, ""length"":3, ""is_rectangle"":false, ""bitmap"":""1111/1111/1100"" },
          ""ports"": [
            {""type"":""Input"",""index"":0,""cell_x"":2,""cell_z"":2,""direction_x"":0,""direction_z"":-1},
            {""type"":""Output"",""index"":0,""cell_x"":4,""cell_z"":1,""direction_x"":1,""direction_z"":0,""number"":1,""output_slot"":1}],
          ""colliders"": [
            {""name"":""Box"",""type"":""Box"",""position"":{""x"":1,""y"":3,""z"":2},""rotation"":{""x"":0,""y"":0,""z"":0,""w"":1},""size"":{""x"":2,""y"":4,""z"":3}},
            {""name"":""Sphere"",""type"":""Sphere"",""position"":{""x"":0,""y"":0,""z"":0},""rotation"":{""x"":0,""y"":0,""z"":0,""w"":1},""radius"":0.5},
            {""name"":""Capsule"",""type"":""Capsule"",""position"":{""x"":0,""y"":0,""z"":0},""rotation"":{""x"":0,""y"":0,""z"":0,""w"":1},""radius"":0.5,""height"":4,""axis"":""Y""},
            {""name"":""Cylinder"",""type"":""Cylinder"",""position"":{""x"":0,""y"":0,""z"":0},""rotation"":{""x"":0,""y"":0,""z"":0,""w"":1},""radius"":0.5,""height"":3,""axis"":""Y""}]
        }";
        private static FactoryBuildingJson Json(string key = "fixture_building") => FactoryBuildingJson.Parse(Full.Replace("fixture_building", key));

        [SetUp] public void SetUp() { draft = ScriptableObject.CreateInstance<FactoryContentDraft>(); draft.Load(); }
        [TearDown] public void TearDown() { Object.DestroyImmediate(draft); }

        [Test]
        public void Json_MissingSectionsDifferFromEmptyArraysAndUnsupportedDataIsRejected()
        {
            var missing = FactoryBuildingJson.Parse(@"{""schema_version"":1,""building"":{""key"":""furnace""}}");
            Assert.That(missing.HasFootprint || missing.HasPorts || missing.HasColliders, Is.False);
            var empty = FactoryBuildingJson.Parse(@"{""schema_version"":1,""building"":{""key"":""furnace""},""ports"":[],""colliders"":[]}");
            Assert.That(empty.HasPorts && empty.HasColliders, Is.True);
            Assert.That(empty.Ports, Is.Empty); Assert.That(empty.Colliders, Is.Empty);
            Assert.Throws<InvalidDataException>(() => FactoryBuildingJson.Parse(Full.Replace("\"schema_version\":1", "\"schema_version\":2")));
            Assert.Catch(() => FactoryBuildingJson.Parse(@"{""schema_version"":1,""building"":{""key"":""x""},""ports"":null}"));
            Assert.Catch(() => FactoryBuildingJson.Parse(@"{""schema_version"":1,""building"":{""key"":""x"",""width"":null}}"));
            Assert.Catch(() => FactoryBuildingJson.Parse(Full.Replace("\"radius\":0.5,\"height\":4", "\"radius\":3,\"height\":4")));
        }

        [Test]
        public void PartialImport_PreservesUnityEditsOtherLocalesAndUnselectedSections()
        {
            draft.SetBuildingName("furnace", "Unity 自定义熔炉");
            draft.ToggleBuildingCell("furnace", new Vector2Int(0, 0));
            var names = draft.ReadTable(FactoryContentDraft.NamesPath);
            names.Append(new Dictionary<string, string> { ["key"] = "furnace", ["locale"] = "en-US", ["name"] = "Custom Furnace" });
            draft.Tables.Single(t => t.path.EndsWith("/building_names.csv")).text = names.Text;
            var before = draft.GetBuildings().Single(b => b.key == "furnace");
            draft.ImportBuildingJson(Json("furnace"), false, false, true);
            var after = draft.GetBuildings().Single(b => b.key == "furnace");
            Assert.That(after.name, Is.EqualTo(before.name)); Assert.That(after.bitmap, Is.EqualTo(before.bitmap));
            Assert.That(after.prefabPath, Is.EqualTo(before.prefabPath)); Assert.That(after.behavior, Is.EqualTo(before.behavior));
            Assert.That(after.ports, Is.EqualTo(before.ports));
            Assert.That(draft.ReadTable(FactoryContentDraft.NamesPath).Text, Does.Contain("Custom Furnace"));
            var empty = FactoryBuildingJson.Parse(@"{""schema_version"":1,""building"":{""key"":""furnace""},""ports"":[],""colliders"":[]}");
            draft.ImportBuildingJson(empty, true, true, false);
            Assert.That(draft.GetBuildings().Single(b => b.key == "furnace").ports, Is.Empty);
            Assert.That(draft.GetBuildingImport("furnace").colliders.Length, Is.EqualTo(4));
            Assert.That(draft.GetBuildings().Single(b => b.key == "furnace").bitmap, Is.EqualTo(before.bitmap));
            Undo.PerformUndo();
            Assert.That(draft.GetBuildings().Single(b => b.key == "furnace").ports, Is.EqualTo(before.ports));
            draft.ImportBuildingJson(empty, false, false, true);
            Assert.That(draft.GetBuildingImport("furnace").colliders, Is.Empty);
        }

        [Test]
        public void SpatialEditing_UsesBoundaryCoordinatesReversesFlowAndClearsNonProcessorBindings()
        {
            string key = draft.AddBuilding();
            draft.SetBuildingFootprint(key, 2, 2, true, "");
            draft.SetBuildingBehavior(key, BuildingKind.Storage);
            draft.AddBuildingPort(key, new Vector2Int(-1, 0), Vector2Int.right, BuildingPortType.Input);
            draft.ReverseBuildingPort(key, 0);
            var b = draft.GetBuildings().Single(v => v.key == key);
            Assert.That(b.ports[0].type, Is.EqualTo(BuildingPortType.Output));
            Assert.That(b.ports[0].direction, Is.EqualTo(Vector2Int.left));
            Assert.Throws<InvalidDataException>(() => draft.AddBuildingPort(key, new Vector2Int(1, 1), Vector2Int.right, BuildingPortType.Input));
            draft.ToggleBuildingCell(key, new Vector2Int(1, 1));
            Assert.That(draft.GetBuildings().Single(v => v.key == key).bitmap, Is.EqualTo("11/10"));
            draft.ImportBuildingJson(Json("storage"), true, true, false);
            var storage = draft.GetBuildings().Single(v => v.key == "storage");
            Assert.That(storage.ports.All(p => p.number == 0 && p.outputSlot == 0), Is.True);
            Assert.That(draft.GetBuildingStat("storage", "storage_stats", "capacity"), Is.EqualTo("100"));
            var shape = storage.SpatialDefinition();
            FactoryBuildingFootprintUtility.ValidatePorts(shape, FactoryBuildingFootprintUtility.GetOccupiedOffsets(shape));
        }

        [Test]
        public void CompleteImport_SaveReopenConfigAndRollbackKeepCsvAndAssetsConsistent()
        {
            string key = "fixture_building";
            string configPath = FactoryContentDraft.BuildingImportPath(key);
            Assert.That(AssetDatabase.LoadMainAssetAtPath(configPath), Is.Null);
            bool hadFolder = AssetDatabase.IsValidFolder(FactoryContentDraft.BuildingImportDirectory);
            var backups = draft.Tables.ToDictionary(t => t.path, t => File.ReadAllBytes(t.path));
            backups.Add(FactoryDatabaseCsvImporter.DatabaseAssetPath, File.ReadAllBytes(FactoryDatabaseCsvImporter.DatabaseAssetPath));
            string prefabPath = "Assets/Prefabs/Buildings/Furnace.prefab";
            string prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
            try
            {
                draft.ImportBuildingJson(Json(), true, true, true);
                Assert.Catch(() => draft.Save(), "Missing behavior and prefab must remain unsaved.");
                Assert.That(File.ReadAllBytes(FactoryDatabaseCsvImporter.DatabaseAssetPath), Is.EqualTo(backups[FactoryDatabaseCsvImporter.DatabaseAssetPath]));
                Assert.That(File.Exists(configPath), Is.False);
                draft.SetBuildingBehavior(key, BuildingKind.Processor);
                draft.SetBuildingStat(key, "processor_stats", "machine_type_key", "smelting");
                draft.SetBuildingPrefab(key, AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
                draft.Save();
                string guid = AssetDatabase.AssetPathToGUID(configPath);
                Assert.That(guid, Is.Not.Empty);
                draft.Load();
                Assert.That(draft.GetBuildings().Single(b => b.key == key).bitmap, Is.EqualTo("1111/1111/1100"));
                var data = draft.GetBuildingImport(key);
                Assert.That(data.colliders.Length, Is.EqualTo(4));
                Assert.That(data.colliders[0].position, Is.EqualTo(new Vector3(1, 3, 2)));
                Assert.That(data.colliders[0].size, Is.EqualTo(new Vector3(2, 4, 3)));
                Assert.That(data.colliders[3].type, Is.EqualTo("Cylinder"));
                byte[] namesBefore = File.ReadAllBytes(FactoryContentDraft.TableDirectory + "/" + FactoryContentDraft.NamesPath);
                byte[] databaseBefore = File.ReadAllBytes(FactoryDatabaseCsvImporter.DatabaseAssetPath);
                draft.SetBuildingName(key, "待保存名称");
                draft.ImportBuildingJson(FactoryBuildingJson.Parse(@"{""schema_version"":1,""building"":{""key"":""fixture_building""},""colliders"":[]}"), false, false, true);
                using (new FileStream(configPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var error = Assert.Throws<IOException>(() => draft.Save());
                    Assert.That(error.ToString(), Does.Contain(configPath));
                    Assert.That(File.ReadAllBytes(FactoryContentDraft.TableDirectory + "/" + FactoryContentDraft.NamesPath), Is.EqualTo(namesBefore));
                    Assert.That(File.ReadAllBytes(FactoryDatabaseCsvImporter.DatabaseAssetPath), Is.EqualTo(databaseBefore));
                }
                draft.Save();
                Assert.That(AssetDatabase.AssetPathToGUID(configPath), Is.EqualTo(guid));
                Assert.That(AssetDatabase.LoadAssetAtPath<FactoryBuildingImportSettings>(configPath).data.colliders, Is.Empty);
                draft.DeleteBuilding(key);
                draft.Save();
                foreach (string path in new[] { FactoryContentDraft.BuildingsPath, FactoryContentDraft.NamesPath, FactoryContentDraft.PortsPath, FactoryContentDraft.PresentationPath, "buildings/processor_stats.csv" })
                    Assert.That(draft.ReadTable(path).Text, Does.Not.Contain(key));
                Assert.That(AssetDatabase.AssetPathToGUID(prefabPath), Is.EqualTo(prefabGuid));
                Assert.That(File.Exists(configPath), Is.True, "Deleting CSV content retains import/model assets.");
            }
            finally
            {
                foreach (var backup in backups) File.WriteAllBytes(backup.Key, backup.Value);
                foreach (string path in backups.Keys) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                AssetDatabase.DeleteAsset(configPath);
                if (!hadFolder && Directory.Exists(FactoryContentDraft.BuildingImportDirectory) && Directory.GetFiles(FactoryContentDraft.BuildingImportDirectory).Length == 0)
                {
                    Assert.That(Path.GetFullPath(FactoryContentDraft.BuildingImportDirectory), Does.StartWith(Path.GetFullPath(Application.dataPath)));
                    AssetDatabase.DeleteAsset(FactoryContentDraft.BuildingImportDirectory);
                }
            }
        }

        private static void Click(Button button)
        {
            Vector2 point = button.worldBound.center;
            using (var down = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 0, mousePosition = point }))
                button.SendEvent(down);
            using (var up = PointerUpEvent.GetPooled(new Event { type = EventType.MouseUp, button = 0, mousePosition = point }))
                button.SendEvent(up);
        }

        [UnityTest]
        public IEnumerator BuildingPage_ReadOnlyDiagramAndSeparateImportWindow()
        {
            var window = ScriptableObject.CreateInstance<FactoryContentManagerWindow>();
            FactoryBuildingImportWindow preview = null;
            try
            {
                window.Show(); yield return null;
                window.CreateGUI();
                preview = window.PreviewBuildingImport(Json("furnace")); yield return null;
                Assert.That(window.rootVisualElement.Q("building-import-preview"), Is.Null);
                Assert.That(window.hasUnsavedChanges, Is.False, "Opening a preview must not alter the draft.");
                Assert.That(preview.rootVisualElement.Q<Toggle>("import-footprint").value, Is.False);
                Assert.That(preview.rootVisualElement.Q<Toggle>("import-ports").value, Is.False);
                Assert.That(preview.rootVisualElement.Q<Toggle>("import-colliders").value, Is.False);
                Click(preview.rootVisualElement.Q<Button>("import-cancel")); yield return null;
                Assert.That(preview == null, Is.True);
                Assert.That(window.hasUnsavedChanges, Is.False);

                preview = window.PreviewBuildingImport(Json("furnace")); yield return null;
                preview.rootVisualElement.Q<Toggle>("import-colliders").value = true;
                Click(preview.rootVisualElement.Q<Button>("import-apply")); yield return null;
                Assert.That(preview == null, Is.True, "Apply closes the separate preview.");
                Assert.That(window.rootVisualElement.Q<TextField>("building-key").isReadOnly, Is.True);
                Assert.That(window.rootVisualElement.Q<TextField>("building-key").value, Is.EqualTo("furnace"));
                Assert.That(window.hasUnsavedChanges, Is.True);
                var list = window.rootVisualElement.Q<ScrollView>("content-list");
                Assert.That(list.Query<FactoryBuildingGridView>().ToList(), Is.Empty);
                Assert.That(list.Query<Button>().ToList().All(b => b.name.StartsWith("building-row-")), Is.True);
                Assert.That(window.rootVisualElement.Q<FactoryBuildingGridView>("building-grid"), Is.Not.Null);
                Assert.That(window.rootVisualElement.Q<IntegerField>("building-width"), Is.Null);
                Assert.That(window.rootVisualElement.Q<IntegerField>("building-length"), Is.Null);
                Assert.That(window.rootVisualElement.Q<IntegerField>("building-output-slot"), Is.Null);
                Assert.That(window.rootVisualElement.Q<Button>("building-port-delete"), Is.Null);
                Assert.That(window.rootVisualElement.Q<Button>("building-port-reverse"), Is.Null);
                Assert.That(window.rootVisualElement.Q<Foldout>("building-space").Query<Button>().ToList(), Is.Empty);
                window.DiscardChanges();

                foreach (string key in new[] { "belt_mk1", "vertical_belt_entry_mk1", "vertical_belt_exit_mk1", "vertical_belt_segment_mk1", "foundation", "ramp_foundation_1_1", "merger", "splitter", "furnace" })
                {
                    int row = draft.GetBuildings().Single(b => b.key == key).row;
                    var button = window.rootVisualElement.Q<Button>("building-row-" + row);
                    button.Focus(); yield return null;
                    using (var submit = NavigationSubmitEvent.GetPooled()) button.SendEvent(submit);
                    yield return null;
                    Assert.That(window.rootVisualElement.Q<TextField>("building-key").value, Is.EqualTo(key));
                    bool visible = key == "merger" || key == "splitter" || key == "furnace";
                    Assert.That(window.rootVisualElement.Q<FactoryBuildingGridView>("building-grid") != null, Is.EqualTo(visible), key);
                    Assert.That(window.hasUnsavedChanges, Is.False, "Selecting and inspecting must not modify spatial data.");
                }
                preview = window.PreviewBuildingImport(Json()); yield return null;
                Assert.That(preview.rootVisualElement.Q<Toggle>("import-footprint").value, Is.True);
                Assert.That(preview.rootVisualElement.Q<Toggle>("import-ports").value, Is.True);
                Assert.That(preview.rootVisualElement.Q<Toggle>("import-colliders").value, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(window);
                if (preview != null) preview.Close();
            }
        }
    }
}
