using System;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class ItemSlotGridView
{
    private readonly VisualElement root;
    private readonly FactoryPresentationCatalog catalog;

    public ItemSlotGridView(VisualElement root, FactoryPresentationCatalog catalog)
    {
        this.root = root ?? throw new ArgumentNullException(nameof(root));
        this.catalog = catalog;
    }

    public void Render(ItemSlotSnapshot[] slots)
    {
        slots ??= Array.Empty<ItemSlotSnapshot>();
        while (root.childCount < slots.Length)
            root.Add(CreateSlot());
        for (int i = 0; i < root.childCount; i++)
        {
            VisualElement slot = root[i];
            bool visible = i < slots.Length;
            slot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible) continue;
            ItemSlotSnapshot value = slots[i];
            VisualElement icon = slot.Q("icon");
            Label count = slot.Q<Label>("count");
            icon.style.backgroundImage = new StyleBackground(
                value.ItemId.IsValid ? catalog?.GetItemIcon(value.ItemId) : null);
            count.text = value.Count > 0 ? value.Count.ToString() : string.Empty;
            slot.tooltip = value.ItemId.IsValid
                ? $"{catalog?.GetItemName(value.ItemId) ?? value.ItemId.ToString()}  {value.Count}/{value.Capacity}"
                : "空槽位";
            slot.EnableInClassList("item-slot--restricted",
                value.Access == UiSlotAccess.Insert || value.Access == UiSlotAccess.Extract);
        }
    }

    private static VisualElement CreateSlot()
    {
        VisualElement slot = new();
        slot.AddToClassList("item-slot");
        VisualElement icon = new() { name = "icon", pickingMode = PickingMode.Ignore };
        icon.AddToClassList("item-slot__icon");
        Label count = new() { name = "count", pickingMode = PickingMode.Ignore };
        count.AddToClassList("item-slot__count");
        slot.Add(icon);
        slot.Add(count);
        return slot;
    }
}

public sealed class BuildingWindowView
{
    private readonly Label title;
    private readonly VisualElement storagePage;
    private readonly VisualElement processorPage;
    private readonly VisualElement recipePage;
    private readonly Label empty;
    private readonly ItemSlotGridView storage;
    private readonly ItemSlotGridView inputs;
    private readonly ItemSlotGridView outputs;
    private readonly Label status;
    private readonly ProgressBar progress;
    private readonly ScrollView recipes;
    private readonly Button recipeOpen;
    private readonly Button recipeBack;
    private readonly FactoryPresentationCatalog catalog;
    private bool showRecipePicker;
    private bool hasSelectedRecipe;

    public BuildingWindowView(VisualElement root, FactoryPresentationCatalog catalog)
    {
        this.catalog = catalog;
        title = Require<Label>(root, "building-title");
        storagePage = Require(root, "building-storage-page");
        processorPage = Require(root, "building-processor-page");
        recipePage = Require(root, "building-recipe-page");
        empty = Require<Label>(root, "building-empty");
        storage = new ItemSlotGridView(Require(root, "storage-slot-grid"), catalog);
        inputs = new ItemSlotGridView(Require(root, "processor-input-grid"), catalog);
        outputs = new ItemSlotGridView(Require(root, "processor-output-grid"), catalog);
        status = Require<Label>(root, "processor-status");
        progress = Require<ProgressBar>(root, "processor-progress");
        recipes = Require<ScrollView>(root, "recipe-list");
        recipeOpen = Require<Button>(root, "recipe-picker-open");
        recipeBack = Require<Button>(root, "recipe-picker-back");
        recipeOpen.clicked += () =>
        {
            showRecipePicker = true;
            ApplyProcessorPageState();
        };
        recipeBack.clicked += () =>
        {
            showRecipePicker = false;
            ApplyProcessorPageState();
        };
    }

    public void Render(BuildingSnapshot snapshot)
    {
        title.text = catalog?.GetBuildingName(snapshot.BuildingLevelId) ?? "建筑";
        bool isStorage = snapshot.Kind == BuildingKind.Storage;
        bool isProcessor = snapshot.Processor != null;
        storagePage.style.display = isStorage ? DisplayStyle.Flex : DisplayStyle.None;
        processorPage.style.display = DisplayStyle.None;
        recipePage.style.display = DisplayStyle.None;
        empty.style.display = !isStorage && !isProcessor ? DisplayStyle.Flex : DisplayStyle.None;
        if (isStorage) storage.Render(snapshot.StorageSlots);
        if (!isProcessor) return;
        ProcessorSnapshot value = snapshot.Processor;
        hasSelectedRecipe = value.SelectedRecipeId.IsValid;
        if (!hasSelectedRecipe)
            showRecipePicker = true;
        recipeBack.SetEnabled(hasSelectedRecipe);
        ApplyProcessorPageState();
        inputs.Render(value.Inputs);
        outputs.Render(value.Outputs);
        status.text = StatusText(value.Status);
        progress.value = value.Progress01 * 100f;
        progress.title = $"{Mathf.RoundToInt(value.Progress01 * 100f)}%";
        recipes.Clear();
        for (int i = 0; i < value.AvailableRecipes.Length; i++)
        {
            RecipeId id = value.AvailableRecipes[i];
            Button card = new() { text = catalog?.GetRecipeName(id) ?? $"配方 {id.Value}" };
            card.AddToClassList("recipe-card");
            card.EnableInClassList("recipe-card--selected", id == value.SelectedRecipeId);
            card.SetEnabled(false); // Phase 4 sends the authoritative selection command.
            recipes.Add(card);
        }
    }

