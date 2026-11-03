using System;
using System.Collections.Generic;
using Unity.Entities;

public readonly struct PlayerCommandResult
{
    public PlayerCommandResult(
        PlayerCommandHeader header,
        PlayerCommandKind kind,
        bool success,
        PlayerCommandFailureReason failureReason)
    {
        Header = header;
        Kind = kind;
        Success = success;
        FailureReason = failureReason;
    }

    public PlayerCommandHeader Header { get; }
    public PlayerCommandKind Kind { get; }
    public bool Success { get; }
    public PlayerCommandFailureReason FailureReason { get; }
}

public sealed class PlayerCommandMailbox
{
    private readonly List<RecipeSelectionCommand> recipes = new();
    private readonly List<MoveItemPlayerCommand> moves = new();
    private readonly List<GridBuildPlayerCommand> builds = new();
    private readonly Queue<PlayerCommandResult> results = new();

    internal void Enqueue(RecipeSelectionCommand command) => recipes.Add(command);
    internal void Enqueue(MoveItemPlayerCommand command) => moves.Add(command);
    internal void Enqueue(GridBuildPlayerCommand command) => builds.Add(command);

    internal void Drain(
        List<RecipeSelectionCommand> recipeDestination,
        List<MoveItemPlayerCommand> moveDestination,
        List<GridBuildPlayerCommand> buildDestination)
    {
        recipeDestination.AddRange(recipes);
        moveDestination.AddRange(moves);
        buildDestination.AddRange(builds);
        recipes.Clear();
        moves.Clear();
        builds.Clear();
    }

    internal void Publish(PlayerCommandResult result) => results.Enqueue(result);

    internal bool TryTakeResult(PlayerId player, out PlayerCommandResult result)
    {
        int count = results.Count;
        for (int i = 0; i < count; i++)
        {
            PlayerCommandResult candidate = results.Dequeue();
            if (candidate.Header.Player == player)
            {
                result = candidate;
                return true;
            }
            results.Enqueue(candidate);
        }
        result = default;
        return false;
    }
}

public sealed class PlayerCommandBus
{
    private readonly PlayerCommandMailbox mailbox;
    private readonly PlayerId player;
    private ulong nextRequestId = 1;
    private ulong nextSequence = 1;

    public PlayerCommandBus(PlayerId player, PlayerCommandMailbox mailbox)
    {
        this.player = player;
        this.mailbox = mailbox ?? throw new ArgumentNullException(nameof(mailbox));
    }

    public event Action<PlayerCommandResult> ResultReceived;

    public ulong Submit(RecipeSelectionCommand command)
    {
        command.Header = NextHeader();
        mailbox.Enqueue(command);
        return command.Header.RequestId;
    }

    public ulong Submit(MoveItemPlayerCommand command)
    {
        command.Header = NextHeader();
        mailbox.Enqueue(command);
        return command.Header.RequestId;
    }

    public ulong Submit(GridBuildPlayerCommand command)
    {
        command.Header = NextHeader();
        mailbox.Enqueue(command);
        return command.Header.RequestId;
    }

    public void PumpResults()
    {
        while (mailbox.TryTakeResult(player, out PlayerCommandResult result))
            ResultReceived?.Invoke(result);
    }

    private PlayerCommandHeader NextHeader() => new()
    {
        Player = player,
        RequestId = nextRequestId++,
        ClientSequence = nextSequence++
    };
}

public static class PlayerCommandRuntimeServices
{
    private static readonly Dictionary<World, PlayerCommandMailbox> mailboxes = new();
    private static readonly Dictionary<World, Dictionary<PlayerId, PlayerCommandBus>> buses = new();

    public static PlayerCommandMailbox GetOrCreateMailbox(World world)
    {
        if (world == null || !world.IsCreated)
            throw new InvalidOperationException("A created ECS World is required.");
        if (!mailboxes.TryGetValue(world, out PlayerCommandMailbox mailbox))
        {
            mailbox = new PlayerCommandMailbox();
            mailboxes.Add(world, mailbox);
        }
        return mailbox;
    }

    public static PlayerCommandBus GetOrCreateBus(World world, PlayerId player)
    {
        PlayerCommandMailbox mailbox = GetOrCreateMailbox(world);
        if (!buses.TryGetValue(world, out Dictionary<PlayerId, PlayerCommandBus> worldBuses))
        {
            worldBuses = new Dictionary<PlayerId, PlayerCommandBus>();
            buses.Add(world, worldBuses);
        }
        if (!worldBuses.TryGetValue(player, out PlayerCommandBus bus))
        {
            bus = new PlayerCommandBus(player, mailbox);
            worldBuses.Add(player, bus);
        }
        return bus;
    }

    public static bool TryGetMailbox(World world, out PlayerCommandMailbox mailbox)
    {
        if (world != null && world.IsCreated)
            return mailboxes.TryGetValue(world, out mailbox);
        mailbox = null;
        return false;
    }

    public static void Detach(World world)
    {
        mailboxes.Remove(world);
        buses.Remove(world);
    }
}
