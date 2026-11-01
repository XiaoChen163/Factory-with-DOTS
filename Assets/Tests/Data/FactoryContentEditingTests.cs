using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
                var field = window.rootVisualElement.Query<TextField>().First();
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
        public void Save_RebuildsDatabaseAndCanBeReadByANewDraft()
        {
            var draft = ScriptableObject.CreateInstance<FactoryContentDraft>();
            var reopened = ScriptableObject.CreateInstance<FactoryContentDraft>();
            var backups = new Dictionary<string, byte[]>();
            try
            {
                draft.Load();
                int namesIndex = draft.Tables.ToList().FindIndex(t => t.path.EndsWith("building_names.csv"));
                int itemsIndex = draft.Tables.ToList().FindIndex(t => t.path.EndsWith("/items.csv"));
                foreach (string path in new[] { draft.Tables[namesIndex].path, draft.Tables[itemsIndex].path, FactoryDatabaseCsvImporter.DatabaseAssetPath })
                    backups.Add(path, File.ReadAllBytes(path));
                var names = FactoryCsvTable.Parse(draft.Tables[namesIndex].text);
                int row = Enumerable.Range(0, names.RowCount).First(r => names.Get(r, "key") == "furnace" && names.Get(r, "locale") == "zh-CN");
                const string displayName = "测试熔炉,\"中文\"\n下一行";
                draft.SetCell(namesIndex, row, "name", displayName);
                draft.SetCell(itemsIndex, 0, "name_key", "测试物品名称");
                draft.Save();
                Assert.That(draft.IsModified, Is.False);
                Assert.That(AssetDatabase.LoadAssetAtPath<FactoryDatabaseAsset>(FactoryDatabaseCsvImporter.DatabaseAssetPath)
                    .buildings.Single(b => b.key == "furnace").displayName, Is.EqualTo(displayName));
                reopened.Load();
                var candidate = reopened.ReadCandidate();
                try
                {
                    Assert.That(candidate.buildings.Single(b => b.key == "furnace").displayName, Is.EqualTo(displayName));
                    Assert.That(candidate.items.Any(i => i.nameKey == "测试物品名称"), Is.True);
                }
                finally { Object.DestroyImmediate(candidate); }
            }
            finally
            {
                foreach (var backup in backups) File.WriteAllBytes(backup.Key, backup.Value);
                foreach (string path in backups.Keys) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                Object.DestroyImmediate(draft);
                Object.DestroyImmediate(reopened);
            }
        }

        [Test]
        public void CsvEdit_RoundTripsEscapesAndPreservesUnrelatedRecords()
        {
            const string source = "# 注释中的\"引号\r\nkey,locale,name,extra\r\nfurnace,en-US,Furnace,untouched\r\nfurnace,zh-CN,熔炉,x\r\n\r\n";
            var table = FactoryCsvTable.Parse(source);
            Assert.That(table.Text, Is.EqualTo(source));
            const string name = "中文,\"名称\"\r\n第二行\n末行";
            table.Set(1, "name", name);
            var reread = FactoryCsvTable.Parse(table.Text);
            Assert.That(reread.Get(1, "name"), Is.EqualTo(name));
            Assert.That(reread.Get(1, "extra"), Is.EqualTo("x"));
            Assert.That(table.Text, Does.StartWith("# 注释中的\"引号\r\nkey,locale,name,extra\r\nfurnace,en-US,Furnace,untouched\r\n"));
            Assert.That(table.Text, Does.EndWith("\r\n\r\n"));
        }

        [TestCase("key,name\na,\"unclosed")]
        [TestCase("key,name\na,\"closed\"junk\n")]
        [TestCase("key,key\na,b\n")]
        public void CsvRead_RejectsMalformedText(string text) =>
            Assert.Throws<InvalidDataException>(() => FactoryCsvTable.Parse(text));

        [Test]
        public void Draft_EditUndoRedoAndDiscard_DoNotWriteSources()
        {
            var draft = ScriptableObject.CreateInstance<FactoryContentDraft>();
            try
            {
                draft.Load();
                int index = draft.Tables.ToList().FindIndex(t => t.path.EndsWith("building_names.csv"));
                string original = draft.Tables[index].original;
                draft.SetCell(index, 0, "name", "草稿名称");
                Assert.That(draft.IsModified, Is.True);
                Undo.PerformUndo();
                Assert.That(draft.IsModified, Is.False);
                Undo.PerformRedo();
                Assert.That(FactoryCsvTable.Parse(draft.Tables[index].text).Get(0, "name"), Is.EqualTo("草稿名称"));
                Assert.That(File.ReadAllText(draft.Tables[index].path), Is.EqualTo(original));
                draft.Load();
                Assert.That(draft.IsModified, Is.False);
            }
            finally { Object.DestroyImmediate(draft); }
        }

        [Test]
        public void InvalidDraft_SaveLeavesAllSourcesAndGeneratedDatabaseIntact()
        {
            var draft = ScriptableObject.CreateInstance<FactoryContentDraft>();
            try
            {
                draft.Load();
                var bytes = draft.Tables.ToDictionary(t => t.path, t => File.ReadAllBytes(t.path));
                byte[] database = File.ReadAllBytes(FactoryDatabaseCsvImporter.DatabaseAssetPath);
                int index = draft.Tables.ToList().FindIndex(t => t.path.EndsWith("/items.csv"));
                draft.SetCell(index, 0, "max_stack", "0");
                Assert.Catch(() => draft.Save());
                foreach (var file in bytes) Assert.That(File.ReadAllBytes(file.Key), Is.EqualTo(file.Value));
                Assert.That(File.ReadAllBytes(FactoryDatabaseCsvImporter.DatabaseAssetPath), Is.EqualTo(database));
                Assert.That(draft.IsModified, Is.True);
            }
            finally { Object.DestroyImmediate(draft); }
        }

        [Test]
        public void ValidCandidate_SaveAndRebuildReadsEditedFieldWithoutChangingBuildingIds()
        {
            var draft = ScriptableObject.CreateInstance<FactoryContentDraft>();
            string directory = Path.Combine("Temp", "FactoryCsv-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                draft.Load();
                int index = draft.Tables.ToList().FindIndex(t => t.path.EndsWith("building_names.csv"));
                var names = FactoryCsvTable.Parse(draft.Tables[index].text);
                int row = Enumerable.Range(0, names.RowCount).First(r => names.Get(r, "key") == "furnace" && names.Get(r, "locale") == "zh-CN");
                const string changed = "新熔炉,\"中文\"\n第二行";
                draft.SetCell(index, row, "name", changed);
                var candidate = draft.ReadCandidate();
                try { Assert.That(candidate.buildings.Single(b => b.key == "furnace").displayName, Is.EqualTo(changed)); }
                finally { Object.DestroyImmediate(candidate); }
                var files = draft.Tables.ToDictionary(t => t.path, t => Path.Combine(directory, Path.GetFileName(t.path)));
                foreach (var t in draft.Tables) File.WriteAllText(files[t.path], t.original);
                string target = files[draft.Tables[index].path];
                int rebuilt = 0;
                FactoryCsvSaveTransaction.Commit(new Dictionary<string, string> { [target] = draft.Tables[index].text }, () =>
                {
                    rebuilt++;
                    var reread = FactoryDatabaseCsvImporter.ReadCandidate(path => FactoryCsvTable.Read(files[path]));
                    try
                    {
                        Assert.That(reread.buildings.Single(b => b.key == "furnace").displayName, Is.EqualTo(changed));
                        Assert.That(reread.buildings.Select(b => b.id), Is.EqualTo(Enumerable.Range(1, reread.buildings.Length).Select(n => (ushort)n)));
                    }
                    finally { Object.DestroyImmediate(reread); }
                });
                Assert.That(rebuilt, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(draft);
                foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }

        [Test]
        public void MultiTableWriteFailure_RestoresBytesAndDoesNotRebuild()
        {
            string directory = Path.Combine("Temp", "FactoryCsv-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string first = Path.Combine(directory, "first.csv");
            string second = Path.Combine(directory, "second.csv");
            File.WriteAllText(first, "name\r\n原文\r\n", new UTF8Encoding(true));
            File.WriteAllText(second, "name\nother\n");
            byte[] original = File.ReadAllBytes(first);
            try
            {
                // Read sharing permits the backup; denying write forces the second write to fail.
                using (var locked = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    int rebuilt = 0;
                    var error = Assert.Throws<IOException>(() => FactoryCsvSaveTransaction.Commit(
                        new Dictionary<string, string> { [first] = "name\nchanged\n", [second] = "name\nchanged\n" }, () => rebuilt++));
                    Assert.That(error.Message, Does.Contain(second));
                    Assert.That(File.ReadAllBytes(first), Is.EqualTo(original));
                    Assert.That(rebuilt, Is.Zero);
                }
            }
            finally
            {
                File.Delete(first);
                File.Delete(second);
                Directory.Delete(directory);
            }
        }
    }
}
