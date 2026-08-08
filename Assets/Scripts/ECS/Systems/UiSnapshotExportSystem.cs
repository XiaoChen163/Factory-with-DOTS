using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class UiSnapshotExportSystem : SystemBase
{
    private EntityQuery playerQuery;
    private EntityQuery buildingQuery;
    private EntityQuery databaseQuery;
    private readonly Dictionary<PlayerId, InventoryCache> inventoryCache = new();
    private readonly Dictionary<BuildingRuntimeId, BuildingCache> buildingCache = new();

    public int InventoryBufferReadsLastUpdate { get; private set; }
    public int BuildingBufferReadsLastUpdate { get; private set; }

    protected override void OnCreate()
    {
        playerQuery = GetEntityQuery(
            ComponentType.ReadOnly<PlayerIdentity>(),
            ComponentType.ReadOnly<PlayerInventory>(),
            ComponentType.ReadOnly<InventorySlot>());
        buildingQuery = GetEntityQuery(
            ComponentType.ReadOnly<ItemContainerIdentity>(),
            ComponentType.ReadOnly<GridPlacement>(),
            ComponentType.ReadOnly<BuildingIdentity>());
        databaseQuery = GetEntityQuery(ComponentType.ReadOnly<FactoryDatabase>());
    }

    protected override void OnUpdate()
    {
        InventoryBufferReadsLastUpdate = 0;
        BuildingBufferReadsLastUpdate = 0;
        if (!UiRuntimeServices.TryGet(World, out UiDataHub hub))
            return;

        ExportInventories(hub);
        ExportBuildings(hub);
        ExportBuildCatalog(hub);
    }

    private void ExportInventories(UiDataHub hub)
    {
        if (hub.Observations.PlayerCount == 0)
            return;

        List<PlayerId> observed = new(hub.Observations.Players);
        Dictionary<PlayerId, Entity> entities = new();
        using NativeArray<Entity> playerEntities =
            playerQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<PlayerIdentity> identities =
            playerQuery.ToComponentDataArray<PlayerIdentity>(Allocator.Temp);
        for (int i = 0; i < identities.Length; i++)
        {
            if (observed.Contains(identities[i].Value))
                entities[identities[i].Value] = playerEntities[i];
        }

        for (int i = 0; i < observed.Count; i++)
        {
            PlayerId playerId = observed[i];
            if (!entities.TryGetValue(playerId, out Entity entity))
            {
                hub.Publish(playerId, new InventorySnapshot(
                    playerId, 0, Array.Empty<ItemSlotSnapshot>(), false));
                inventoryCache.Remove(playerId);
                continue;
            }

            PlayerInventory inventory = EntityManager.GetComponentData<PlayerInventory>(entity);
            if (inventoryCache.TryGetValue(playerId, out InventoryCache cached) &&
                cached.Revision == inventory.Revision)
            {
                hub.Publish(playerId, cached.Snapshot);
                continue;
            }

            DynamicBuffer<InventorySlot> slots =
                EntityManager.GetBuffer<InventorySlot>(entity, true);
            InventoryBufferReadsLastUpdate++;
            ItemSlotSnapshot[] exported = new ItemSlotSnapshot[slots.Length];
            for (int slotIndex = 0; slotIndex < slots.Length; slotIndex++)
            {
                InventorySlot slot = slots[slotIndex];
                exported[slotIndex] = new ItemSlotSnapshot(
                    (ushort)slotIndex,
                    slot.ItemType,
                    slot.Count,
                    GetItemCapacity(slot.ItemType),
                    default,
                    UiSlotAccess.InsertAndExtract);
            }

            InventorySnapshot snapshot = new(
                playerId, inventory.Revision, exported);
            inventoryCache[playerId] = new InventoryCache(inventory.Revision, snapshot);
            hub.Publish(playerId, snapshot);
        }
    }

    private void ExportBuildings(UiDataHub hub)
    {
        if (hub.Observations.BuildingCount == 0)
            return;

        List<BuildingRuntimeId> observed = new(hub.Observations.Buildings);
        Dictionary<BuildingRuntimeId, Entity> entities = new();
        using NativeArray<Entity> buildingEntities =
            buildingQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<ItemContainerIdentity> identities =
            buildingQuery.ToComponentDataArray<ItemContainerIdentity>(Allocator.Temp);
        for (int i = 0; i < identities.Length; i++)
        {
            BuildingRuntimeId id = new(identities[i].RuntimeId);
            if (observed.Contains(id))
                entities[id] = buildingEntities[i];
        }

        for (int i = 0; i < observed.Count; i++)
        {
            BuildingRuntimeId id = observed[i];
            if (!entities.TryGetValue(id, out Entity entity))
            {
                hub.Publish(id, new BuildingSnapshot
                {
                    RuntimeId = id,
                    IsAvailable = false
                });
                buildingCache.Remove(id);
                continue;
            }

            ExportBuilding(hub, id, entity);
        }
    }

    private void ExportBuilding(UiDataHub hub, BuildingRuntimeId id, Entity entity)
    {
        BuildingIdentity identity = EntityManager.GetComponentData<BuildingIdentity>(entity);
        GridPlacement placement = EntityManager.GetComponentData<GridPlacement>(entity);
        BuildingSnapshot snapshot = new()
        {
            RuntimeId = id,
            BuildingLevelId = identity.BuildingLevel,
            Kind = placement.Kind,
            GridCell = placement.AnchorCell,
            IsAvailable = true
        };

        if (EntityManager.HasComponent<PlayerInventory>(entity))
        {
            hub.Publish(id, snapshot);
            return;
        }

        if (EntityManager.HasComponent<StorageState>(entity) &&
            EntityManager.HasBuffer<InventorySlot>(entity))
        {
            StorageState state = EntityManager.GetComponentData<StorageState>(entity);
            snapshot.Revision = state.Revision;
            if (buildingCache.TryGetValue(id, out BuildingCache cached) &&
                cached.Revision == state.Revision)
            {
                snapshot.StorageSlots = cached.Slots;
            }
            else
            {
                DynamicBuffer<InventorySlot> slots =
                    EntityManager.GetBuffer<InventorySlot>(entity, true);
                BuildingBufferReadsLastUpdate++;
                snapshot.StorageSlots = ExportInventorySlots(slots);
                buildingCache[id] = new BuildingCache(state.Revision, snapshot.StorageSlots);
            }
        }
        else if (EntityManager.HasComponent<ItemProcessState>(entity) &&
                 EntityManager.HasComponent<ItemProcessor>(entity) &&
                 EntityManager.HasBuffer<ProcessorItemSlot>(entity))
        {
            ItemProcessState state = EntityManager.GetComponentData<ItemProcessState>(entity);
            ItemProcessor processor = EntityManager.GetComponentData<ItemProcessor>(entity);
            snapshot.Revision = state.InventoryRevision;
            ItemSlotSnapshot[] inputs;
            ItemSlotSnapshot[] outputs;
            if (buildingCache.TryGetValue(id, out BuildingCache cached) &&
                cached.Revision == state.InventoryRevision)
            {
                inputs = cached.Inputs;
                outputs = cached.Outputs;
            }
            else
            {
                ItemSlotSnapshot[] slots = ExportProcessorSlots(
                    EntityManager.GetBuffer<ProcessorItemSlot>(entity, true));
                BuildingBufferReadsLastUpdate++;
                SplitProcessorSlots(slots, out inputs, out outputs);
                buildingCache[id] = new BuildingCache(
                    state.InventoryRevision, null, inputs, outputs);
            }
            snapshot.Processor = new ProcessorSnapshot
            {
                SelectedRecipeId = GetSelectedRecipe(state.SelectedRecipeIndex),
                Status = state.Status,
                Progress01 = state.DurationTicks <= 0
                    ? 0f
                    : math.saturate((float)state.ElapsedTicks / state.DurationTicks),
                InventoryRevision = state.InventoryRevision,
                Inputs = inputs,
                Outputs = outputs,
                AvailableRecipes = GetAvailableRecipes(processor.MachineType)
            };
        }

        hub.Publish(id, snapshot);
    }

    private void ExportBuildCatalog(UiDataHub hub)
    {
        if (!hub.Observations.ObserveBuildCatalog || databaseQuery.CalculateEntityCount() != 1)
            return;
        BlobAssetReference<FactoryDatabaseBlob> reference =
            databaseQuery.GetSingleton<FactoryDatabase>().Value;
        ref FactoryDatabaseBlob database = ref reference.Value;
        BuildingLevelId[] levels = new BuildingLevelId[database.BuildingLevelMenu.Length];
        for (int i = 0; i < levels.Length; i++)
            levels[i] = database.BuildingLevelMenu[i];
        hub.PublishBuildCatalog(new BuildCatalogSnapshot(levels));
    }

    private ushort GetItemCapacity(ItemId itemId)
    {
        if (databaseQuery.CalculateEntityCount() != 1)
            return 0;
        BlobAssetReference<FactoryDatabaseBlob> reference =
            databaseQuery.GetSingleton<FactoryDatabase>().Value;
        ref FactoryDatabaseBlob database = ref reference.Value;
        return FactoryDatabaseUtility.IsValidItem(ref database, itemId)
            ? database.ItemsById[itemId.Value].MaxStack
            : (ushort)0;
    }

    private RecipeId GetSelectedRecipe(int index)
    {
        if (index < 0 || databaseQuery.CalculateEntityCount() != 1)
            return default;
        BlobAssetReference<FactoryDatabaseBlob> reference =
            databaseQuery.GetSingleton<FactoryDatabase>().Value;
        ref FactoryDatabaseBlob database = ref reference.Value;
        return index < database.Recipes.Length ? database.Recipes[index].Id : default;
    }

    private RecipeId[] GetAvailableRecipes(MachineTypeId machineType)
    {
        if (databaseQuery.CalculateEntityCount() != 1)
            return Array.Empty<RecipeId>();
        BlobAssetReference<FactoryDatabaseBlob> reference =
            databaseQuery.GetSingleton<FactoryDatabase>().Value;
        ref FactoryDatabaseBlob database = ref reference.Value;
        FactoryRecipeRangeBlob range = FactoryDatabaseUtility.GetRecipeRange(ref database, machineType);
        RecipeId[] result = new RecipeId[range.Count];
        for (int i = 0; i < result.Length; i++)
            result[i] = database.Recipes[range.Start + i].Id;
        return result;
    }

    private ItemSlotSnapshot[] ExportInventorySlots(DynamicBuffer<InventorySlot> slots)
    {
        ItemSlotSnapshot[] result = new ItemSlotSnapshot[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            InventorySlot slot = slots[i];
            result[i] = new ItemSlotSnapshot(
                (ushort)i, slot.ItemType, slot.Count, GetItemCapacity(slot.ItemType), default,
                UiSlotAccess.InsertAndExtract);
        }
        return result;
    }

    private static ItemSlotSnapshot[] ExportProcessorSlots(DynamicBuffer<ProcessorItemSlot> slots)
    {
        ItemSlotSnapshot[] result = new ItemSlotSnapshot[slots.Length];
        for (int i = 0; i < slots.Length; i++)
        {
            ProcessorItemSlot slot = slots[i];
            result[i] = new ItemSlotSnapshot(
                (ushort)i,
                slot.Count == 0 ? default : slot.AcceptedItemType,
                slot.Count,
                slot.Capacity,
                slot.AcceptedItemType,
                slot.Kind == ProcessorSlotKind.Input
                    ? UiSlotAccess.Insert
                    : UiSlotAccess.Extract);
        }
        return result;
    }

    private static void SplitProcessorSlots(
        ItemSlotSnapshot[] slots,
        out ItemSlotSnapshot[] inputs,
        out ItemSlotSnapshot[] outputs)
    {
        int inputCount = 0;
        for (int i = 0; i < slots.Length; i++)
            if (slots[i].Access == UiSlotAccess.Insert)
                inputCount++;
        inputs = new ItemSlotSnapshot[inputCount];
        outputs = new ItemSlotSnapshot[slots.Length - inputCount];
        int inputIndex = 0;
        int outputIndex = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].Access == UiSlotAccess.Insert)
                inputs[inputIndex] = WithLogicalIndex(slots[i], (ushort)inputIndex++);
            else
                outputs[outputIndex] = WithLogicalIndex(slots[i], (ushort)outputIndex++);
        }
    }

    private static ItemSlotSnapshot WithLogicalIndex(ItemSlotSnapshot value, ushort index) =>
        new(index, value.ItemId, value.Count, value.Capacity,
            value.AcceptedItemId, value.Access);

    private sealed class InventoryCache
    {
        public InventoryCache(uint revision, InventorySnapshot snapshot)
        {
            Revision = revision;
            Snapshot = snapshot;
        }
        public uint Revision { get; }
        public InventorySnapshot Snapshot { get; }
    }

    private sealed class BuildingCache
    {
        public BuildingCache(
            uint revision,
            ItemSlotSnapshot[] slots,
            ItemSlotSnapshot[] inputs = null,
            ItemSlotSnapshot[] outputs = null)
        {
            Revision = revision;
            Slots = slots;
            Inputs = inputs;
            Outputs = outputs;
        }
        public uint Revision { get; }
        public ItemSlotSnapshot[] Slots { get; }
        public ItemSlotSnapshot[] Inputs { get; }
        public ItemSlotSnapshot[] Outputs { get; }
    }
}
