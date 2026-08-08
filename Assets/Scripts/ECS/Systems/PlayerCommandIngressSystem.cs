using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

[UpdateInGroup(typeof(InitializationSystemGroup))]
[UpdateAfter(typeof(PlayerBootstrapSystem))]
public partial class PlayerCommandIngressSystem : SystemBase
{
    private readonly List<RecipeSelectionCommand> recipes = new();
    private readonly List<MoveItemPlayerCommand> moves = new();
    private readonly List<GridBuildPlayerCommand> builds = new();

    protected override void OnUpdate()
    {
        if (!PlayerCommandRuntimeServices.TryGetMailbox(World, out PlayerCommandMailbox mailbox))
            return;
        mailbox.Drain(recipes, moves, builds);
        if (recipes.Count + moves.Count + builds.Count == 0)
            return;

        List<Envelope> commands = new(recipes.Count + moves.Count + builds.Count);
        for (int i = 0; i < recipes.Count; i++)
            commands.Add(new Envelope(recipes[i]));
        for (int i = 0; i < moves.Count; i++)
            commands.Add(new Envelope(moves[i]));
        for (int i = 0; i < builds.Count; i++)
            commands.Add(new Envelope(builds[i]));
        recipes.Clear();
        moves.Clear();
        builds.Clear();
        commands.Sort(Envelope.Compare);

        Dictionary<PlayerId, Entity> players = new();
        EntityQuery query = GetEntityQuery(ComponentType.ReadOnly<PlayerIdentity>());
        using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
        using NativeArray<PlayerIdentity> identities =
            query.ToComponentDataArray<PlayerIdentity>(Allocator.Temp);
        for (int i = 0; i < entities.Length; i++)
            players[identities[i].Value] = entities[i];

        for (int i = 0; i < commands.Count; i++)
        {
            Envelope command = commands[i];
            if (!players.TryGetValue(command.Header.Player, out Entity playerEntity))
            {
                Reject(mailbox, command, PlayerCommandFailureReason.PlayerNotFound);
                continue;
            }
            EnsureBuffers(playerEntity);
            PlayerCommandSequenceState sequence =
                EntityManager.GetComponentData<PlayerCommandSequenceState>(playerEntity);
            if (command.Header.ClientSequence <= sequence.LastAcceptedSequence)
            {
                Reject(mailbox, command, PlayerCommandFailureReason.SequenceDuplicate);
                continue;
            }
            sequence.LastAcceptedSequence = command.Header.ClientSequence;
            EntityManager.SetComponentData(playerEntity, sequence);
            command.Append(EntityManager, playerEntity);
        }
    }

    protected override void OnDestroy() =>
        PlayerCommandRuntimeServices.Detach(World);

    private void EnsureBuffers(Entity entity)
    {
        if (!EntityManager.HasComponent<PlayerCommandSequenceState>(entity))
            EntityManager.AddComponentData(entity, new PlayerCommandSequenceState());
        if (!EntityManager.HasBuffer<RecipeSelectionCommand>(entity))
            EntityManager.AddBuffer<RecipeSelectionCommand>(entity);
        if (!EntityManager.HasBuffer<RecipeSelectionResult>(entity))
            EntityManager.AddBuffer<RecipeSelectionResult>(entity);
        if (!EntityManager.HasBuffer<MoveItemPlayerCommand>(entity))
            EntityManager.AddBuffer<MoveItemPlayerCommand>(entity);
        if (!EntityManager.HasBuffer<MoveItemPlayerResult>(entity))
            EntityManager.AddBuffer<MoveItemPlayerResult>(entity);
        if (!EntityManager.HasBuffer<GridBuildPlayerCommand>(entity))
            EntityManager.AddBuffer<GridBuildPlayerCommand>(entity);
        if (!EntityManager.HasBuffer<GridBuildPlayerResult>(entity))
            EntityManager.AddBuffer<GridBuildPlayerResult>(entity);
    }

    private static void Reject(
        PlayerCommandMailbox mailbox,
        Envelope command,
        PlayerCommandFailureReason failure) =>
        mailbox.Publish(new PlayerCommandResult(
            command.Header, command.Kind, false, failure));

    private readonly struct Envelope
    {
        private readonly RecipeSelectionCommand recipe;
        private readonly MoveItemPlayerCommand move;
        private readonly GridBuildPlayerCommand build;

        public Envelope(RecipeSelectionCommand value)
        { recipe = value; move = default; build = default; Kind = PlayerCommandKind.SelectRecipe; Header = value.Header; }
        public Envelope(MoveItemPlayerCommand value)
        { recipe = default; move = value; build = default; Kind = PlayerCommandKind.MoveItem; Header = value.Header; }
        public Envelope(GridBuildPlayerCommand value)
        { recipe = default; move = default; build = value; Kind = PlayerCommandKind.GridBuild; Header = value.Header; }

        public PlayerCommandHeader Header { get; }
        public PlayerCommandKind Kind { get; }

        public void Append(EntityManager manager, Entity entity)
        {
            if (Kind == PlayerCommandKind.SelectRecipe)
                manager.GetBuffer<RecipeSelectionCommand>(entity).Add(recipe);
            else if (Kind == PlayerCommandKind.MoveItem)
                manager.GetBuffer<MoveItemPlayerCommand>(entity).Add(move);
            else
                manager.GetBuffer<GridBuildPlayerCommand>(entity).Add(build);
        }

        public static int Compare(Envelope left, Envelope right)
        {
            int player = left.Header.Player.Value.CompareTo(right.Header.Player.Value);
            return player != 0
                ? player
                : left.Header.ClientSequence.CompareTo(right.Header.ClientSequence);
        }
    }
}
