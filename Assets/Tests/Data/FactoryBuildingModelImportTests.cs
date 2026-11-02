using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using Unity.Entities;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Factory.Tests
{
    public sealed class FactoryBuildingModelImportTests
    {
        private const string Key = "stage4_fixture";
        private const string Source = "Tools/Blender/Fixtures/testMachine/testMachine.fbx";
        private const string Folder = "Assets/Art/Buildings/" + Key;
        private const string Prefab = "Assets/Prefabs/Buildings/" + Key + ".prefab";
        private const string MaterialFolder = "Assets/Art/Materials/Buildings/stage4_fixture";
        private string createdMaterialPath;

        [TearDown]
        public void Cleanup()
        {
            AssetDatabase.DeleteAsset(FactoryContentDraft.BuildingImportPath(Key));
            AssetDatabase.DeleteAsset(Prefab);
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.DeleteAsset(MaterialFolder);
            if (!string.IsNullOrEmpty(createdMaterialPath)) AssetDatabase.DeleteAsset(createdMaterialPath);
            createdMaterialPath = null;
        }

        [Test]
        public void JsonFbx_GenerateSaveAndReopen_BindsModelPrefabAndColliders()
        {
            var draft = ScriptableObject.CreateInstance<FactoryContentDraft>();
            draft.Load();
            var backup = draft.Tables.ToDictionary(t => t.path, t => File.ReadAllBytes(t.path));
            backup.Add(FactoryDatabaseCsvImporter.DatabaseAssetPath, File.ReadAllBytes(FactoryDatabaseCsvImporter.DatabaseAssetPath));
            try
            {
                string json = File.ReadAllText("Tools/Blender/Fixtures/testMachine/testMachine.factory.json").Replace("\"testMachine\"", "\"" + Key + "\"");
                draft.ImportBuildingJson(FactoryBuildingJson.Parse(json), true, true, true);
                draft.SetBuildingBehavior(Key, BuildingKind.Storage);
                var model = FactoryBuildingModelPipeline.Import(Key, Source);
                draft.SetBuildingModel(Key, model);
                var prefab = draft.GenerateBuildingPrefab(Key);
                draft.Save();
                draft.Load();
                Assert.That(draft.IsModified, Is.False);
                Assert.That(draft.GetBuildingImport(Key).model, Is.EqualTo(model));
                Assert.That(draft.GetBuildingImport(Key).generatedPrefab, Is.EqualTo(prefab));
                Assert.That(draft.GetBuildingImport(Key).colliders.Length, Is.EqualTo(2));
                Assert.That(draft.GetBuildings().Single(b => b.key == Key).prefabPath, Is.EqualTo(Prefab));
                Assert.That(AssetDatabase.LoadAssetAtPath<FactoryDatabaseAsset>(FactoryDatabaseCsvImporter.DatabaseAssetPath).buildings.Single(b => b.key == Key).visualPrefab, Is.EqualTo(prefab));
            }
            finally
            {
                foreach (var entry in backup) File.WriteAllBytes(entry.Key, entry.Value);
                foreach (string path in backup.Keys) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Object.DestroyImmediate(draft);
            }
        }

        [Test]
        public void Blender41_AsymmetricTestMachine_UsesProtocolCoordinatesAndStableAssets()
        {
            var model = FactoryBuildingModelPipeline.Import(Key, Source);
            string path = AssetDatabase.GetAssetPath(model);
            Assert.That(path, Does.StartWith(Folder));
            Assert.That(FactoryBuildingModelPipeline.Import(Key, path), Is.EqualTo(model), "Project FBX is referenced directly.");
            var instance = Object.Instantiate(model);
            try
            {
                var pillar = instance.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "柱体");
                Assert.That(pillar.bounds.center.x, Is.EqualTo(.6f).Within(.001f));
                Assert.That(pillar.bounds.center.y, Is.EqualTo(1.9398389f).Within(.001f));
                Assert.That(pillar.bounds.center.z, Is.EqualTo(.75f).Within(.001f));
                Assert.That(pillar.bounds.size.y, Is.EqualTo(2f).Within(.001f));
                var body = instance.GetComponentsInChildren<MeshRenderer>().Single(r => r.name == "立方体");
                Assert.That(body.bounds.min.y, Is.EqualTo(0).Within(.001f), "Keep the exported bottom origin, no bounds recentering.");
            }
            finally { Object.DestroyImmediate(instance); }
            var sources = FactoryBuildingModelPipeline.Sources(path);
            Assert.That(sources.Length, Is.GreaterThan(0));
            bool hadMatch = FactoryBuildingModelPipeline.Matches(sources[0].name).Length != 0;
            var createdMaterial = FactoryBuildingModelPipeline.CreateMaterial(path, sources[0]);
            if (!hadMatch) createdMaterialPath = AssetDatabase.GetAssetPath(createdMaterial);
            Assert.That(createdMaterial.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
            Assert.That(FactoryBuildingModelPipeline.CreateMaterial(path, sources[0]), Is.EqualTo(createdMaterial));
            Assert.That(FactoryBuildingModelPipeline.Matches(sources[0].name), Does.Contain(createdMaterial));
            Directory.CreateDirectory(MaterialFolder);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, MaterialFolder + "/Manual.mat");
            material.SetColor("_BaseColor", Color.magenta);
            FactoryBuildingModelPipeline.Remap(path, sources[0], material);
            var json = FactoryBuildingJson.Read("Tools/Blender/Fixtures/testMachine/testMachine.factory.json");
            var data = new FactoryBuildingImportData { buildingKey = Key, model = model, colliders = json.Colliders };
            var prefab = FactoryBuildingModelPipeline.Generate(data, "");
            data.generatedPrefab = prefab;
            string guid = AssetDatabase.AssetPathToGUID(Prefab), modelGuid = AssetDatabase.AssetPathToGUID(path);
            var content = PrefabUtility.LoadPrefabContents(Prefab);
            new GameObject("Hand Authored Child").transform.SetParent(content.transform, false);
            PrefabUtility.SaveAsPrefabAsset(content, Prefab);
            PrefabUtility.UnloadPrefabContents(content);
            FactoryBuildingModelPipeline.Import(Key, Source);
            prefab = FactoryBuildingModelPipeline.Generate(data, Prefab);
            Assert.That(AssetDatabase.AssetPathToGUID(Prefab), Is.EqualTo(guid));
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(modelGuid));
            Assert.That(prefab.transform.Find("Hand Authored Child"), Is.Not.Null);
            Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(prefab.GetComponentInChildren<FactoryBuildingCollisionAuthoring>().shapes.Length, Is.EqualTo(2));
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.GetExternalObjectMap()[sources[0]], Is.EqualTo(material));
            Assert.That(material.GetColor("_BaseColor"), Is.EqualTo(Color.magenta));
            Assert.That(FactoryBuildingModelPipeline.CreateMaterial(path, sources[0]), Is.EqualTo(material));
            // Exercise the real Baker, including blob registration. Baking APIs are internal
            // in Entities 1.4; reflection stays confined to this acceptance test.
            var world = new World("Stage 4 baking acceptance");
            var hybrid = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Unity.Entities.Hybrid");
            var storeType = hybrid.GetType("Unity.Entities.BlobAssetStore");
            var store = (IDisposable)Activator.CreateInstance(storeType, new object[] { 128 });
            try
            {
                var settingsType = hybrid.GetType("Unity.Entities.BakingSettings");
                var settings = Activator.CreateInstance(settingsType);
                settingsType.GetProperty("BlobAssetStore").SetValue(settings, store);
                var bake = hybrid.GetType("Unity.Entities.BakingUtility").GetMethod("BakeGameObjects", BindingFlags.Static | BindingFlags.NonPublic);
                bake.Invoke(null, new object[] { world, new[] { prefab }, settings });
                using var query = world.EntityManager.CreateEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<FactoryBuildingCollider>() }, Options = EntityQueryOptions.IncludePrefab });
                Assert.That(query.CalculateEntityCount(), Is.EqualTo(1));
                Assert.That(query.GetSingleton<FactoryBuildingCollider>().Value.IsCreated, Is.True);
            }
            finally { world.Dispose(); store.Dispose(); }
        }
    }
}
