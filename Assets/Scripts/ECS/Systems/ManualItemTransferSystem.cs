using Unity.Collections;
using Unity.Entities;
using System;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(RecipeSelectionCommandSystem))]
[UpdateBefore(typeof(ItemProcessSystem))]
public partial struct ManualItemTransferSystem : ISystem
{
    private EntityQuery commandQuery;
    private EntityQuery ownerQuery;

    public void OnCreate(ref SystemState state)
    {
        commandQuery = state.GetEntityQuery(
            ComponentType.ReadWrite<MoveItemPlayerCommand>(),
            ComponentType.ReadWrite<MoveItemPlayerResult>());
        ownerQuery = state.GetEntityQuery(
            ComponentType.ReadOnly<ItemContainerIdentity>());
        state.RequireForUpdate(commandQuery);
        state.RequireForUpdate<FactoryDatabase>();
    }

    public void OnUpdate(ref SystemState state)
    {
        state.Dependency.Complete();
        using NativeArray<Entity> commandEntities =
            commandQuery.ToEntityArray(Allocator.Temp);
        using NativeList<CommandEnvelope> pending = new(Allocator.Temp);
        for (int entityIndex = 0; entityIndex < commandEntities.Length; entityIndex++)
        {
            Entity owner = commandEntities[entityIndex];
            DynamicBuffer<MoveItemPlayerCommand> buffer =
                state.EntityManager.GetBuffer<MoveItemPlayerCommand>(owner);
            for (int commandIndex = 0; commandIndex < buffer.Length; commandIndex++)
                pending.Add(new CommandEnvelope(owner, buffer[commandIndex]));
            buffer.Clear();
        }
        pending.Sort();
        using NativeArray<Entity> owners = ownerQuery.ToEntityArray(Allocator.Temp);
        BlobAssetReference<FactoryDatabaseBlob> databaseReference =
            SystemAPI.GetSingleton<FactoryDatabase>().Value;
        ref FactoryDatabaseBlob database = ref databaseReference.Value;

        for (int i = 0; i < pending.Length; i++)
        {
            CommandEnvelope envelope = pending[i];
            MoveItemPlayerCommand command = envelope.Command;
            MoveItemFailureReason failure = TryMove(
                ref state,
                ref database,
                owners,
                command);
            state.EntityManager.GetBuffer<MoveItemPlayerResult>(envelope.Owner).Add(
                new MoveItemPlayerResult
            {
                Header = command.Header,
                Success = failure == MoveItemFailureReason.None ? (byte)1 : (byte)0,
                MovedAmount = failure == MoveItemFailureReason.None
                    ? command.Amount
                    : (ushort)0,
                FailureReason = failure
            });
        }
    }

    private readonly struct CommandEnvelope : IComparable<CommandEnvelope>
    {
        public CommandEnvelope(Entity owner, MoveItemPlayerCommand command)
        {
            Owner = owner;
            Command = command;
        }
        public Entity Owner { get; }
        public MoveItemPlayerCommand Command { get; }
        public int CompareTo(CommandEnvelope other)
        {
            int player = Command.Header.Player.Value.CompareTo(
                other.Command.Header.Player.Value);
            return player != 0 ? player : Command.Header.ClientSequence.CompareTo(
                other.Command.Header.ClientSequence);
        }
    }