    private void ApplyProcessorPageState()
    {
        processorPage.style.display = showRecipePicker
            ? DisplayStyle.None
            : DisplayStyle.Flex;
        recipePage.style.display = showRecipePicker
            ? DisplayStyle.Flex
            : DisplayStyle.None;
    }

    private static string StatusText(ItemProcessStatus value) => value switch
    {
        ItemProcessStatus.Processing => "生产中",
        ItemProcessStatus.Completed => "已完成",
        ItemProcessStatus.OutputBlocked => "输出受阻",
        _ => "空闲"
    };

    private static VisualElement Require(VisualElement root, string name) =>
        root.Q(name) ?? throw new InvalidOperationException($"Missing UI element '{name}'.");
    private static T Require<T>(VisualElement root, string name) where T : VisualElement =>
        root.Q<T>(name) ?? throw new InvalidOperationException($"Missing UI element '{name}'.");
}

public sealed class BuildCatalogView
{
    private readonly VisualElement list;
    private readonly FactoryPresentationCatalog catalog;
    private readonly VisualElement detailIcon;
    private readonly Label detailName;
    private readonly Label detailDescription;
    private readonly Button confirm;
    private BuildingLevelId selected;
    public event Action<BuildingLevelId> BuildingSelected;

    public BuildCatalogView(VisualElement root, FactoryPresentationCatalog catalog)
    {
        list = root.Q("build-catalog-list") ??
            throw new InvalidOperationException("Missing UI element 'build-catalog-list'.");
        this.catalog = catalog;
        detailIcon = root.Q("catalog-detail-icon") ??
            throw new InvalidOperationException("Missing UI element 'catalog-detail-icon'.");
        detailName = root.Q<Label>("catalog-detail-name") ??
            throw new InvalidOperationException("Missing UI element 'catalog-detail-name'.");
        detailDescription = root.Q<Label>("catalog-detail-description") ??
            throw new InvalidOperationException("Missing UI element 'catalog-detail-description'.");
        confirm = root.Q<Button>("build-catalog-confirm") ??
            throw new InvalidOperationException("Missing UI element 'build-catalog-confirm'.");
        confirm.clicked += ConfirmSelection;
        UpdateDetails();
    }

    public void SetSelected(BuildingLevelId value)
    {
        selected = value;
        RefreshSelection();
        UpdateDetails();
    }

    public void Render(BuildCatalogSnapshot snapshot)
    {
        list.Clear();
        for (int i = 0; i < snapshot.Levels.Length; i++)
        {
            BuildingLevelId id = snapshot.Levels[i];
            Button card = new();
            card.AddToClassList("catalog-card");
            card.userData = id;
            VisualElement icon = new() { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("catalog-card__icon");
            icon.style.backgroundImage = new StyleBackground(catalog?.GetBuildingIcon(id));
            Label label = new(catalog?.GetBuildingName(id) ?? $"建筑 {id.Value}")
                { pickingMode = PickingMode.Ignore };
            label.AddToClassList("catalog-card__label");
            card.Add(icon);
            card.Add(label);
            card.clicked += () => SetSelected(id);
            list.Add(card);
        }
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        for (int i = 0; i < list.childCount; i++)
        {
            VisualElement card = list[i];
            card.EnableInClassList("catalog-card--selected",
                card.userData is BuildingLevelId id && id == selected);
        }
    }

    private void UpdateDetails()
    {
        confirm.SetEnabled(selected.IsValid);
        detailIcon.style.backgroundImage = new StyleBackground(
            selected.IsValid ? catalog?.GetBuildingIcon(selected) : null);
        detailName.text = selected.IsValid
            ? catalog?.GetBuildingName(selected) ?? $"建筑 {selected.Value}"
            : "请选择建筑";
        detailDescription.text = selected.IsValid
            ? "选择该建筑并返回世界进行连续放置。"
            : "从中间列表选择要放置的建筑。";
    }

    private void ConfirmSelection()
    {
        if (selected.IsValid)
            BuildingSelected?.Invoke(selected);
    }
}
