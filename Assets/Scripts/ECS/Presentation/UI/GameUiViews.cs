using System;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class ItemSlotGridView
{
    private readonly VisualElement root;
    private readonly FactoryPresentationCatalog catalog;
    private Func<ItemSlotSnapshot, ItemEndpoint> endpointFactory;
    public event Action<ItemSlotBinding, PointerDownEvent> PointerDown;

    public ItemSlotGridView(VisualElement root, FactoryPresentationCatalog catalog)
    {
        this.root = root ?? throw new ArgumentNullException(nameof(root));
        this.catalog = catalog;
    }

    public void SetEndpointFactory(Func<ItemSlotSnapshot, ItemEndpoint> value) =>
        endpointFactory = value;

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
            ItemSlotBinding binding = slot.userData as ItemSlotBinding ?? new ItemSlotBinding();
            binding.Element = slot;
            binding.Snapshot = value;
            binding.Endpoint = endpointFactory != null ? endpointFactory(value) : default;
            slot.userData = binding;
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

    private VisualElement CreateSlot()
    {
        VisualElement slot = new();
        slot.AddToClassList("item-slot");
        VisualElement icon = new() { name = "icon", pickingMode = PickingMode.Ignore };
        icon.AddToClassList("item-slot__icon");
        Label count = new() { name = "count", pickingMode = PickingMode.Ignore };
        count.AddToClassList("item-slot__count");
        slot.Add(icon);
        slot.Add(count);
        slot.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (slot.userData is ItemSlotBinding binding)
                PointerDown?.Invoke(binding, evt);
        });
        return slot;
    }
}

