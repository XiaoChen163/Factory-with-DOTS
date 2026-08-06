using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(BeltProgressSystem))]
[UpdateBefore(typeof(ItemPortBufferSwapSystem))]
public partial class BeltTransferSystem : SystemBase
{
    private FactoryLinearTransferResolver transferResolver;
    private EntityQuery beltTopologyQuery;
    private EntityQuery mergerQuery;
    private EntityQuery splitterQuery;
    private EntityQuery inputPortQuery;
    private EntityQuery outputPortQuery;
    private EntityQuery itemCatalogQuery;
    private EntityQuery gridQuery;
    private TransferCommandBufferSystem transferEcbSystem;
    private Entity statsEntity;
    private Entity itemCatalog = Entity.Null;
    private NativeArray<Entity> inputPortOwners;
    private NativeArray<Entity> outputPortOwners;
    private NativeArray<BeltState> emptyBeltStates;
    private NativeArray<Merger> emptyMergers;
    private NativeArray<Splitter> emptySplitters;
    private uint cachedGridRevision;
    private bool hasCachedGridRevision;

    public int TransportTopologyRebuildCount =>
        transferResolver?.TopologyRebuildCount ?? 0;

    protected override void OnCreate()
    {
        transferResolver = new FactoryLinearTransferResolver();
        emptyBeltStates =
            new NativeArray<BeltState>(0, Allocator.Persistent);
        emptyMergers = new NativeArray<Merger>(0, Allocator.Persistent);
        emptySplitters =
            new NativeArray<Splitter>(0, Allocator.Persistent);
        beltTopologyQuery = GetEntityQuery(
            ComponentType.ReadOnly<BeltTopology>());
        mergerQuery = GetEntityQuery(
            ComponentType.ReadOnly<Merger>());
        splitterQuery = GetEntityQuery(
            ComponentType.ReadOnly<Splitter>());
        inputPortQuery = GetEntityQuery(
            ComponentType.ReadOnly<GridPlacement>(),
            ComponentType.ReadOnly<BuildingPort>(),
            ComponentType.ReadOnly<ItemInputPortCurrent>(),
            ComponentType.ReadWrite<ItemTransferReceiptNext>());
        outputPortQuery = GetEntityQuery(
            ComponentType.ReadOnly<GridPlacement>(),
            ComponentType.ReadOnly<BuildingPort>(),
            ComponentType.ReadOnly<ItemOutputPortCurrent>(),
            ComponentType.ReadWrite<ItemTransferReceiptNext>());
        itemCatalogQuery = GetEntityQuery(
            ComponentType.ReadOnly<BuildingPrefabCatalog>(),
            ComponentType.ReadOnly<ItemPrefabEntry>());
        gridQuery = GetEntityQuery(
            ComponentType.ReadOnly<GridDefinition>());
        transferEcbSystem =
            World.GetOrCreateSystemManaged<TransferCommandBufferSystem>();
        statsEntity =
            EntityManager.CreateEntity(typeof(Stage3SimulationStats));
        RequireForUpdate<Stage3SimulationStats>();
    }

    protected override void OnDestroy()
    {
        Dependency.Complete();
        if (emptyBeltStates.IsCreated)
        {
            emptyBeltStates.Dispose();
        }
        if (emptyMergers.IsCreated)
        {
            emptyMergers.Dispose();
        }
        if (emptySplitters.IsCreated)
        {
            emptySplitters.Dispose();
        }
        if (inputPortOwners.IsCreated)
        {
            inputPortOwners.Dispose();
        }
        if (outputPortOwners.IsCreated)
        {
            outputPortOwners.Dispose();
        }
        transferResolver?.Dispose();
        transferResolver = null;
    }

