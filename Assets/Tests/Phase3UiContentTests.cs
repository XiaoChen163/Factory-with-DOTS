using NUnit.Framework;
using UnityEngine.UIElements;

namespace Factory.Tests
{
    public sealed class Phase3UiContentTests
    {
        [Test]
        public void ItemGrid_RendersSnapshotWithoutAccessingEcs()
        {
            VisualElement root = new();
            ItemSlotGridView view = new(root, null);
            view.Render(new[]
            {
                new ItemSlotSnapshot(0, new ItemId { Value = 2 }, 17, 100,
                    default, UiSlotAccess.InsertAndExtract),
                new ItemSlotSnapshot(1, default, 0, 100,
                    default, UiSlotAccess.InsertAndExtract)
            });

            Assert.That(root.childCount, Is.EqualTo(2));
            Assert.That(root[0].Q<Label>("count").text, Is.EqualTo("17"));
            Assert.That(root[1].Q<Label>("count").text, Is.Empty);
        }

        [Test]
        public void ItemGrid_ReusesSlotElementsWhenRevisionContentChanges()
        {
            VisualElement root = new();
            ItemSlotGridView view = new(root, null);
            view.Render(new[] { Slot(1) });
            VisualElement first = root[0];

            view.Render(new[] { Slot(9) });

            Assert.That(root[0], Is.SameAs(first));
            Assert.That(root[0].Q<Label>("count").text, Is.EqualTo("9"));
        }

        // Guards pointer identity across continuously published UI snapshots.
        [Test]
        public void BuildingWindow_ReusesRecipeCardsAcrossSnapshotRefreshes()
        {
            VisualElement root = CreateBuildingWindowRoot();
            BuildingWindowView view = new(root, null);
            BuildingSnapshot snapshot = ProcessorBuilding(
                new RecipeId { Value = 1 },
                new RecipeId { Value = 2 });

            view.Render(snapshot);
            VisualElement container =
                root.Q<ScrollView>("recipe-list").contentContainer;
            VisualElement first = container[0];
            VisualElement second = container[1];

            view.Render(snapshot);

            Assert.That(container[0], Is.SameAs(first));
            Assert.That(container[1], Is.SameAs(second));
            Assert.That(container[0].userData,
                Is.EqualTo(new RecipeId { Value = 1 }));
        }

