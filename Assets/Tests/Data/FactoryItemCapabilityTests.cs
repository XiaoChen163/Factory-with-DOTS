using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Factory.Tests
{
    public sealed class FactoryItemCapabilityTests
    {
        private FactoryDatabaseAsset backup;
        private FactoryContentDraft draft;
        private bool saved;
        private const int GunRow = 0, AmmoRow = 1;
        [SetUp] public void SetUp()
        {
            backup = FactoryDatabaseEditor.CreateSnapshot(FactoryDatabaseEditor.Load());
            draft = ScriptableObject.CreateInstance<FactoryContentDraft>(); draft.Load(); saved = false;
        }
        [TearDown] public void TearDown()
        {
            try { if (saved) FactoryDatabaseEditor.Save(backup); }
            finally { Object.DestroyImmediate(draft); Object.DestroyImmediate(backup); }
        }
        private void Configure()
        {
            draft.AddItemCapability(AmmoRow, ItemCapabilityKind.Ammunition);
            draft.EditItemCapability<AmmunitionCapabilityDefinition>(AmmoRow, c => c.baseDamage = 23);
            draft.AddItemCapability(GunRow, ItemCapabilityKind.Gun);
            draft.EditItemCapability<GunCapabilityDefinition>(GunRow, c =>
            {
                c.ammunitionItem = new ItemId { Value = (ushort)draft.GetItems()[AmmoRow].id };
                c.fireMode = GunFireMode.Automatic; c.roundsPerMinute = 720; c.magazineCapacity = 24; c.reloadSeconds = 1.5f;
            });
            draft.AddItemCapability(GunRow, ItemCapabilityKind.WeaponPresentation);
            draft.EditItemCapability<WeaponPresentationCapabilityDefinition>(GunRow, c => c.firstPersonPrefab = backup.items[GunRow].model);
        }
        private void Save() { saved = true; draft.Save(); }

        [Test]
        public void RootReferencesThreeLibrariesWithoutDuplicatingRows()
        {
            var source = FactoryDatabaseEditor.Load();
            Assert.That(source.itemDatabase, Is.Not.Null); Assert.That(source.recipeDatabase, Is.Not.Null); Assert.That(source.buildingDatabase, Is.Not.Null);
            Assert.That(ReferenceEquals(source.items, source.itemDatabase.items), Is.True);
            var serialized = new SerializedObject(source);
            foreach (string field in new[] { "inlineItems", "inlineRecipes", "inlineBuildings", "inlineMachines" })
                Assert.That(serialized.FindProperty(field).arraySize, Is.Zero);
        }

        [Test]
        public void CapabilityEditsUndoRedoAndSerializedDraftLeaveSourceUntouched()
        {
            string source = FactoryDatabaseEditor.SourceJson(); string disk = FactoryDatabaseEditor.SourceBytes();
            draft.AddItemCapability(AmmoRow, ItemCapabilityKind.Ammunition);
            draft.EditItemCapability<AmmunitionCapabilityDefinition>(AmmoRow, c => c.baseDamage = 47);
            Undo.PerformUndo();
            var cap = (AmmunitionCapabilityDefinition)draft.ReadItemCapability(AmmoRow, ItemCapabilityKind.Ammunition);
            try { Assert.That(cap.baseDamage, Is.EqualTo(10)); } finally { Object.DestroyImmediate(cap); }
            Undo.PerformRedo();
            var restored = ScriptableObject.CreateInstance<FactoryContentDraft>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(draft), restored);
                cap = (AmmunitionCapabilityDefinition)restored.ReadItemCapability(AmmoRow, ItemCapabilityKind.Ammunition);
                try { Assert.That(cap.baseDamage, Is.EqualTo(47)); } finally { Object.DestroyImmediate(cap); }
                Assert.That(restored.IsModified, Is.True);
            }
            finally { Object.DestroyImmediate(restored); }
            Assert.That(FactoryDatabaseEditor.SourceJson(), Is.EqualTo(source)); Assert.That(FactoryDatabaseEditor.SourceBytes(), Is.EqualTo(disk));
            draft.Load(); Assert.That(draft.GetItemCapabilities(AmmoRow), Is.Empty);
        }

        [Test]
        public void SaveReopenUsesPrivateSubAssetsPreservesLocalIdAndCleansRemovedCapabilities()
        {
            Configure(); Save();
            var source = FactoryDatabaseEditor.Load();
            var stored = FactoryItemCapabilityValidation.Get<GunCapabilityDefinition>(source.items[GunRow]);
            Assert.That(AssetDatabase.IsSubAsset(stored), Is.True);
            Assert.That(AssetDatabase.GetAssetPath(stored), Is.EqualTo(AssetDatabase.GetAssetPath(source.itemDatabase)));
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(stored, out string guid, out long fileId);
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(source.itemDatabase)).OfType<ItemCapabilityDefinition>().Count(), Is.EqualTo(3));
            draft.Load(); draft.EditItemCapability<GunCapabilityDefinition>(GunRow, c => c.magazineCapacity = 31); Save();
            stored = FactoryItemCapabilityValidation.Get<GunCapabilityDefinition>(source.items[GunRow]);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(stored, out string nextGuid, out long nextId);
            Assert.That((nextGuid, nextId), Is.EqualTo((guid, fileId))); Assert.That(stored.magazineCapacity, Is.EqualTo(31));
            Assert.That(FactoryItemCapabilityValidation.Get<WeaponPresentationCapabilityDefinition>(source.items[GunRow]).firstPersonPrefab, Is.EqualTo(backup.items[GunRow].model));
            draft.RemoveItemCapability(GunRow, ItemCapabilityKind.Gun); Save();
            Assert.That(FactoryItemCapabilityValidation.Get<GunCapabilityDefinition>(source.items[GunRow]), Is.Null);
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(source.itemDatabase)).OfType<ItemCapabilityDefinition>().Count(), Is.EqualTo(2));
        }

        [Test]
        public void DuplicateTypesAndSharedInstancesAreRejected()
        {
            draft.AddItemCapability(AmmoRow, ItemCapabilityKind.Ammunition);
            Assert.Throws<InvalidDataException>(() => draft.AddItemCapability(AmmoRow, ItemCapabilityKind.Ammunition));
            var candidate = draft.ReadCandidate();
            try
            {
                candidate.items[GunRow].capabilities = candidate.items[AmmoRow].capabilities;
                Assert.Throws<InvalidDataException>(() => FactoryDatabaseValidation.Validate(candidate));
                candidate.items[GunRow].capabilities = Array.Empty<ItemCapabilityDefinition>();
                candidate.items[AmmoRow].capabilities = new[] { candidate.items[AmmoRow].capabilities[0], candidate.items[AmmoRow].capabilities[0] };
                Assert.Throws<InvalidDataException>(() => FactoryDatabaseValidation.Validate(candidate));
            }
            finally { Object.DestroyImmediate(candidate); }
        }

        [TestCase(0)] [TestCase(-1)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void InvalidGunTimingCannotBeSaved(float rpm)
        {
            Configure(); draft.EditItemCapability<GunCapabilityDefinition>(GunRow, c => c.roundsPerMinute = rpm);
            string disk = FactoryDatabaseEditor.SourceBytes();
            Assert.Throws<InvalidDataException>(() => draft.Save()); Assert.That(FactoryDatabaseEditor.SourceBytes(), Is.EqualTo(disk));
        }

        [Test]
        public void IntegerAmmunitionDamageAcceptsZeroAndMaximumButRejectsNegative()
        {
            draft.AddItemCapability(AmmoRow, ItemCapabilityKind.Ammunition);
            foreach (int damage in new[] { 0, int.MaxValue })
            {
                draft.EditItemCapability<AmmunitionCapabilityDefinition>(AmmoRow, c => c.baseDamage = damage);
                var candidate = draft.ReadCandidate();
                try
                {
                    Assert.That(FactoryDatabaseBakingUtility.TryBuild(candidate, null, out var blob), Is.True);
                    try { Assert.That(blob.Value.AmmunitionByItemId[candidate.items[AmmoRow].id].BaseDamage, Is.EqualTo(damage)); }
                    finally { blob.Dispose(); }
                }
                finally { Object.DestroyImmediate(candidate); }
            }
            draft.EditItemCapability<AmmunitionCapabilityDefinition>(AmmoRow, c => c.baseDamage = -1);
            Assert.Throws<InvalidDataException>(() => draft.ReadCandidate());
        }

        [Test]
        public void RemovingAmmoCapabilityInvalidatesGunAndIdRenameUpdatesItsReference()
        {
            Configure();
            draft.SetItemValue(AmmoRow, "id", "60000");
            var candidate = draft.ReadCandidate();
            try { Assert.That(FactoryItemCapabilityValidation.Get<GunCapabilityDefinition>(candidate.items[GunRow]).ammunitionItem.Value, Is.EqualTo(60000)); }
            finally { Object.DestroyImmediate(candidate); }
            draft.RemoveItemCapability(AmmoRow, ItemCapabilityKind.Ammunition);
            Assert.Throws<InvalidDataException>(() => draft.ReadCandidate());
        }

        [Test]
        public void ReferencedAmmoItemCannotBeDeletedAndRemovedItemDropsItsCapabilities()
        {
            int row = draft.AddItem();
            draft.AddItemCapability(row, ItemCapabilityKind.Ammunition);
            draft.AddItemCapability(GunRow, ItemCapabilityKind.Gun);
            draft.EditItemCapability<GunCapabilityDefinition>(GunRow, c => c.ammunitionItem = new ItemId { Value = (ushort)draft.GetItems()[row].id });
            Assert.Throws<InvalidDataException>(() => draft.DeleteItem(row));
            draft.RemoveItemCapability(GunRow, ItemCapabilityKind.Gun);
            draft.DeleteItem(row);
            Assert.That(draft.GetItems().Count, Is.EqualTo(backup.items.Length));
            var candidate = draft.ReadCandidate();
            try { Assert.That(candidate.items.All(i => (i.capabilities?.Length ?? 0) == 0), Is.True); }
            finally { Object.DestroyImmediate(candidate); }
        }

        [Test]
        public void UnsavedPresentationResourcesCannotEnterPersistentDefinitions()
        {
            Configure();
            var controller = new AnimatorOverrideController();
            try
            {
                draft.EditItemCapability<WeaponPresentationCapabilityDefinition>(GunRow, c => c.firstPersonAnimations = controller);
                Assert.Throws<InvalidDataException>(() => draft.ReadCandidate());
            }
            finally { Object.DestroyImmediate(controller); }
        }

        [Test]
        public void BakedDefinitionsMatchAuthoringAndPresentationRemainsManaged()
        {
            Configure(); var candidate = draft.ReadCandidate();
            var presentation = ScriptableObject.CreateInstance<FactoryPresentationCatalog>();
            try
            {
                Assert.That(FactoryDatabaseBakingUtility.TryBuild(candidate, null, out var blob), Is.True);
                try
                {
                    var gun = blob.Value.GunsByItemId[candidate.items[GunRow].id];
                    Assert.That(gun.MagazineCapacity, Is.EqualTo(24)); Assert.That(gun.RoundsPerMinute, Is.EqualTo(720));
                    Assert.That(gun.AmmunitionItem.Value, Is.EqualTo(candidate.items[AmmoRow].id));
                    Assert.That(blob.Value.AmmunitionByItemId[gun.AmmunitionItem.Value].BaseDamage, Is.EqualTo(23));
                    Assert.That(blob.Value.GunsByItemId[candidate.items[AmmoRow].id].Item.IsValid, Is.False);
                }
                finally { blob.Dispose(); }
                typeof(FactoryPresentationCatalog).GetField("database", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(presentation, candidate);
                presentation.BuildIndex();
                Assert.That(presentation.GetWeaponPresentation(new ItemId { Value = candidate.items[GunRow].id }).firstPersonPrefab, Is.EqualTo(backup.items[GunRow].model));
                Assert.That(presentation.GetWeaponPresentation(new ItemId { Value = candidate.items[AmmoRow].id }), Is.Null);
            }
            finally { Object.DestroyImmediate(presentation); Object.DestroyImmediate(candidate); }
        }

        [Test]
        public void LibraryLockAndExternalSubAssetEditRejectSaveWithoutPartialWrites()
        {
            Configure(); Save();
            draft.EditItemCapability<GunCapabilityDefinition>(GunRow, c => c.magazineCapacity = 40);
            string disk = FactoryDatabaseEditor.SourceBytes();
            string path = AssetDatabase.GetAssetPath(FactoryDatabaseEditor.Load().itemDatabase);
            AssetDatabase.ReleaseCachedFileHandles();
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Throws<IOException>(() => draft.Save());
            Assert.That(FactoryDatabaseEditor.SourceBytes(), Is.EqualTo(disk));
            var stored = FactoryItemCapabilityValidation.Get<GunCapabilityDefinition>(FactoryDatabaseEditor.Load().items[GunRow]);
            stored.magazineCapacity = 17;
            Assert.Throws<IOException>(() => draft.Save());
            Assert.That(stored.magazineCapacity, Is.EqualTo(17));
        }

        [UnityTest]
        public IEnumerator ManagerShowsTypedCapabilityFieldsAndRemovesThemThroughDraft()
        {
            var window = ScriptableObject.CreateInstance<FactoryContentManagerWindow>();
            try
            {
                window.Show(); yield return null; window.CreateGUI();
                var windowDraft = (FactoryContentDraft)new SerializedObject(window).FindProperty("draft").objectReferenceValue;
                windowDraft.AddItemCapability(0, ItemCapabilityKind.Ammunition); window.CreateGUI();
                var field = window.rootVisualElement.Q<IntegerField>("ammo-damage");
                Assert.That(field, Is.Not.Null); field.value = 19;
                Assert.That(window.hasUnsavedChanges, Is.True);
                var value = (AmmunitionCapabilityDefinition)windowDraft.ReadItemCapability(0, ItemCapabilityKind.Ammunition);
                try { Assert.That(value.baseDamage, Is.EqualTo(19)); } finally { Object.DestroyImmediate(value); }
                windowDraft.RemoveItemCapability(0, ItemCapabilityKind.Ammunition); window.CreateGUI();
                Assert.That(window.rootVisualElement.Q<IntegerField>("ammo-damage"), Is.Null);
                window.DiscardChanges();
            }
            finally { Object.DestroyImmediate(window); }
        }
    }
}
