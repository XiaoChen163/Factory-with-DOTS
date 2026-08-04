using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(BeltProgressSystem))]
[UpdateBefore(typeof(ItemPortBufferSwapSystem))]
[UpdateBefore(typeof(BeltItemPositionSystem))]
public partial class BeltTransferSystem : SystemBase
{
    private enum TransportKind : byte
    {
        Belt,
        Merger,
        Splitter
    }

    private readonly struct TransportIndex
    {
        public TransportIndex(TransportKind kind, int index)
        {
            Kind = kind;
            Index = index;
        }

        public TransportKind Kind { get; }
        public int Index { get; }
    }

    private readonly struct PortKey : IEquatable<PortKey>
    {
        public PortKey(Entity owner, byte portIndex)
        {
            Owner = owner;
            PortIndex = portIndex;
        }

        public Entity Owner { get; }
        public byte PortIndex { get; }

        public bool Equals(PortKey other)
        {
            return Owner == other.Owner && PortIndex == other.PortIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is PortKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Owner.GetHashCode() * 397) ^ PortIndex;
        }
    }

    private readonly HashSet<Entity> processedJunctions =
        new HashSet<Entity>();
    private readonly Dictionary<PortKey, ulong> acceptedInputs =
        new Dictionary<PortKey, ulong>();
    private readonly Dictionary<PortKey, ulong> acceptedOutputs =
        new Dictionary<PortKey, ulong>();

    private EntityQuery beltQuery;
    private EntityQuery mergerQuery;
    private EntityQuery splitterQuery;
    private EntityQuery inputPortQuery;
    private EntityQuery outputPortQuery;
    private EntityQuery itemCatalogQuery;
    private Entity statsEntity;

    protected override void OnCreate()
    {
        beltQuery = GetEntityQuery(ComponentType.ReadWrite<Belt>());
        mergerQuery = GetEntityQuery(ComponentType.ReadWrite<Merger>());
        splitterQuery = GetEntityQuery(ComponentType.ReadWrite<Splitter>());
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
        statsEntity = EntityManager.CreateEntity(typeof(Stage3SimulationStats));
        RequireForUpdate<Stage3SimulationStats>();
    }

    protected override void OnUpdate()
    {
        using NativeArray<Entity> beltEntitySnapshot =
            beltQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<Belt> beltComponentSnapshot =
            beltQuery.ToComponentDataArray<Belt>(Allocator.Temp);
        using NativeArray<Entity> mergerEntitySnapshot =
            mergerQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<Merger> mergerComponentSnapshot =
            mergerQuery.ToComponentDataArray<Merger>(Allocator.Temp);
        using NativeArray<Entity> splitterEntitySnapshot =
            splitterQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<Splitter> splitterComponentSnapshot =
            splitterQuery.ToComponentDataArray<Splitter>(Allocator.Temp);

        Entity[] beltEntities = beltEntitySnapshot.ToArray();
        Belt[] belts = beltComponentSnapshot.ToArray();
        Entity[] mergerEntities = mergerEntitySnapshot.ToArray();
        Merger[] mergers = mergerComponentSnapshot.ToArray();
        Entity[] splitterEntities = splitterEntitySnapshot.ToArray();
        Splitter[] splitters = splitterComponentSnapshot.ToArray();

        Dictionary<int2, TransportIndex> transportByCell =
            BuildTransportIndex(belts, mergers, splitters);
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.Temp);

        processedJunctions.Clear();
        int interfaceRequestCount = 0;
        int interfaceAcceptedCount = ConsumeBuildingInputs(
            transportByCell,
            mergerEntities,
            splitterEntities,
            belts,
            mergers,
            splitters,
            ref ecb,
            ref interfaceRequestCount);

        int loopCount = 0;
        int readyRequestCount = 0;
        int acceptedTransferCount = 0;
        int passLimit = mergers.Length + splitters.Length + 1;
        for (int pass = 0; pass < passLimit; pass++)
        {
            FactoryTransferResolver.Resolve(
                beltEntities,
                belts,
                mergerEntities,
                mergers,
                splitterEntities,
                splitters,
                processedJunctions,
                out int passLoopCount,
                out int passReadyRequestCount,
                out int passAcceptedTransferCount);

            loopCount = passLoopCount;
            readyRequestCount += passReadyRequestCount;
            acceptedTransferCount += passAcceptedTransferCount;
            if (passAcceptedTransferCount == 0)
            {
                break;
            }
        }

        WriteTransportSnapshots(
            beltEntities,
            belts,
            mergerEntities,
            mergers,
            splitterEntities,
            splitters);

        interfaceAcceptedCount += InjectBuildingOutputs(
            transportByCell,
            beltEntities,
            mergerEntities,
            splitterEntities,
            ref ecb,
            ref interfaceRequestCount);

        ecb.Playback(EntityManager);
        ecb.Dispose();

        Stage3SimulationStats stats =
            EntityManager.GetComponentData<Stage3SimulationStats>(statsEntity);
        stats.BeltCount = belts.Length;
        stats.MergerCount = mergers.Length;
        stats.SplitterCount = splitters.Length;
        stats.LoopCount = loopCount;
        stats.ReadyRequestCount =
            readyRequestCount + interfaceRequestCount;
        stats.AcceptedTransferCount =
            acceptedTransferCount + interfaceAcceptedCount;
        stats.TickCount++;
        stats.TotalReadyRequestCount += (ulong)stats.ReadyRequestCount;
        stats.TotalAcceptedTransferCount +=
            (ulong)stats.AcceptedTransferCount;
        EntityManager.SetComponentData(statsEntity, stats);
    }

    private int ConsumeBuildingInputs(
        Dictionary<int2, TransportIndex> transportByCell,
        Entity[] mergerEntities,
        Entity[] splitterEntities,
        Belt[] belts,
        Merger[] mergers,
        Splitter[] splitters,
        ref EntityCommandBuffer ecb,
        ref int requestCount)
    {
        using NativeArray<Entity> snapshot =
            inputPortQuery.ToEntityArray(Allocator.Temp);
        Entity[] owners = snapshot.ToArray();
        SortPortOwners(owners);
        int acceptedCount = 0;

        for (int ownerIndex = 0; ownerIndex < owners.Length; ownerIndex++)
        {
            Entity owner = owners[ownerIndex];
            GridPlacement placement =
                EntityManager.GetComponentData<GridPlacement>(owner);
            DynamicBuffer<BuildingPort> buildingPorts =
                EntityManager.GetBuffer<BuildingPort>(owner, true);
            DynamicBuffer<ItemInputPortCurrent> ports =
                EntityManager.GetBuffer<ItemInputPortCurrent>(owner, true);
            DynamicBuffer<ItemTransferReceiptNext> receipts =
                EntityManager.GetBuffer<ItemTransferReceiptNext>(owner);

            for (int i = 0; i < ports.Length; i++)
            {
                ItemInputPortSnapshot port = ports[i].Value;
                if (port.Enabled == 0)
                {
                    continue;
                }

                PortKey key = new PortKey(owner, port.PortIndex);
                int availableCapacity = GetEffectiveCount(
                    acceptedInputs,
                    key,
                    port.AppliedTransferCount,
                    port.FreeCapacity);
                if (availableCapacity <= 0 ||
                    !TryGetBuildingPort(
                        buildingPorts,
                        BuildingPortType.Input,
                        port.PortIndex,
                        out BuildingPort geometry))
                {
                    continue;
                }

                int2 sourceCell = EcsGridUtility.GetBuildingCell(
                    placement,
                    geometry.CellOffset);
                int2 direction = EcsGridUtility.Rotate(
                    geometry.Direction,
                    placement.QuarterTurns);
                if (!transportByCell.TryGetValue(
                        sourceCell,
                        out TransportIndex source) ||
                    !TryGetReadyItem(
                        source,
                        direction,
                        belts,
                        mergers,
                        splitters,
                        out Entity itemEntity,
                        out int sourceOutputIndex))
                {
                    continue;
                }

                requestCount++;
                if (!EntityManager.Exists(itemEntity) ||
                    !EntityManager.HasComponent<Item>(itemEntity))
                {
                    continue;
                }

                ItemId itemType =
                    EntityManager.GetComponentData<Item>(itemEntity).ItemType;
                if (port.FilterMode == ItemPortFilterMode.ExactItemType &&
                    port.AcceptedItemType != itemType)
                {
                    continue;
                }

                ClearTransportItem(
                    source,
                    sourceOutputIndex,
                    belts,
                    mergers,
                    splitters);
                if (source.Kind == TransportKind.Merger)
                {
                    processedJunctions.Add(
                        mergerEntities[source.Index]);
                }
                else if (source.Kind == TransportKind.Splitter)
                {
                    processedJunctions.Add(
                        splitterEntities[source.Index]);
                }
                receipts.Add(new ItemTransferReceiptNext
                {
                    Value = new ItemTransferReceipt
                    {
                        ItemType = itemType,
                        Count = 1,
                        PortIndex = port.PortIndex,
                        Kind = ItemTransferReceiptKind.InputAccepted
                    }
                });
                acceptedInputs[key] =
                    GetAcceptedCount(
                        acceptedInputs,
                        key,
                        port.AppliedTransferCount) + 1;
                ecb.DestroyEntity(itemEntity);
                acceptedCount++;
            }
        }

        return acceptedCount;
    }

    private int InjectBuildingOutputs(
        Dictionary<int2, TransportIndex> transportByCell,
        Entity[] beltEntities,
        Entity[] mergerEntities,
        Entity[] splitterEntities,
        ref EntityCommandBuffer ecb,
        ref int requestCount)
    {
        Dictionary<ItemId, Entity> prefabsByType = BuildItemPrefabIndex();
        using NativeArray<Entity> snapshot =
            outputPortQuery.ToEntityArray(Allocator.Temp);
        Entity[] owners = snapshot.ToArray();
        SortPortOwners(owners);
        HashSet<Entity> reservedTargets = new HashSet<Entity>();
        int acceptedCount = 0;

        for (int ownerIndex = 0; ownerIndex < owners.Length; ownerIndex++)
        {
            Entity owner = owners[ownerIndex];
            GridPlacement placement =
                EntityManager.GetComponentData<GridPlacement>(owner);
            DynamicBuffer<BuildingPort> buildingPorts =
                EntityManager.GetBuffer<BuildingPort>(owner, true);
            DynamicBuffer<ItemOutputPortCurrent> ports =
                EntityManager.GetBuffer<ItemOutputPortCurrent>(owner, true);
            DynamicBuffer<ItemTransferReceiptNext> receipts =
                EntityManager.GetBuffer<ItemTransferReceiptNext>(owner);

            for (int i = 0; i < ports.Length; i++)
            {
                ItemOutputPortSnapshot port = ports[i].Value;
                if (port.Enabled == 0 || !port.ItemType.IsValid)
                {
                    continue;
                }

                PortKey key = new PortKey(owner, port.PortIndex);
                int availableCount = GetEffectiveCount(
                    acceptedOutputs,
                    key,
                    port.AppliedTransferCount,
                    port.AvailableCount);
                if (availableCount <= 0 ||
                    !TryGetBuildingPort(
                        buildingPorts,
                        BuildingPortType.Output,
                        port.PortIndex,
                        out BuildingPort geometry))
                {
                    continue;
                }

                int2 targetCell = EcsGridUtility.GetBuildingCell(
                    placement,
                    geometry.CellOffset);
                int2 direction = EcsGridUtility.Rotate(
                    geometry.Direction,
                    placement.QuarterTurns);
                if (!transportByCell.TryGetValue(
                        targetCell,
                        out TransportIndex target))
                {
                    continue;
                }

                Entity targetEntity = GetTransportEntity(
                    target,
                    beltEntities,
                    mergerEntities,
                    splitterEntities);
                if (reservedTargets.Contains(targetEntity) ||
                    !CanInjectIntoTransport(
                        target,
                        direction,
                        beltEntities,
                        mergerEntities,
                        splitterEntities))
                {
                    continue;
                }

                requestCount++;
                if (!TryGetItemPrefab(
                        port.ItemType,
                        prefabsByType,
                        out Entity prefab))
                {
                    continue;
                }

                reservedTargets.Add(targetEntity);

                Entity item = ecb.Instantiate(prefab);
                float3 position = new float3(
                    targetCell.x + 0.5f,
                    0.535f,
                    targetCell.y + 0.5f);
                ecb.AddComponent(item, new Item
                {
                    ItemType = port.ItemType,
                    Position = position
                });
                if (EntityManager.HasComponent<LocalTransform>(prefab))
                {
                    LocalTransform transform =
                        EntityManager.GetComponentData<LocalTransform>(prefab);
                    transform.Position = position;
                    ecb.SetComponent(item, transform);
                }

                SetTransportItem(
                    target,
                    item,
                    beltEntities,
                    mergerEntities,
                    splitterEntities,
                    ref ecb);
                receipts.Add(new ItemTransferReceiptNext
                {
                    Value = new ItemTransferReceipt
                    {
                        ItemType = port.ItemType,
                        Count = 1,
                        PortIndex = port.PortIndex,
                        Kind = ItemTransferReceiptKind.OutputTransferred
                    }
                });
                acceptedOutputs[key] =
                    GetAcceptedCount(
                        acceptedOutputs,
                        key,
                        port.AppliedTransferCount) + 1;
                acceptedCount++;
            }
        }

        return acceptedCount;
    }

    private static Entity GetTransportEntity(
        TransportIndex target,
        Entity[] beltEntities,
        Entity[] mergerEntities,
        Entity[] splitterEntities)
    {
        switch (target.Kind)
        {
            case TransportKind.Belt:
                return beltEntities[target.Index];
            case TransportKind.Merger:
                return mergerEntities[target.Index];
            case TransportKind.Splitter:
                return splitterEntities[target.Index];
            default:
                return Entity.Null;
        }
    }

    private bool TryGetItemPrefab(
        ItemId itemType,
        Dictionary<ItemId, Entity> prefabsByType,
        out Entity prefab)
    {
        if (prefabsByType.TryGetValue(itemType, out prefab) &&
            prefab != Entity.Null && EntityManager.Exists(prefab) &&
            EntityManager.HasComponent<Prefab>(prefab))
        {
            return true;
        }

        prefab = Entity.Null;
        return false;
    }

    private Dictionary<ItemId, Entity> BuildItemPrefabIndex()
    {
        Dictionary<ItemId, Entity> result =
            new Dictionary<ItemId, Entity>();
        if (itemCatalogQuery.CalculateEntityCount() != 1)
        {
            return result;
        }

        Entity catalog = itemCatalogQuery.GetSingletonEntity();
        DynamicBuffer<ItemPrefabEntry> entries =
            EntityManager.GetBuffer<ItemPrefabEntry>(catalog, true);
        for (int i = 0; i < entries.Length; i++)
        {
            ItemPrefabEntry entry = entries[i];
            if (entry.ItemType.IsValid && entry.Prefab != Entity.Null)
            {
                result[entry.ItemType] = entry.Prefab;
            }
        }

        return result;
    }

    private static Dictionary<int2, TransportIndex> BuildTransportIndex(
        Belt[] belts,
        Merger[] mergers,
        Splitter[] splitters)
    {
        Dictionary<int2, TransportIndex> result =
            new Dictionary<int2, TransportIndex>(
                belts.Length + mergers.Length + splitters.Length);
        for (int i = 0; i < belts.Length; i++)
        {
            result[belts[i].Cell] =
                new TransportIndex(TransportKind.Belt, i);
        }
        for (int i = 0; i < mergers.Length; i++)
        {
            result[mergers[i].Cell] =
                new TransportIndex(TransportKind.Merger, i);
        }
        for (int i = 0; i < splitters.Length; i++)
        {
            result[splitters[i].Cell] =
                new TransportIndex(TransportKind.Splitter, i);
        }

        return result;
    }

    private static bool TryGetReadyItem(
        TransportIndex source,
        int2 expectedDirection,
        Belt[] belts,
        Merger[] mergers,
        Splitter[] splitters,
        out Entity item,
        out int outputIndex)
    {
        switch (source.Kind)
        {
            case TransportKind.Belt:
                Belt belt = belts[source.Index];
                item = belt.CurrentItem;
                outputIndex = 0;
                return item != Entity.Null &&
                       belt.Progress >= 1f &&
                       math.all(belt.Direction == expectedDirection);
            case TransportKind.Merger:
                Merger merger = mergers[source.Index];
                item = merger.CurrentItem;
                outputIndex = 0;
                return item != Entity.Null &&
                       math.all(merger.Direction == expectedDirection);
            case TransportKind.Splitter:
                Splitter splitter = splitters[source.Index];
                item = splitter.CurrentItem;
                outputIndex = GetSplitterOutputIndex(
                    splitter.Direction,
                    expectedDirection);
                return item != Entity.Null &&
                       outputIndex >= 0;
            default:
                item = Entity.Null;
                outputIndex = -1;
                return false;
        }
    }

    private static void ClearTransportItem(
        TransportIndex source,
        int outputIndex,
        Belt[] belts,
        Merger[] mergers,
        Splitter[] splitters)
    {
        switch (source.Kind)
        {
            case TransportKind.Belt:
                Belt belt = belts[source.Index];
                belt.CurrentItem = Entity.Null;
                belt.Progress = 0f;
                belts[source.Index] = belt;
                break;
            case TransportKind.Merger:
                Merger merger = mergers[source.Index];
                merger.CurrentItem = Entity.Null;
                mergers[source.Index] = merger;
                break;
            case TransportKind.Splitter:
                Splitter splitter = splitters[source.Index];
                splitter.CurrentItem = Entity.Null;
                splitter.NextOutputIndex =
                    WrapThree(outputIndex + 1);
                splitters[source.Index] = splitter;
                break;
        }
    }

    private bool CanInjectIntoTransport(
        TransportIndex target,
        int2 direction,
        Entity[] beltEntities,
        Entity[] mergerEntities,
        Entity[] splitterEntities)
    {
        switch (target.Kind)
        {
            case TransportKind.Belt:
                return EntityManager.GetComponentData<Belt>(
                    beltEntities[target.Index]).CurrentItem == Entity.Null;
            case TransportKind.Merger:
                Merger merger = EntityManager.GetComponentData<Merger>(
                    mergerEntities[target.Index]);
                return merger.CurrentItem == Entity.Null &&
                       GetMergerInputIndex(merger.Direction, direction) >= 0;
            case TransportKind.Splitter:
                Splitter splitter =
                    EntityManager.GetComponentData<Splitter>(
                        splitterEntities[target.Index]);
                return splitter.CurrentItem == Entity.Null &&
                       math.all(direction == splitter.Direction);
            default:
                return false;
        }
    }

    private void SetTransportItem(
        TransportIndex target,
        Entity item,
        Entity[] beltEntities,
        Entity[] mergerEntities,
        Entity[] splitterEntities,
        ref EntityCommandBuffer ecb)
    {
        switch (target.Kind)
        {
            case TransportKind.Belt:
                Belt belt = EntityManager.GetComponentData<Belt>(
                    beltEntities[target.Index]);
                belt.CurrentItem = item;
                belt.Progress = 0f;
                ecb.SetComponent(beltEntities[target.Index], belt);
                break;
            case TransportKind.Merger:
                Merger merger = EntityManager.GetComponentData<Merger>(
                    mergerEntities[target.Index]);
                merger.CurrentItem = item;
                ecb.SetComponent(mergerEntities[target.Index], merger);
                break;
            case TransportKind.Splitter:
                Splitter splitter =
                    EntityManager.GetComponentData<Splitter>(
                        splitterEntities[target.Index]);
                splitter.CurrentItem = item;
                ecb.SetComponent(splitterEntities[target.Index], splitter);
                break;
        }
    }

    private void SortPortOwners(Entity[] owners)
    {
        Array.Sort(owners, (left, right) =>
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
    }

    private static bool TryGetBuildingPort(
        DynamicBuffer<BuildingPort> ports,
        BuildingPortType type,
        byte index,
        out BuildingPort result)
    {
        for (int i = 0; i < ports.Length; i++)
        {
            BuildingPort port = ports[i];
            if (port.Type == type && port.Index == index)
            {
                result = port;
                return true;
            }
        }

        result = default;
        return false;
    }

    private static ulong GetAcceptedCount(
        Dictionary<PortKey, ulong> accepted,
        PortKey key,
        ulong applied)
    {
        if (!accepted.TryGetValue(key, out ulong value) || value < applied)
        {
            value = applied;
            accepted[key] = value;
        }

        return value;
    }

    private static int GetEffectiveCount(
        Dictionary<PortKey, ulong> accepted,
        PortKey key,
        ulong applied,
        int publishedCount)
    {
        ulong acceptedCount = GetAcceptedCount(accepted, key, applied);
        ulong outstanding = acceptedCount - applied;
        return outstanding >= (ulong)math.max(0, publishedCount)
            ? 0
            : publishedCount - (int)outstanding;
    }

    private void WriteTransportSnapshots(
        Entity[] beltEntities,
        Belt[] belts,
        Entity[] mergerEntities,
        Merger[] mergers,
        Entity[] splitterEntities,
        Splitter[] splitters)
    {
        for (int i = 0; i < beltEntities.Length; i++)
        {
            EntityManager.SetComponentData(beltEntities[i], belts[i]);
        }
        for (int i = 0; i < mergerEntities.Length; i++)
        {
            EntityManager.SetComponentData(mergerEntities[i], mergers[i]);
        }
        for (int i = 0; i < splitterEntities.Length; i++)
        {
            EntityManager.SetComponentData(splitterEntities[i], splitters[i]);
        }
    }

    private static int GetMergerInputIndex(
        int2 direction,
        int2 travelDirection)
    {
        if (math.all(travelDirection == direction))
        {
            return 0;
        }
        if (math.all(travelDirection == new int2(direction.y, -direction.x)))
        {
            return 1;
        }
        if (math.all(travelDirection == new int2(-direction.y, direction.x)))
        {
            return 2;
        }
        return -1;
    }

    private static int2 GetSplitterOutputDirection(
        int2 direction,
        int outputIndex)
    {
        switch (WrapThree(outputIndex))
        {
            case 0:
                return direction;
            case 1:
                return new int2(-direction.y, direction.x);
            default:
                return new int2(direction.y, -direction.x);
        }
    }

    private static int GetSplitterOutputIndex(
        int2 direction,
        int2 outputDirection)
    {
        for (int i = 0; i < 3; i++)
        {
            if (math.all(
                    GetSplitterOutputDirection(direction, i) ==
                    outputDirection))
            {
                return i;
            }
        }

        return -1;
    }

    private static int WrapThree(int value)
    {
        int wrapped = value % 3;
        return wrapped < 0 ? wrapped + 3 : wrapped;
    }
}
