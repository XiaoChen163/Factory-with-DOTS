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

        private void EnsureEditingMachine()
        {
            if (draft.GetBuildings().Any(b => b.key == "editing_fixture")) return;
            string key = draft.AddBuilding("editing_fixture");
            draft.SetBuildingBehavior(key, BuildingKind.Processor);
            draft.SetBuildingFootprint(key, 3, 3, true, "");
            draft.SetBuildingStat(key, "processor_stats", "machine_type_key", "test_machine_m");
            draft.SetBuildingModel(key, FactoryDatabaseEditor.Load().buildings.Single(b => b.key == "furnace").visualPrefab);
            for (int n = 0; n < 3; n++) draft.AddBuildingPort(key, new Vector2Int(3, n), Vector2Int.right, BuildingPortType.Output);
        }

        private string FixtureRecipe(string[] inputs, string[] outputs)
        {
            EnsureEditingMachine();
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
            string recipe = FixtureRecipe(new[] { "iron_ore" }, new[] { "iron_ore" });
            var references = draft.GetItemReferences("iron_ore");
            int row = draft.GetItems().Single(i => i.key == "iron_ore").row;
            var error = Assert.Throws<InvalidDataException>(() => draft.DeleteItem(row));
            Assert.That(error.Message, Does.Contain(recipe).And.Contain("原料").And.Contain("产物"));
            draft.SetItemValue(row, "key", "renamed_a");
            Assert.That(draft.GetItemReferences("iron_ore"), Is.Empty);
            Assert.That(draft.GetItemReferences("renamed_a").Count, Is.EqualTo(references.Count));
            Undo.PerformUndo();
            Assert.That(draft.GetItemReferences("renamed_a"), Is.Empty);
            Assert.That(draft.GetItemReferences("iron_ore"), Is.EqualTo(references));
            Assert.That(draft.GetItems().Single(i => i.row == row).key, Is.EqualTo("iron_ore"));
            Assert.Throws<InvalidDataException>(() => draft.SetItemValue(row, "id", "2"));
            Assert.Throws<InvalidDataException>(() => draft.SetItemValue(row, "key", "iron_ingot"));
        }

        [Test]
        public void StackReduction_WarnsWithoutChangingRecipesAndMergeChecksCombinedCount()
        {
            string recipe = FixtureRecipe(new[] { "iron_ore", "iron_ingot" }, new[] { "copper_ore" });
            var a = draft.GetItems().Single(i => i.key == "iron_ore");
            draft.AddIngredient(recipe, false, "iron_ore", 2);
            Assert.That(draft.GetIngredients(recipe, false).Single(i => i.itemKey == "iron_ore").count, Is.EqualTo(3));
            draft.SetItemValue(a.row, "max_stack", "2");
            Assert.That(draft.GetItemReferences("iron_ore", 2).Single(), Does.Contain(recipe));
            Assert.That(draft.GetIngredients(recipe, false).Single(i => i.itemKey == "iron_ore").count, Is.EqualTo(3));
            Assert.Catch(() => draft.ReadCandidate());
            string before = EditorJsonUtility.ToJson(draft);
            Assert.Throws<InvalidDataException>(() => draft.AddIngredient(recipe, false, "iron_ore"));
            int bRow = draft.GetIngredients(recipe, false).Single(i => i.itemKey == "iron_ingot").row;
            Assert.Throws<InvalidDataException>(() => draft.ReplaceIngredient(recipe, false, bRow, "iron_ore"));
            Assert.That(EditorJsonUtility.ToJson(draft), Is.EqualTo(before));
            draft.SetIngredientCount(recipe, false, draft.GetIngredients(recipe, false).Single(i => i.itemKey == "iron_ore").row, 1);
            draft.ReplaceIngredient(recipe, false, bRow, "iron_ore");
            Assert.That(draft.GetIngredients(recipe, false).Count, Is.EqualTo(1));
            Assert.That(draft.GetIngredients(recipe, false)[0].count, Is.EqualTo(2));
        }

        [Test]
        public void RecipeEditing_PreservesUnrelatedRowsAndEnforcesExistingOutputSlotRules()
        {
            string key = FixtureRecipe(Array.Empty<string>(), new[] { "iron_ore", "iron_ingot" });
            var others = draft.GetRecipes().Where(r => r.key != key).SelectMany(r =>
                draft.GetIngredients(r.key, true).Select(i => r.key + ":" + i.itemKey)).ToArray();
            draft.MoveOutput(key, 1, -1);
            Assert.That(draft.GetIngredients(key, true).Select(i => i.itemKey), Is.EqualTo(new[] { "iron_ingot", "iron_ore" }));
            var candidate = draft.ReadCandidate();
            Object.DestroyImmediate(candidate);
            Assert.That(draft.GetRecipes().Where(r => r.key != key).SelectMany(r =>
                draft.GetIngredients(r.key, true).Select(i => r.key + ":" + i.itemKey)), Is.EqualTo(others));
            draft.AddIngredient(key, true, "copper_ore");
            draft.AddIngredient(key, true, "copper_ingot");
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
            var backup = Object.Instantiate(FactoryDatabaseEditor.Load());
            try
            {
                EnsureEditingMachine();
                int itemRow = draft.AddItem();
                var item = draft.GetItems().Single(i => i.row == itemRow);
                Assert.That(item.id, Is.EqualTo(backup.items.Max(i => i.id) + 1));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Items/test_a/test_a.fbx");
                Assert.That(prefab, Is.Not.Null);
                draft.SetItemValue(itemRow, "model_path", FactoryItemAssetBindings.ModelPath(prefab));
                var icon = FactoryDatabaseEditor.BuildSpriteIndex(FactoryDatabaseEditor.ItemIconDirectory).Values.First();
                draft.SetItemIcon(itemRow, icon);
                int recipeRow = draft.AddRecipe();
                draft.SetRecipeValue(recipeRow, "machine_type_key", "test_machine_m");
                string key = draft.GetRecipes().Single(r => r.row == recipeRow).key;
                draft.AddIngredient(key, false, item.key);
                draft.SetIngredientCount(key, false, draft.GetIngredients(key, false)[0].row, 3);
                draft.AddIngredient(key, true, "iron_ore");
                draft.AddIngredient(key, true, "iron_ingot");
                draft.MoveOutput(key, 1, -1);
                draft.Save();
                draft.Load();
                Assert.That(draft.GetIngredients(key, false)[0].count, Is.EqualTo(3));
                Assert.That(draft.GetIngredients(key, true).Select(i => i.itemKey), Is.EqualTo(new[] { "iron_ingot", "iron_ore" }));
                var database = AssetDatabase.LoadAssetAtPath<FactoryDatabaseAsset>(FactoryDatabaseEditor.DatabaseAssetPath);
                Assert.That(database.items.Single(i => i.key == item.key).model, Is.EqualTo(prefab));
                Assert.That(database.items.Single(i => i.key == item.key).icon, Is.EqualTo(icon));
                Assert.That(database.recipes.Single(r => r.key == key).outputs.Select(i => i.itemKey), Is.EqualTo(new[] { "iron_ingot", "iron_ore" }));
                draft.DeleteRecipe(draft.GetRecipes().Single(r => r.key == key).row);
                draft.DeleteItem(draft.GetItems().Single(i => i.key == item.key).row);
                draft.AddMachineType("new_machine_type");
                Assert.That(draft.GetMachineTypes(), Does.Contain("new_machine_type"));
            }
            finally
            {
                FactoryDatabaseEditor.Save(backup);
                Object.DestroyImmediate(backup);
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
                var idField = window.rootVisualElement.Q<IntegerField>("item-id");
                Assert.That(idField.isDelayed, Is.True);
                idField.value = 60000;
                Assert.That(window.hasUnsavedChanges, Is.True);
                Assert.That(idField.value, Is.EqualTo(60000));
                window.DiscardChanges();
                window.rootVisualElement.Q<ToolbarSearchField>("content-search").value = "铁矿";
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
                root.Q<TextField>("item-picker-search").value = "铁矿";
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
                Assert.That(picked?.key, Is.EqualTo("iron_ore"));
            }
            finally
            {
                if (popup?.editorWindow != null) popup.editorWindow.Close();
                Object.DestroyImmediate(window);
            }
        }
    }
}