    protected override void OnUpdate()
    {
        bool hasGrid = gridQuery.CalculateEntityCount() == 1;
        uint gridRevision = hasGrid
            ? gridQuery.GetSingleton<GridDefinition>().Revision
            : 0;
        if (!hasCachedGridRevision || gridRevision != cachedGridRevision)
        {
            Dependency.Complete();
            RefreshTopology(gridRevision, hasGrid);
            RefreshPortOwnerCache();
            hasCachedGridRevision = true;
            cachedGridRevision = gridRevision;
        }

        RefreshItemCatalog();

        EntityCommandBuffer ecb = transferEcbSystem.CreateCommandBuffer();
        FactoryTransferArbitrationJob job =
            transferResolver.CreateArbitrationJob();
        job.EnableInterface = 1;
        job.BeltStateLookup = GetComponentLookup<BeltState>(false);
        job.MergerLookup = GetComponentLookup<Merger>(false);
        job.SplitterLookup = GetComponentLookup<Splitter>(false);
        job.GridPlacementLookup = GetComponentLookup<GridPlacement>(true);
        job.ItemLookup = GetComponentLookup<Item>(true);
        job.TransformLookup = GetComponentLookup<LocalTransform>(true);
        job.BuildingPortLookup = GetBufferLookup<BuildingPort>(true);
        job.InputPortCurrentLookup =
            GetBufferLookup<ItemInputPortCurrent>(true);
        job.OutputPortCurrentLookup =
            GetBufferLookup<ItemOutputPortCurrent>(true);
        job.ReceiptNextLookup =
            GetBufferLookup<ItemTransferReceiptNext>(false);
        job.ItemPrefabLookup = GetBufferLookup<ItemPrefabEntry>(true);
        job.StatsLookup =
            GetComponentLookup<Stage3SimulationStats>(false);
        job.InputPortOwners = inputPortOwners;
        job.OutputPortOwners = outputPortOwners;
        job.BeltStates = emptyBeltStates;
        job.Mergers = emptyMergers;
        job.Splitters = emptySplitters;
        job.ItemCatalog = itemCatalog;
        job.StatsEntity = statsEntity;
        job.Ecb = ecb;

        Dependency = job.Schedule(Dependency);
        transferEcbSystem.AddJobHandleForProducer(Dependency);
    }

    private void RefreshTopology(uint gridRevision, bool hasGrid)
    {
        uint revision = hasGrid ? gridRevision : 0;
        using NativeArray<Entity> beltEntities =
            beltTopologyQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<BeltTopology> beltTopologies =
            beltTopologyQuery.ToComponentDataArray<BeltTopology>(
                Allocator.Temp);
        using NativeArray<Entity> mergerEntities =
            mergerQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<Merger> mergers =
            mergerQuery.ToComponentDataArray<Merger>(Allocator.Temp);
        using NativeArray<Entity> splitterEntities =
            splitterQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<Splitter> splitters =
            splitterQuery.ToComponentDataArray<Splitter>(Allocator.Temp);
        transferResolver.EnsureTopology(
            revision,
            beltEntities,
            beltTopologies,
            mergerEntities,
            mergers,
            splitterEntities,
            splitters,
            !hasGrid);
    }

    private void RefreshPortOwnerCache()
    {
        if (inputPortOwners.IsCreated)
        {
            inputPortOwners.Dispose();
        }
        if (outputPortOwners.IsCreated)
        {
            outputPortOwners.Dispose();
        }
        inputPortOwners = default;
        outputPortOwners = default;
        if (gridQuery.CalculateEntityCount() != 1)
        {
            inputPortOwners =
                new NativeArray<Entity>(0, Allocator.Persistent);
            outputPortOwners =
                new NativeArray<Entity>(0, Allocator.Persistent);
            return;
        }

        using NativeArray<Entity> inputSnapshot =
            inputPortQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<Entity> outputSnapshot =
            outputPortQuery.ToEntityArray(Allocator.Temp);
        inputPortOwners = SortPortOwners(inputSnapshot);
        outputPortOwners = SortPortOwners(outputSnapshot);
    }

    private void RefreshItemCatalog()
    {
        itemCatalog = itemCatalogQuery.CalculateEntityCount() == 1
            ? itemCatalogQuery.GetSingletonEntity()
            : Entity.Null;
    }

    private NativeArray<Entity> SortPortOwners(
        NativeArray<Entity> owners)
    {
        Entity[] managed = owners.ToArray();
        Array.Sort(managed, (left, right) =>
        {
            GridPlacement leftPlacement =
                EntityManager.GetComponentData<GridPlacement>(left);
            GridPlacement rightPlacement =
                EntityManager.GetComponentData<GridPlacement>(right);
            int x = leftPlacement.AnchorCell.x.CompareTo(
                rightPlacement.AnchorCell.x);
            if (x != 0)
            {
                return x;
            }

            int y = leftPlacement.AnchorCell.y.CompareTo(
                rightPlacement.AnchorCell.y);
            return y != 0 ? y : left.Index.CompareTo(right.Index);
        });

        NativeArray<Entity> result = new NativeArray<Entity>(
            managed.Length,
            Allocator.Persistent);
        for (int i = 0; i < managed.Length; i++)
        {
            result[i] = managed[i];
        }

        return result;
    }
}