    private static MoveItemFailureReason TryMove(
        ref SystemState state,
        ref FactoryDatabaseBlob database,
        in NativeArray<Entity> owners,
        in MoveItemPlayerCommand command)
    {
        if ((command.Source.OwnerKind == ItemOwnerKind.Player &&
             command.Source.OwnerRuntimeId != command.Header.Player.Value) ||
            (command.Destination.OwnerKind == ItemOwnerKind.Player &&
             command.Destination.OwnerRuntimeId != command.Header.Player.Value))
        {
            return MoveItemFailureReason.PlayerNotFound;
        }
        if (command.Amount == 0 ||
            !TryFindOwner(ref state, owners, command.Source, out Entity source) ||
            !TryFindOwner(ref state, owners, command.Destination, out Entity destination))
        {
            return MoveItemFailureReason.OwnerNotFound;
        }
        if (source == destination &&
            command.Source.Domain == command.Destination.Domain &&
            command.Source.SlotIndex == command.Destination.SlotIndex)
        {
            return MoveItemFailureReason.DestinationRejected;
        }

        if (!TryReadSlot(
                ref state,
                source,
                command.Source,
                out ItemId itemType,
                out ushort sourceCount,
                out int sourceBufferIndex))
        {
            return MoveItemFailureReason.InvalidSlot;
        }
        if (!itemType.IsValid || sourceCount < command.Amount)
            return MoveItemFailureReason.EmptySource;
        if (itemType != command.ExpectedItemType)
            return MoveItemFailureReason.StaleSnapshot;
        if (!FactoryDatabaseUtility.IsValidItem(ref database, itemType))
            return MoveItemFailureReason.StaleSnapshot;

        if (!TryReadDestination(
                ref state,
                ref database,
                destination,
                command.Destination,
                itemType,
                out ItemId destinationType,
                out ushort destinationCount,
                out ushort destinationCapacity,
                out int destinationBufferIndex,
                out MoveItemFailureReason destinationFailure))
        {
            return destinationFailure;
        }
        if (destinationType.IsValid && destinationType != itemType)
            return MoveItemFailureReason.DestinationRejected;
        if (destinationCount + command.Amount > destinationCapacity)
            return MoveItemFailureReason.CapacityExceeded;

        WriteSlot(
            ref state,
            source,
            command.Source,
            sourceBufferIndex,
            itemType,
            (ushort)(sourceCount - command.Amount));
        WriteSlot(
            ref state,
            destination,
            command.Destination,
            destinationBufferIndex,
            itemType,
            (ushort)(destinationCount + command.Amount));
        UpdateRevision(ref state, source, -(int)command.Amount);
        UpdateRevision(ref state, destination, command.Amount);
        return MoveItemFailureReason.None;
    }

    private static bool TryFindOwner(
        ref SystemState state,
        in NativeArray<Entity> owners,
        in ItemEndpoint endpoint,
        out Entity result)
    {
        for (int i = 0; i < owners.Length; i++)
        {
            ItemContainerIdentity identity = state.EntityManager.GetComponentData<
                ItemContainerIdentity>(owners[i]);
            if (identity.RuntimeId == endpoint.OwnerRuntimeId)
            {
                bool kindMatches = endpoint.OwnerKind == ItemOwnerKind.Player
                    ? state.EntityManager.HasComponent<PlayerIdentity>(owners[i])
                    : endpoint.OwnerKind == ItemOwnerKind.Storage
                        ? state.EntityManager.HasComponent<StorageState>(owners[i])
                        : state.EntityManager.HasComponent<ItemProcessState>(owners[i]);
                if (!kindMatches)
                    continue;
                result = owners[i];
                return true;
            }
        }
        result = Entity.Null;
        return false;
    }

    private static bool TryReadSlot(
        ref SystemState state,
        Entity owner,
        in ItemEndpoint endpoint,
        out ItemId itemType,
        out ushort count,
        out int bufferIndex)
    {
        if (endpoint.Domain == ItemSlotDomain.Inventory &&
            state.EntityManager.HasBuffer<InventorySlot>(owner))
        {
            DynamicBuffer<InventorySlot> slots =
                state.EntityManager.GetBuffer<InventorySlot>(owner);
            bufferIndex = endpoint.SlotIndex;
            if (bufferIndex >= slots.Length)
            {
                itemType = default;
                count = 0;
                return false;
            }
            itemType = slots[bufferIndex].ItemType;
            count = slots[bufferIndex].Count;
            return true;
        }

        if (state.EntityManager.HasBuffer<ProcessorItemSlot>(owner) &&
            TryGetProcessorSlotIndex(
                state.EntityManager.GetBuffer<ProcessorItemSlot>(owner),
                endpoint.Domain,
                endpoint.SlotIndex,
                out bufferIndex))
        {
            ProcessorItemSlot slot =
                state.EntityManager.GetBuffer<ProcessorItemSlot>(owner)[bufferIndex];
            itemType = slot.AcceptedItemType;
            count = slot.Count;
            return true;
        }
        itemType = default;
        count = 0;
        bufferIndex = -1;
        return false;
    }