        [Test]
        public void BuildingWindow_RebindsPooledRecipeCardsWhenRecipesChange()
        {
            VisualElement root = CreateBuildingWindowRoot();
            BuildingWindowView view = new(root, null);
            view.Render(ProcessorBuilding(
                new RecipeId { Value = 1 },
                new RecipeId { Value = 2 }));
            VisualElement container =
                root.Q<ScrollView>("recipe-list").contentContainer;
            VisualElement first = container[0];

            view.Render(ProcessorBuilding(new RecipeId { Value = 7 }));

            Assert.That(container[0], Is.SameAs(first));
            Assert.That(container[0].userData,
                Is.EqualTo(new RecipeId { Value = 7 }));
            Assert.That(container[1].style.display.value,
                Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void BuildingWindow_ChangeRecipeButtonIsTopmostAndPickerSurvivesRefresh()
        {
            VisualElement root = CreateBuildingWindowRoot();
            BuildingWindowView view = new(root, null);
            BuildingSnapshot snapshot = ProcessorBuilding(
                new RecipeId { Value = 1 },
                new RecipeId { Value = 2 });
            snapshot.Processor.SelectedRecipeId =
                new RecipeId { Value = 1 };
            VisualElement processorPage = root.Q("building-processor-page");
            Button open = root.Q<Button>("recipe-picker-open");

            Assert.That(processorPage[processorPage.childCount - 1],
                Is.SameAs(open),
                "The absolute change-recipe button must stay above the flow layer.");
            view.Render(snapshot);
            view.OpenRecipePicker();
            view.Render(snapshot);

            Assert.That(view.IsRecipePickerShown, Is.True);
            Assert.That(root.Q("building-processor-page").style.display.value,
                Is.EqualTo(DisplayStyle.None));
            Assert.That(root.Q("building-recipe-page").style.display.value,
                Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void BuildingWindow_SwitchingBuildingsResetsRecipePickerState()
        {
            VisualElement root = CreateBuildingWindowRoot();
            BuildingWindowView view = new(root, null);
            BuildingSnapshot first = ProcessorBuilding(
                new RecipeId { Value = 1 });
            first.Processor.SelectedRecipeId = new RecipeId { Value = 1 };
            view.Render(first);
            view.OpenRecipePicker();

            BuildingSnapshot second = ProcessorBuilding(
                new RecipeId { Value = 1 });
            second.RuntimeId = new BuildingRuntimeId(2);
            second.Processor.SelectedRecipeId = new RecipeId { Value = 1 };
            view.Render(second);

            Assert.That(view.IsRecipePickerShown, Is.False);
            Assert.That(root.Q("building-processor-page").style.display.value,
                Is.EqualTo(DisplayStyle.Flex));
        }

        [Test]
        public void BuildCatalog_ClickingSelectedBuildingConfirmsSelection()
        {
            VisualElement root = CreateBuildCatalogRoot();
            BuildCatalogView view = new(root, null);
            BuildingLevelId first = new BuildingLevelId { Value = 1 };
            BuildingLevelId second = new BuildingLevelId { Value = 2 };
            BuildingLevelId confirmed = default;
            int confirmationCount = 0;
            view.BuildingSelected += value =>
            {
                confirmed = value;
                confirmationCount++;
            };
            view.Render(new BuildCatalogSnapshot(new[] { first, second }));

            view.SelectBuilding(first);
            Assert.That(view.SelectedBuildingLevel, Is.EqualTo(first));
            Assert.That(confirmationCount, Is.Zero);

            view.SelectBuilding(second);
            Assert.That(view.SelectedBuildingLevel, Is.EqualTo(second));
            Assert.That(confirmationCount, Is.Zero);

            view.SelectBuilding(second);
            Assert.That(confirmationCount, Is.EqualTo(1));
            Assert.That(confirmed, Is.EqualTo(second));
        }

        private static ItemSlotSnapshot Slot(ushort count) =>
            new(0, new ItemId { Value = 1 }, count, 100,
                default, UiSlotAccess.InsertAndExtract);

        private static BuildingSnapshot ProcessorBuilding(params RecipeId[] recipes) =>
            new()
            {
                RuntimeId = new BuildingRuntimeId(1),
                BuildingLevelId = new BuildingLevelId { Value = 1 },
                Kind = BuildingKind.Furnace,
                IsAvailable = true,
                Processor = new ProcessorSnapshot
                {
                    AvailableRecipes = recipes
                }
            };

        private static VisualElement CreateBuildingWindowRoot()
        {
            VisualElement root = new();
            root.Add(new Label { name = "building-title" });
            root.Add(new VisualElement { name = "building-storage-page" });
            VisualElement processorPage = new()
                { name = "building-processor-page" };
            processorPage.Add(new Button { name = "recipe-picker-open" });
            processorPage.Add(new VisualElement { name = "processor-flow" });
            root.Add(processorPage);
            root.Add(new VisualElement { name = "building-recipe-page" });
            root.Add(new Label { name = "building-empty" });
            root.Add(new VisualElement { name = "storage-slot-grid" });
            root.Add(new VisualElement { name = "processor-input-grid" });
            root.Add(new VisualElement { name = "processor-output-grid" });
            root.Add(new Label { name = "processor-status" });
            root.Add(new ProgressBar { name = "processor-progress" });
            root.Add(new ScrollView { name = "recipe-list" });
            root.Add(new Button { name = "recipe-picker-back" });
            return root;
        }

        private static VisualElement CreateBuildCatalogRoot()
        {
            VisualElement root = new();
            root.Add(new VisualElement { name = "build-catalog-list" });
            root.Add(new VisualElement { name = "catalog-detail-icon" });
            root.Add(new Label { name = "catalog-detail-name" });
            root.Add(new Label { name = "catalog-detail-description" });
            root.Add(new Button { name = "build-catalog-confirm" });
            return root;
        }
    }
}
