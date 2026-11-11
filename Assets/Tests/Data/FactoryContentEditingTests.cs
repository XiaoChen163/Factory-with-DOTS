using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Factory.Tests
{
    public sealed class FactoryContentEditingTests
    {
        [UnityTest]
        public IEnumerator Window_FieldEditAndUndoUpdateUnsavedPrompt()
        {
            var window = ScriptableObject.CreateInstance<FactoryContentManagerWindow>();
            try
            {
                window.Show();
                yield return null;
                window.CreateGUI();
                var field = window.rootVisualElement.Q<TextField>("item-name");
                Assert.That(field, Is.Not.Null);
                Assert.That(field.isDelayed, Is.False, "Typing must mark the draft dirty before the field loses focus.");
                field.value += "draft";
                Assert.That(window.hasUnsavedChanges, Is.True);
                Undo.PerformUndo();
                Assert.That(window.hasUnsavedChanges, Is.False);
                window.DiscardChanges();
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void Save_AssetRoundTripPreservesUnicodeReferencesAndGuid()
        {
            var asset = FactoryDatabaseEditor.Load();
            var backup = Object.Instantiate(asset);
            var draft = ScriptableObject.CreateInstance<FactoryContentDraft>();
            var reopened = ScriptableObject.CreateInstance<FactoryContentDraft>();
            string guid = AssetDatabase.AssetPathToGUID(FactoryDatabaseEditor.DatabaseAssetPath);
            try
            {
                draft.Load();
                const string name = "测试熔炉,\"中文\"\n下一行";
                draft.SetBuildingName("furnace", name);
                draft.SetItemValue(0, "name_key", name);
                draft.Save();
                Assert.That(draft.IsModified, Is.False);
                reopened.Load();
                var candidate = reopened.ReadCandidate();
                try
                {
                    Assert.That(candidate.buildings.Single(b => b.key == "furnace").displayName, Is.EqualTo(name));
                    Assert.That(candidate.items[0].nameKey, Is.EqualTo(name));
                    Assert.That(candidate.items[0].model, Is.EqualTo(backup.items[0].model));
                    Assert.That(candidate.items[0].icon, Is.EqualTo(backup.items[0].icon));
                    Assert.That(AssetDatabase.AssetPathToGUID(FactoryDatabaseEditor.DatabaseAssetPath), Is.EqualTo(guid));
                }
                finally { Object.DestroyImmediate(candidate); }
            }
            finally
            {
                FactoryDatabaseEditor.Save(backup);
                Object.DestroyImmediate(backup); Object.DestroyImmediate(draft); Object.DestroyImmediate(reopened);
            }
        }

        [Test]
        public void Draft_UndoRedoDiscardAndSerializationDoNotWriteSource()
        {
            var draft = ScriptableObject.CreateInstance<FactoryContentDraft>();
            var restored = ScriptableObject.CreateInstance<FactoryContentDraft>();
            byte[] disk = File.ReadAllBytes(FactoryDatabaseEditor.DatabaseAssetPath);
            string source = EditorJsonUtility.ToJson(FactoryDatabaseEditor.Load());
            try
            {
                draft.Load();
                draft.SetBuildingName("furnace", "草稿名称");
                Assert.That(draft.IsModified, Is.True);
                Undo.PerformUndo(); Assert.That(draft.IsModified, Is.False);
                Undo.PerformRedo();
                Assert.That(draft.GetBuildings().Single(b => b.key == "furnace").name, Is.EqualTo("草稿名称"));
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(draft), restored);
                Assert.That(restored.IsModified, Is.True);
                Assert.That(restored.GetItems()[0].model, Is.EqualTo(draft.GetItems()[0].model));
                Assert.That(File.ReadAllBytes(FactoryDatabaseEditor.DatabaseAssetPath), Is.EqualTo(disk));
                Assert.That(EditorJsonUtility.ToJson(FactoryDatabaseEditor.Load()), Is.EqualTo(source));
                draft.Load(); Assert.That(draft.IsModified, Is.False);
            }
            finally { Object.DestroyImmediate(draft); Object.DestroyImmediate(restored); }
        }

        [Test]
        public void InvalidDraft_DoesNotModifySavedAsset()
        {
            var draft = ScriptableObject.CreateInstance<FactoryContentDraft>();
            byte[] disk = File.ReadAllBytes(FactoryDatabaseEditor.DatabaseAssetPath);
            string source = EditorJsonUtility.ToJson(FactoryDatabaseEditor.Load());
            try
            {
                draft.Load();
                draft.AddItem(); // Missing FBX.
                Assert.Throws<InvalidDataException>(() => draft.Save());
                Assert.That(draft.IsModified, Is.True);
                Assert.That(File.ReadAllBytes(FactoryDatabaseEditor.DatabaseAssetPath), Is.EqualTo(disk));
                Assert.That(EditorJsonUtility.ToJson(FactoryDatabaseEditor.Load()), Is.EqualTo(source));
            }
            finally { Object.DestroyImmediate(draft); }
        }

        [Test]
        public void CompetingDraftSave_RejectsStaleSourceWithoutOverwritingIt()
        {
            var backup = Object.Instantiate(FactoryDatabaseEditor.Load());
            var first = ScriptableObject.CreateInstance<FactoryContentDraft>();
            var second = ScriptableObject.CreateInstance<FactoryContentDraft>();
            try
            {
                first.Load(); second.Load();
                first.SetBuildingName("furnace", "第一个窗口"); first.Save();
                second.SetBuildingName("furnace", "过期窗口");
                Assert.Throws<IOException>(() => second.Save());
                Assert.That(FactoryDatabaseEditor.Load().buildings.Single(b => b.key == "furnace").displayName, Is.EqualTo("第一个窗口"));
                Assert.That(second.IsModified, Is.True);
            }
            finally
            {
                FactoryDatabaseEditor.Save(backup);
                Object.DestroyImmediate(backup); Object.DestroyImmediate(first); Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void DiskOrInspectorChanges_RejectStaleDraft()
        {
            var asset = FactoryDatabaseEditor.Load();
            var backup = Object.Instantiate(asset);
            var draft = ScriptableObject.CreateInstance<FactoryContentDraft>();
            byte[] disk = File.ReadAllBytes(FactoryDatabaseEditor.DatabaseAssetPath);
            try
            {
                draft.Load(); draft.SetBuildingName("furnace", "草稿");
                asset.items[0].nameKey = "Inspector 修改";
                Assert.Throws<IOException>(() => draft.Save());
                FactoryDatabaseEditor.CopyData(backup, asset);
                File.AppendAllText(FactoryDatabaseEditor.DatabaseAssetPath, "\n");
                Assert.Throws<IOException>(() => draft.Save());
            }
            finally
            {
                AssetDatabase.ReleaseCachedFileHandles();
                File.WriteAllBytes(FactoryDatabaseEditor.DatabaseAssetPath, disk);
                FactoryDatabaseEditor.CopyData(backup, asset);
                Object.DestroyImmediate(backup); Object.DestroyImmediate(draft);
            }
        }

        [Test]
        public void LockedAsset_SaveFailsBeforeMutationAndCanBeRetried()
        {
            var asset = FactoryDatabaseEditor.Load();
            var backup = Object.Instantiate(asset);
            var draft = ScriptableObject.CreateInstance<FactoryContentDraft>();
            string source = EditorJsonUtility.ToJson(asset);
            byte[] disk = File.ReadAllBytes(FactoryDatabaseEditor.DatabaseAssetPath);
            try
            {
                draft.Load(); draft.SetBuildingName("furnace", "稍后保存");
                AssetDatabase.ReleaseCachedFileHandles();
                using (new FileStream(FactoryDatabaseEditor.DatabaseAssetPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Assert.Throws<IOException>(() => draft.Save());
                    Assert.That(EditorJsonUtility.ToJson(asset), Is.EqualTo(source));
                    Assert.That(File.ReadAllBytes(FactoryDatabaseEditor.DatabaseAssetPath), Is.EqualTo(disk));
                    Assert.That(draft.IsModified, Is.True);
                }
                draft.Save(); Assert.That(draft.IsModified, Is.False);
            }
            finally { FactoryDatabaseEditor.Save(backup); Object.DestroyImmediate(backup); Object.DestroyImmediate(draft); }
        }

        [Test]
        public void AssetReorderingAndAddingContent_PreservesExistingIdsAndBindings()
        {
            var candidate = FactoryDatabaseEditor.ReadCandidate();
            try
            {
                var ids = candidate.buildings.ToDictionary(b => b.key, b => b.id);
                candidate.buildings = candidate.buildings.Reverse().ToArray();
                candidate.items = candidate.items.Reverse().ToArray();
                candidate.machineTypes = candidate.machineTypes.Reverse().ToArray();
                candidate.recipes = candidate.recipes.Reverse().ToArray();
                FactoryDatabaseEditor.Validate(candidate);
                Assert.That(FactoryDatabaseBakingUtility.TryBuild(candidate, null, out var blob), Is.True);
                try
                {
                    foreach (var pair in ids)
                        Assert.That(FactoryDatabaseUtility.GetBuildingDefinitionId(ref blob.Value, pair.Key).Value, Is.EqualTo(pair.Value));
                }
                finally { blob.Dispose(); }
            }
            finally { Object.DestroyImmediate(candidate); }
        }
    }
}
