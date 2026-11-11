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
            AssetDatabase.DeleteAsset(Prefab);
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.DeleteAsset(MaterialFolder);
            if (!string.IsNullOrEmpty(createdMaterialPath)) AssetDatabase.DeleteAsset(createdMaterialPath);
            createdMaterialPath = null;
        }

        [Test]
        public void JsonFbx_SaveAndReopen_BindsFbxAndCollidersWithoutWrapper()
        {
            var draft = ScriptableObject.CreateInstance<FactoryContentDraft>();
            draft.Load();
            var database = AssetDatabase.LoadAssetAtPath<FactoryDatabaseAsset>(FactoryDatabaseEditor.DatabaseAssetPath);
            var databaseBackup = FactoryDatabaseEditor.CreateSnapshot(database);
            databaseBackup.name = database.name;
            try
            {
                string json = File.ReadAllText("Tools/Blender/Fixtures/testMachine/testMachine.factory.json").Replace("\"testMachine\"", "\"" + Key + "\"");
                draft.ImportBuildingJson(FactoryBuildingJson.Parse(json), true, true, true);
                draft.SetBuildingBehavior(Key, BuildingKind.Storage);
                var model = FactoryModelPipeline.Import(Key, Source);
                draft.SetBuildingModel(Key, model);
                draft.Save();
                draft.Load();
                Assert.That(draft.IsModified, Is.False);
                Assert.That(draft.GetBuildingImport(Key).model, Is.EqualTo(model));
                Assert.That(File.Exists(Prefab), Is.False);
                Assert.That(draft.GetBuildingImport(Key).colliders.Length, Is.EqualTo(2));
                Assert.That(draft.GetBuildings().Single(b => b.key == Key).modelPath, Is.EqualTo(AssetDatabase.GetAssetPath(model)));
                Assert.That(AssetDatabase.LoadAssetAtPath<FactoryDatabaseAsset>(FactoryDatabaseEditor.DatabaseAssetPath).buildings.Single(b => b.key == Key).visualPrefab, Is.EqualTo(model));
            }
            finally
            {
                // Restore the loaded native asset through Unity rather than truncating
                // its memory-mapped file while the editor still has it loaded.
                FactoryDatabaseEditor.Save(databaseBackup);
                Object.DestroyImmediate(databaseBackup);
                Object.DestroyImmediate(draft);
            }
        }

        [Test]
        public void Blender41_AsymmetricTestMachine_UsesProtocolCoordinatesAndStableAssets()
        {
            var model = FactoryModelPipeline.Import(Key, Source);
            string path = AssetDatabase.GetAssetPath(model);
            Assert.That(path, Does.StartWith(Folder));
            Assert.That(FactoryModelPipeline.Import(Key, path), Is.EqualTo(model), "Project FBX is referenced directly.");
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
            var sources = FactoryModelPipeline.Sources(path);
            Assert.That(sources.Length, Is.GreaterThan(0));
            bool hadMatch = FactoryModelPipeline.Matches(sources[0].name).Length != 0;
            var createdMaterial = FactoryModelPipeline.CreateMaterial(path, sources[0]);
            if (!hadMatch) createdMaterialPath = AssetDatabase.GetAssetPath(createdMaterial);
            Assert.That(createdMaterial.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
            Assert.That(FactoryModelPipeline.CreateMaterial(path, sources[0]), Is.EqualTo(createdMaterial));
            Assert.That(FactoryModelPipeline.Matches(sources[0].name), Does.Contain(createdMaterial));
            Directory.CreateDirectory(MaterialFolder);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, MaterialFolder + "/Manual.mat");
            material.SetColor("_BaseColor", Color.magenta);
            FactoryModelPipeline.Remap(path, sources[0], material);
            var json = FactoryBuildingJson.Read("Tools/Blender/Fixtures/testMachine/testMachine.factory.json");
            string modelGuid = AssetDatabase.AssetPathToGUID(path);
            FactoryModelPipeline.Import(Key, Source);
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(modelGuid));
            Assert.That(File.Exists(Prefab), Is.False);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer.GetExternalObjectMap()[sources[0]], Is.EqualTo(material));
            Assert.That(material.GetColor("_BaseColor"), Is.EqualTo(Color.magenta));
            Assert.That(FactoryModelPipeline.CreateMaterial(path, sources[0]), Is.EqualTo(material));
            // Exercise the real Baker, including blob registration. Baking APIs are internal
            // in Entities 1.4; reflection stays confined to this acceptance test.
            var world = new World("FBX catalog baking acceptance");
            var catalogRoot = new GameObject("Catalog");
            var authoring = catalogRoot.AddComponent<BuildingPrefabCatalogAuthoring>();
            var candidate = FactoryDatabaseEditor.ReadCandidate();
            // Two definitions share an FBX but require independent physics.
            int furnace = Array.FindIndex(candidate.buildings, b => b.key == "furnace");
            int storage = Array.FindIndex(candidate.buildings, b => b.key == "storage");
            candidate.buildings[furnace].visualPrefab = model;
            candidate.buildings[furnace].colliders = json.Colliders;
            candidate.buildings[storage].visualPrefab = model;
            candidate.buildings[storage].colliders = new[] { new FactoryColliderProxy { name = "wide", type = "Box", size = new Vector3(8, 1, 1) } };
            candidate.items[1].model = candidate.items[0].model;
            authoring.database = candidate;
            authoring.inputPortVisualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Ports/InputPortView.prefab");
            authoring.outputPortVisualPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Ports/OutputPortView.prefab");
            var hybrid = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Unity.Entities.Hybrid");
            var storeType = hybrid.GetType("Unity.Entities.BlobAssetStore");
            var store = (IDisposable)Activator.CreateInstance(storeType, new object[] { 128 });
            try
            {
                var settingsType = hybrid.GetType("Unity.Entities.BakingSettings");
                var settings = Activator.CreateInstance(settingsType);
                settingsType.GetProperty("BlobAssetStore").SetValue(settings, store);
                var bake = hybrid.GetType("Unity.Entities.BakingUtility").GetMethod("BakeGameObjects", BindingFlags.Static | BindingFlags.NonPublic);
                bake.Invoke(null, new object[] { world, new[] { catalogRoot }, settings });
                var manager = world.EntityManager;
                using var itemQuery = manager.CreateEntityQuery(typeof(ItemPrefabEntry));
                var owner = itemQuery.GetSingletonEntity();
                var itemEntries = manager.GetBuffer<ItemPrefabEntry>(owner);
                Entity itemA = itemEntries[0].Prefab, itemB = itemEntries[1].Prefab;
                Assert.That(itemA, Is.Not.EqualTo(itemB), "Shared FBX models require separate item identities.");
                Assert.That(manager.GetComponentData<Item>(itemA).ItemType.Value, Is.EqualTo(candidate.items[0].id));
                Assert.That(manager.GetComponentData<Item>(itemB).ItemType.Value, Is.EqualTo(candidate.items[1].id));
                var runtimeItem = manager.Instantiate(itemA);
                using (var members = manager.GetBuffer<LinkedEntityGroup>(runtimeItem).ToNativeArray(Unity.Collections.Allocator.Temp))
                    foreach (var member in members)
                    {
                        Assert.That(manager.HasComponent<Unity.Physics.PhysicsCollider>(member.Value), Is.False);
                        Assert.That(manager.HasComponent<Unity.Physics.PhysicsVelocity>(member.Value), Is.False);
                    }
                manager.DestroyEntity(runtimeItem);
                using var templates = manager.CreateEntityQuery(new EntityQueryDesc { All = new[] { ComponentType.ReadOnly<Item>() }, Options = EntityQueryOptions.IncludePrefab });
                Assert.That(templates.CalculateEntityCount(), Is.EqualTo(candidate.items.Length));
                world.GetExistingSystemManaged<ItemPrefabBakingSystem>().Update();
                Assert.That(templates.CalculateEntityCount(), Is.EqualTo(candidate.items.Length), "Repeated baking must replace previous templates.");
                using var query = world.EntityManager.CreateEntityQuery(typeof(BuildingVisualPrefabEntry));
                var entries = world.EntityManager.GetBuffer<BuildingVisualPrefabEntry>(query.GetSingletonEntity());
                var first = entries.ToNativeArray(Unity.Collections.Allocator.Temp);
                try
                {
                    var a = first.ToArray().Single(e => e.BuildingDefinition.Value == candidate.buildings[furnace].id);
                    var b = first.ToArray().Single(e => e.BuildingDefinition.Value == candidate.buildings[storage].id);
                    Assert.That(a.Prefab, Is.EqualTo(b.Prefab));
                    Assert.That(a.Collider.IsCreated && b.Collider.IsCreated, Is.True);
                    Assert.That(a.Collider, Is.Not.EqualTo(b.Collider));
                    Assert.That(b.Collider.Value.CalculateAabb().Extents.x, Is.EqualTo(8).Within(.001f));
                    Assert.That(world.EntityManager.HasComponent<Prefab>(a.Prefab), Is.True);
                }
                finally { first.Dispose(); }
            }
            finally { world.Dispose(); store.Dispose(); Object.DestroyImmediate(catalogRoot); Object.DestroyImmediate(candidate); }
        }
    }
}