    private static bool TryReadDestination(
        ref SystemState state,
        ref FactoryDatabaseBlob database,
        Entity owner,
        in ItemEndpoint endpoint,
        ItemId itemType,
        out ItemId currentType,
        out ushort count,
        out ushort capacity,
        out int bufferIndex,
        out MoveItemFailureReason failure)
    {
        if (!TryReadSlot(
                ref state,
                owner,
                endpoint,
                out currentType,
                out count,
                out bufferIndex))
        {
            capacity = 0;
            failure = MoveItemFailureReason.InvalidSlot;
            return false;
        }
        if (endpoint.Domain == ItemSlotDomain.ProcessorOutput)
        {
            capacity = 0;
            failure = MoveItemFailureReason.DestinationRejected;
            return false;
        }
        if (endpoint.Domain == ItemSlotDomain.ProcessorInput)
        {
            ProcessorItemSlot slot =
                state.EntityManager.GetBuffer<ProcessorItemSlot>(owner)[bufferIndex];
            if (slot.AcceptedItemType != itemType)
            {
                capacity = 0;
                failure = MoveItemFailureReason.DestinationRejected;
                return false;
            }
            capacity = slot.Capacity;
        }
        else
        {
            int maxStack = database.ItemsById[itemType.Value].MaxStack;
            if (state.EntityManager.HasComponent<StorageState>(owner))
            {
                StorageState storage = state.EntityManager.GetComponentData<
                    StorageState>(owner);
                int totalFree = Unity.Mathematics.math.max(
                    0,
                    storage.Capacity - storage.TotalStored);
                capacity = (ushort)(count + Unity.Mathematics.math.min(
                    maxStack - count,
                    totalFree));
            }
            else
            {
                capacity = (ushort)maxStack;
            }
        }
        failure = MoveItemFailureReason.None;
        return true;
    }

    private static bool TryGetProcessorSlotIndex(
        in DynamicBuffer<ProcessorItemSlot> slots,
        ItemSlotDomain domain,
        ushort logicalIndex,
        out int bufferIndex)
    {
        ProcessorSlotKind kind = domain == ItemSlotDomain.ProcessorInput
            ? ProcessorSlotKind.Input
            : ProcessorSlotKind.Output;
        int found = 0;
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i].Kind != kind)
                continue;
            if (found++ == logicalIndex)
            {
                bufferIndex = i;
                return true;
            }
        }
        bufferIndex = -1;
        return false;
    }

    private static void WriteSlot(
        ref SystemState state,
        Entity owner,
        in ItemEndpoint endpoint,
        int bufferIndex,
        ItemId itemType,
        ushort count)
    {
        if (endpoint.Domain == ItemSlotDomain.Inventory)
        {
            DynamicBuffer<InventorySlot> slots =
                state.EntityManager.GetBuffer<InventorySlot>(owner);
            slots[bufferIndex] = count == 0
                ? default
                : new InventorySlot { ItemType = itemType, Count = count };
            return;
        }
        DynamicBuffer<ProcessorItemSlot> processorSlots =
            state.EntityManager.GetBuffer<ProcessorItemSlot>(owner);
        ProcessorItemSlot slot = processorSlots[bufferIndex];
        slot.Count = count;
        processorSlots[bufferIndex] = slot;
    }

    private static void UpdateRevision(
        ref SystemState state,
        Entity owner,
        int storedDelta)
    {
        if (state.EntityManager.HasComponent<PlayerInventory>(owner))
        {
            PlayerInventory inventory = state.EntityManager.GetComponentData<
                PlayerInventory>(owner);
            inventory.Revision++;
            state.EntityManager.SetComponentData(owner, inventory);
        }
        if (state.EntityManager.HasComponent<StorageState>(owner))
        {
            StorageState storage = state.EntityManager.GetComponentData<
                StorageState>(owner);
            storage.TotalStored += storedDelta;
            storage.Revision++;
            state.EntityManager.SetComponentData(owner, storage);
        }
        if (state.EntityManager.HasComponent<ItemProcessState>(owner))
        {
            ItemProcessState process = state.EntityManager.GetComponentData<
                ItemProcessState>(owner);
            process.InventoryRevision++;
            state.EntityManager.SetComponentData(owner, process);
        }
    }
}
