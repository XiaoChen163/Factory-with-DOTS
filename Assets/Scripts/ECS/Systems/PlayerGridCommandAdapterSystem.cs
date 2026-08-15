using System;
using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(GridBuildCommandSystem))]
public partial struct PlayerGridCommandAdapterSystem : ISystem
{
    private EntityQuery playerQuery;
    private EntityQuery gridQuery;

    public void OnCreate(ref SystemState state)
    {
        playerQuery = state.GetEntityQuery(
            ComponentType.ReadWrite<GridBuildPlayerCommand>(),
            ComponentType.ReadWrite<GridBuildPlayerResult>());
        gridQuery = state.GetEntityQuery(
            ComponentType.ReadWrite<GridBuildCommand>(),
            ComponentType.ReadWrite<GridBuildResult>(),
            ComponentType.ReadWrite<PlayerGridCommandPending>(),
            ComponentType.ReadWrite<PlayerGridCommandAdapterState>());
        state.RequireForUpdate(playerQuery);
        state.RequireForUpdate(gridQuery);
    }

    public void OnUpdate(ref SystemState state)
    {
        if (gridQuery.CalculateEntityCount() != 1)
            return;
        state.Dependency.Complete();
        Entity grid = gridQuery.GetSingletonEntity();
        DynamicBuffer<GridBuildCommand> gridCommands =
            state.EntityManager.GetBuffer<GridBuildCommand>(grid);
        DynamicBuffer<PlayerGridCommandPending> mappings =
            state.EntityManager.GetBuffer<PlayerGridCommandPending>(grid);
        PlayerGridCommandAdapterState adapter =
            state.EntityManager.GetComponentData<PlayerGridCommandAdapterState>(grid);
        if (adapter.NextGridRequestId == 0)
            adapter.NextGridRequestId = 1;

        using NativeArray<Entity> owners = playerQuery.ToEntityArray(Allocator.Temp);
        using NativeList<Envelope> pending = new(Allocator.Temp);
        for (int ownerIndex = 0; ownerIndex < owners.Length; ownerIndex++)
        {
            Entity owner = owners[ownerIndex];
            DynamicBuffer<GridBuildPlayerCommand> commands =
                state.EntityManager.GetBuffer<GridBuildPlayerCommand>(owner);
            for (int commandIndex = 0; commandIndex < commands.Length; commandIndex++)
                pending.Add(new Envelope(commands[commandIndex]));
            commands.Clear();
        }
        pending.Sort();

        for (int i = 0; i < pending.Length; i++)
        {
            GridBuildPlayerCommand value = pending[i].Command;
            uint gridRequestId = adapter.NextGridRequestId++;
            if (adapter.NextGridRequestId == 0)
                adapter.NextGridRequestId = 1;
            mappings.Add(new PlayerGridCommandPending
            {
                GridRequestId = gridRequestId,
                Header = value.Header
            });
            gridCommands.Add(new GridBuildCommand
            {
                RequestId = gridRequestId,
                Player = value.Header.Player,
                Type = value.Type,
                Kind = value.Kind,
                BuildingLevel = value.BuildingLevel,
                StartCell = value.StartCell,
                EndCell = value.EndCell,
                QuarterTurns = value.QuarterTurns,
                HorizontalFirst = value.HorizontalFirst,
                VisualMaterialId = value.VisualMaterialId,
                RampStartHeightUnits = value.RampStartHeightUnits,
                RampRiseHeightUnits = value.RampRiseHeightUnits
            });
        }
        state.EntityManager.SetComponentData(grid, adapter);
    }

    private readonly struct Envelope : IComparable<Envelope>
    {
        public Envelope(GridBuildPlayerCommand command) => Command = command;
        public GridBuildPlayerCommand Command { get; }
        public int CompareTo(Envelope other)
        {
            int player = Command.Header.Player.Value.CompareTo(
                other.Command.Header.Player.Value);
            return player != 0 ? player : Command.Header.ClientSequence.CompareTo(
                other.Command.Header.ClientSequence);
        }
    }
}

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(GridBuildCommandSystem))]
public partial class PlayerGridCommandResultSystem : SystemBase
{
    protected override void OnUpdate()
    {
        EntityQuery gridQuery = GetEntityQuery(
            ComponentType.ReadWrite<GridBuildResult>(),
            ComponentType.ReadWrite<PlayerGridCommandPending>());
        if (gridQuery.CalculateEntityCount() != 1)
            return;
        Entity grid = gridQuery.GetSingletonEntity();
        DynamicBuffer<GridBuildResult> results = EntityManager.GetBuffer<GridBuildResult>(grid);
        DynamicBuffer<PlayerGridCommandPending> pending =
            EntityManager.GetBuffer<PlayerGridCommandPending>(grid);
        if (results.Length == 0 || pending.Length == 0)
            return;

        EntityQuery playerQuery = GetEntityQuery(ComponentType.ReadOnly<PlayerIdentity>());
        using NativeArray<Entity> players = playerQuery.ToEntityArray(Allocator.Temp);
        for (int resultIndex = results.Length - 1; resultIndex >= 0; resultIndex--)
        {
            GridBuildResult result = results[resultIndex];
            int mappingIndex = FindMapping(pending, result.RequestId);
            if (mappingIndex < 0)
                continue;
            PlayerCommandHeader header = pending[mappingIndex].Header;
            pending.RemoveAt(mappingIndex);
            results.RemoveAt(resultIndex);
            for (int playerIndex = 0; playerIndex < players.Length; playerIndex++)
            {
                Entity player = players[playerIndex];
                if (EntityManager.GetComponentData<PlayerIdentity>(player).Value != header.Player)
                    continue;
                if (!EntityManager.HasBuffer<GridBuildPlayerResult>(player))
                    EntityManager.AddBuffer<GridBuildPlayerResult>(player);
                EntityManager.GetBuffer<GridBuildPlayerResult>(player).Add(
                    new GridBuildPlayerResult
                    {
                        Header = header,
                        Success = result.Success,
                        AffectedCount = result.AffectedCount,
                        FailureReason = result.FailureReason
                    });
                break;
            }
        }
    }

    private static int FindMapping(
        DynamicBuffer<PlayerGridCommandPending> pending,
        uint requestId)
    {
        for (int i = 0; i < pending.Length; i++)
            if (pending[i].GridRequestId == requestId)
                return i;
        return -1;
    }
}
