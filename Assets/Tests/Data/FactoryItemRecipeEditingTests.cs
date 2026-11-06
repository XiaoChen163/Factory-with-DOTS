using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Factory.Tests
{
    public sealed class FactoryItemRecipeEditingTests
    {
        private FactoryContentDraft draft;

        [SetUp]
        public void SetUp() { draft = ScriptableObject.CreateInstance<FactoryContentDraft>(); draft.Load(); }

        [TearDown]
        public void TearDown() { Object.DestroyImmediate(draft); }

        private string FixtureRecipe(string[] inputs, string[] outputs)
        {
            int row = draft.AddRecipe();
            draft.SetRecipeValue(row, "machine_type_key", "test_machine_m");
            string key = draft.GetRecipes().Single(r => r.row == row).key;
            foreach (string item in inputs) draft.AddIngredient(key, false, item);
            foreach (string item in outputs) draft.AddIngredient(key, true, item);
            return key;
        }

        [Test]
        public void ItemRename_UpdatesBothRecipeSidesAsOneUndoAndBlocksReferencedDeletion()
        {
            string recipe = FixtureRecipe(new[] { "test_a" }, new[] { "test_a" });
            var references = draft.GetItemReferences("test_a");
            int row = draft.GetItems().Single(i => i.key == "test_a").row;
            var error = Assert.Throws<InvalidDataException>(() => draft.DeleteItem(row));
            Assert.That(error.Message, Does.Contain(recipe).And.Contain("原料").And.Contain("产物"));
            draft.SetItemValue(row, "key", "renamed_a");
            Assert.That(draft.GetItemReferences("test_a"), Is.Empty);
            Assert.That(draft.GetItemReferences("renamed_a").Count, Is.EqualTo(references.Count));
            Undo.PerformUndo();
            Assert.That(draft.GetItemReferences("renamed_a"), Is.Empty);
            Assert.That(draft.GetItemReferences("test_a"), Is.EqualTo(references));
            Assert.That(draft.GetItems().Single(i => i.row == row).key, Is.EqualTo("test_a"));
            Assert.Throws<InvalidDataException>(() => draft.SetItemValue(row, "id", "1"));
            Assert.Throws<InvalidDataException>(() => draft.SetItemValue(row, "key", "test_b"));
        }

        [Test]
        public void StackReduction_WarnsWithoutChangingRecipesAndMergeChecksCombinedCount()
        {
            string recipe = FixtureRecipe(new[] { "test_a", "test_b" }, new[] { "test_c" });
            var a = draft.GetItems().Single(i => i.key == "test_a");
            draft.AddIngredient(recipe, false, "test_a", 2);
            Assert.That(draft.GetIngredients(recipe, false).Single(i => i.itemKey == "test_a").count, Is.EqualTo(3));
            draft.SetItemValue(a.row, "max_stack", "2");
            Assert.That(draft.GetItemReferences("test_a", 2).Single(), Does.Contain(recipe));
            Assert.That(draft.GetIngredients(recipe, false).Single(i => i.itemKey == "test_a").count, Is.EqualTo(3));
            Assert.Catch(() => draft.ReadCandidate());
            var before = draft.Tables.Select(t => t.text).ToArray();
            Assert.Throws<InvalidDataException>(() => draft.AddIngredient(recipe, false, "test_a"));
            int bRow = draft.GetIngredients(recipe, false).Single(i => i.itemKey == "test_b").row;
            Assert.Throws<InvalidDataException>(() => draft.ReplaceIngredient(recipe, false, bRow, "test_a"));
            Assert.That(draft.Tables.Select(t => t.text), Is.EqualTo(before));
            draft.SetIngredientCount(recipe, false, draft.GetIngredients(recipe, false).Single(i => i.itemKey == "test_a").row, 1);
            draft.ReplaceIngredient(recipe, false, bRow, "test_a");
            Assert.That(draft.GetIngredients(recipe, false).Count, Is.EqualTo(1));
            Assert.That(draft.GetIngredients(recipe, false)[0].count, Is.EqualTo(2));
        }

        [Test]
        public void RecipeEditing_PreservesUnrelatedRowsAndEnforcesExistingOutputSlotRules()
        {
            string key = FixtureRecipe(Array.Empty<string>(), new[] { "test_a", "test_b" });
            var before = draft.ReadTable(FactoryContentDraft.OutputsPath);
            string[] others = Enumerable.Range(0, before.RowCount).Where(r => before.Get(r, "recipe_key") != key)
                .Select(r => before.Get(r, "recipe_key") + ":" + before.Get(r, "item_key")).ToArray();
            draft.MoveOutput(key, 1, -1);
            Assert.That(draft.GetIngredients(key, true).Select(i => i.itemKey), Is.EqualTo(new[] { "test_b", "test_a" }));
            var candidate = draft.ReadCandidate();
            Object.DestroyImmediate(candidate);
            var after = draft.ReadTable(FactoryContentDraft.OutputsPath);
            Assert.That(Enumerable.Range(0, after.RowCount).Where(r => after.Get(r, "recipe_key") != key)
                .Select(r => after.Get(r, "recipe_key") + ":" + after.Get(r, "item_key")), Is.EqualTo(others));
            draft.AddIngredient(key, true, "test_c");
            draft.AddIngredient(key, true, "test_d");
            Assert.Catch(() => draft.ReadCandidate(), "The processor has no outlet for slot 4.");
            int row = draft.GetRecipes().Single(r => r.key == key).row;
            draft.SetRecipeValue(row, "key", "renamed_recipe");
            Assert.That(draft.GetIngredients(key, false), Is.Empty);
            Assert.That(draft.GetIngredients("renamed_recipe", true).Count, Is.EqualTo(4));
            draft.DeleteRecipe(row);
            Assert.That(draft.GetIngredients("renamed_recipe", true), Is.Empty);
            Assert.That(draft.GetIngredients("renamed_recipe", false), Is.Empty);
        }

        [Test]
        public void NewItemAndRecipe_SaveReopenPreservesCountsBindingsAndOutputOrder()
        {
            var backups = draft.Tables.ToDictionary(t => t.path, t => File.ReadAllBytes(t.path));
            backups.Add(FactoryDatabaseCsvImporter.DatabaseAssetPath, File.ReadAllBytes(FactoryDatabaseCsvImporter.DatabaseAssetPath));
            try
            {
                int itemRow = draft.AddItem();
                var item = draft.GetItems().Single(i => i.row == itemRow);
                Assert.That(item.id, Is.EqualTo(7));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Items/test_a/test_a.fbx");
                Assert.That(prefab, Is.Not.Null);
                draft.SetItemValue(itemRow, "model_path", FactoryItemAssetBindings.ModelPath(prefab));
                var icon = FactoryDatabaseCsvImporter.BuildSpriteIndex(FactoryDatabaseCsvImporter.ItemIconDirectory).Values.First();
                draft.SetItemValue(itemRow, "icon_key", FactoryItemAssetBindings.IconKey(icon));
                int recipeRow = draft.AddRecipe();
                draft.SetRecipeValue(recipeRow, "machine_type_key", "test_machine_m");
                string key = draft.GetRecipes().Single(r => r.row == recipeRow).key;
                draft.AddIngredient(key, false, item.key);
                draft.SetIngredientCount(key, false, draft.GetIngredients(key, false)[0].row, 3);
                draft.AddIngredient(key, true, "test_a");
                draft.AddIngredient(key, true, "test_b");
                draft.MoveOutput(key, 1, -1);
                draft.Save();
                draft.Load();
                Assert.That(draft.GetIngredients(key, false)[0].count, Is.EqualTo(3));
                Assert.That(draft.GetIngredients(key, true).Select(i => i.itemKey), Is.EqualTo(new[] { "test_b", "test_a" }));
                var database = AssetDatabase.LoadAssetAtPath<FactoryDatabaseAsset>(FactoryDatabaseCsvImporter.DatabaseAssetPath);
                Assert.That(database.items.Single(i => i.key == item.key).model, Is.EqualTo(prefab));
                Assert.That(database.items.Single(i => i.key == item.key).icon, Is.EqualTo(icon));
                Assert.That(database.recipes.Single(r => r.key == key).outputs.Select(i => i.itemKey), Is.EqualTo(new[] { "test_b", "test_a" }));
                draft.DeleteRecipe(draft.GetRecipes().Single(r => r.key == key).row);
                draft.DeleteItem(draft.GetItems().Single(i => i.key == item.key).row);
                draft.AddMachineType("new_machine_type");
                Assert.That(draft.GetMachineTypes(), Does.Contain("new_machine_type"));
            }
            finally
            {
                foreach (var backup in backups) File.WriteAllBytes(backup.Key, backup.Value);
                foreach (string path in backups.Keys) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }

        [UnityTest]
        public IEnumerator WindowAndPicker_SearchByNameAndSelectItem()
        {
            var window = ScriptableObject.CreateInstance<FactoryContentManagerWindow>();
            FactoryItemSearchPopup popup = null;
            try
            {
                window.Show();
                yield return null;
                window.CreateGUI();
                Assert.That(window.rootVisualElement.Query<ToolbarToggle>().ToList().Count, Is.EqualTo(3));
                var idField = window.rootVisualElement.Q<TextField>("item-id");
                idField.value = "";
                Assert.That(window.hasUnsavedChanges, Is.True, "Incomplete numeric input must still mark the draft dirty.");
                idField.value = "10";
                Assert.That(idField.value, Is.EqualTo("10"));
                window.DiscardChanges();
                window.rootVisualElement.Q<ToolbarSearchField>("content-search").value = "红色";
                Assert.That(window.rootVisualElement.Q<ScrollView>("content-list").Query<Button>().ToList().Count, Is.EqualTo(1));
                window.rootVisualElement.Q<ToolbarToggle>("tab-recipes").value = true;
                Assert.That(window.rootVisualElement.Q<TextField>("recipe-key"), Is.Not.Null);
                window.Focus();
                yield return null;
                FactoryContentItem picked = null;
                popup = new FactoryItemSearchPopup(draft.GetItems(), _ => null, item => picked = item);
                UnityEditor.PopupWindow.Show(new Rect(100, 100, 100, 20), popup);
                // Editor popup panels can attach after more than two updates when
                // a preceding test has just closed/repainted another editor window.
                for (int frame = 0; frame < 60 && popup.editorWindow != null && popup.editorWindow.rootVisualElement.panel == null; frame++)
                {
                    popup.editorWindow.Repaint();
                    yield return null;
                }
                Assert.That(popup.editorWindow, Is.Not.Null);
                var root = popup.editorWindow.rootVisualElement;
                Assert.That(root.panel, Is.Not.Null, "The popup must be attached to an editor panel before interacting.");
                root.Q<TextField>("item-picker-search").value = "红色";
                var buttons = root.Q<ScrollView>("item-picker-list").Query<Button>().ToList();
                Assert.That(buttons.Count, Is.EqualTo(1));
                // Submit before the next editor update: native focus loss can
                // close a hidden batch popup while its visual tree is retained.
                // Hidden batch popups do not reliably
                // receive pointer capture from the operating system.
                buttons[0].Focus();
                using (var submit = NavigationSubmitEvent.GetPooled()) buttons[0].SendEvent(submit);
                float deadline = Time.realtimeSinceStartup + 1;
                while (picked == null && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(picked?.key, Is.EqualTo("test_a"));
            }
            finally
            {
                if (popup?.editorWindow != null) popup.editorWindow.Close();
                Object.DestroyImmediate(window);
            }
        }
    }
}