public sealed class ItemSlotBinding
{
    public VisualElement Element { get; set; }
    public ItemSlotSnapshot Snapshot { get; set; }
    public ItemEndpoint Endpoint { get; set; }
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
    private bool recipePending;
    private BuildingRuntimeId runtimeId;
    public event Action<RecipeId> RecipeSelected;
    public event Action<ItemSlotBinding, PointerDownEvent> SlotPointerDown;
    public bool IsRecipePickerShown => showRecipePicker;

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
        storage.PointerDown += (binding, evt) => SlotPointerDown?.Invoke(binding, evt);
        inputs.PointerDown += (binding, evt) => SlotPointerDown?.Invoke(binding, evt);
        outputs.PointerDown += (binding, evt) => SlotPointerDown?.Invoke(binding, evt);
        status = Require<Label>(root, "processor-status");
        progress = Require<ProgressBar>(root, "processor-progress");
        recipes = Require<ScrollView>(root, "recipe-list");
        recipeOpen = Require<Button>(root, "recipe-picker-open");
        recipeBack = Require<Button>(root, "recipe-picker-back");
        recipeOpen.clicked += OpenRecipePicker;
        recipeBack.clicked += CloseRecipePicker;
        recipeOpen.BringToFront();
    }

    public void Render(BuildingSnapshot snapshot)
    {
        title.text = catalog?.GetBuildingName(snapshot.BuildingLevelId) ?? "建筑";
        bool buildingChanged = !runtimeId.Equals(snapshot.RuntimeId);
        runtimeId = snapshot.RuntimeId;
        if (buildingChanged)
        {
            showRecipePicker = false;
            recipePending = false;
        }
        storage.SetEndpointFactory(slot => new ItemEndpoint
        {
            OwnerKind = ItemOwnerKind.Storage,
            OwnerRuntimeId = runtimeId.Value,
            Domain = ItemSlotDomain.Inventory,
            SlotIndex = slot.SlotIndex
        });
        inputs.SetEndpointFactory(slot => new ItemEndpoint
        {
            OwnerKind = ItemOwnerKind.Processor,
            OwnerRuntimeId = runtimeId.Value,
            Domain = ItemSlotDomain.ProcessorInput,
            SlotIndex = slot.SlotIndex
        });
        outputs.SetEndpointFactory(slot => new ItemEndpoint
        {
            OwnerKind = ItemOwnerKind.Processor,
            OwnerRuntimeId = runtimeId.Value,
            Domain = ItemSlotDomain.ProcessorOutput,
            SlotIndex = slot.SlotIndex
        });
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
        VisualElement recipeContainer = recipes.contentContainer;
        while (recipeContainer.childCount < value.AvailableRecipes.Length)
            recipeContainer.Add(CreateRecipeCard());
        for (int i = 0; i < recipeContainer.childCount; i++)
        {
            Button card = (Button)recipeContainer[i];
            bool visible = i < value.AvailableRecipes.Length;
            card.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
                continue;
            RecipeId id = value.AvailableRecipes[i];
            card.userData = id;
            card.text = catalog?.GetRecipeName(id) ?? $"配方 {id.Value}";
            card.EnableInClassList("recipe-card--selected", id == value.SelectedRecipeId);
            card.SetEnabled(!recipePending);
            card.EnableInClassList("recipe-card--pending", recipePending);
        }
    }

    private Button CreateRecipeCard()
    {
        Button card = new();
        card.AddToClassList("recipe-card");
        card.clicked += () =>
        {
            if (card.userData is RecipeId id)
                RecipeSelected?.Invoke(id);
        };
        return card;
    }

    public void OpenRecipePicker()
    {
        if (!hasSelectedRecipe)
            return;
        showRecipePicker = true;
        ApplyProcessorPageState();
    }

    public void CloseRecipePicker()
    {
        if (!hasSelectedRecipe)
            return;
        showRecipePicker = false;
        ApplyProcessorPageState();
    }

    public void SetRecipePending(bool pending, bool selectionSucceeded = false)
    {
        recipePending = pending;
        if (selectionSucceeded)
        {
            showRecipePicker = false;
            ApplyProcessorPageState();
        }
        VisualElement recipeContainer = recipes.contentContainer;
        for (int i = 0; i < recipeContainer.childCount; i++)
        {
            VisualElement card = recipeContainer[i];
            card.SetEnabled(!pending);
            card.EnableInClassList("recipe-card--pending", pending);
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
    public BuildingLevelId SelectedBuildingLevel => selected;

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
        while (list.childCount < snapshot.Levels.Length)
            list.Add(CreateCatalogCard());
        for (int i = 0; i < list.childCount; i++)
        {
            Button card = (Button)list[i];
            bool visible = i < snapshot.Levels.Length;
            card.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
                continue;
            BuildingLevelId id = snapshot.Levels[i];
            card.userData = id;
            VisualElement icon = card.Q("catalog-card-icon");
            icon.style.backgroundImage = new StyleBackground(catalog?.GetBuildingIcon(id));
            card.Q<Label>("catalog-card-label").text =
                catalog?.GetBuildingName(id) ?? $"建筑 {id.Value}";
        }
        RefreshSelection();
    }

    private Button CreateCatalogCard()
    {
        Button card = new();
        card.AddToClassList("catalog-card");
        VisualElement icon = new()
        {
            name = "catalog-card-icon",
            pickingMode = PickingMode.Ignore
        };
        icon.AddToClassList("catalog-card__icon");
        Label label = new()
        {
            name = "catalog-card-label",
            pickingMode = PickingMode.Ignore
        };
        label.AddToClassList("catalog-card__label");
        card.Add(icon);
        card.Add(label);
        card.clicked += () =>
        {
            if (card.userData is BuildingLevelId id)
                SelectBuilding(id);
        };
        return card;
    }

    /// <summary>Handles a catalog card activation, including repeat confirmation.</summary>
    public void SelectBuilding(BuildingLevelId value)
    {
        if (value.IsValid && value == selected)
        {
            ConfirmSelection();
            return;
        }
        SetSelected(value);
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

    public void ConfirmSelection()
    {
        if (selected.IsValid)
            BuildingSelected?.Invoke(selected);
    }
}

public sealed class BuildShortcutBarView
{
    public const int SlotCount = 9;

    private readonly VisualElement[] slots = new VisualElement[SlotCount];
    private readonly FactoryPresentationCatalog catalog;

    public BuildShortcutBarView(
        VisualElement root,
        FactoryPresentationCatalog catalog)
    {
        if (root == null)
            throw new ArgumentNullException(nameof(root));
        this.catalog = catalog;
        for (int i = 0; i < SlotCount; i++)
        {
            VisualElement slot = root.Q($"build-shortcut-{i + 1}") ??
                throw new InvalidOperationException(
                    $"Missing UI element 'build-shortcut-{i + 1}'.");
            slots[i] = slot;
            slot.Q<Label>("key").text = (i + 1).ToString();
        }
    }

    public void Render(
        BuildingLevelId[] assignments,
        BuildingLevelId activeBuilding,
        bool isBuildMode)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            BuildingLevelId assignment =
                assignments != null && i < assignments.Length
                    ? assignments[i]
                    : default;
            VisualElement slot = slots[i];
            slot.Q("icon").style.backgroundImage = new StyleBackground(
                assignment.IsValid
                    ? catalog?.GetBuildingIcon(assignment)
                    : null);
            slot.Q<Label>("empty").style.display = assignment.IsValid
                ? DisplayStyle.None
                : DisplayStyle.Flex;
            slot.tooltip = assignment.IsValid
                ? $"{i + 1} · {catalog?.GetBuildingName(assignment) ?? $"建筑 {assignment.Value}"}"
                : $"快捷栏 {i + 1} · 未绑定";
            slot.EnableInClassList(
                "build-shortcut-slot--active",
                isBuildMode && assignment.IsValid && assignment == activeBuilding);
        }
    }
}
