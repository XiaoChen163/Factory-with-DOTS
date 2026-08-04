using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

namespace Factory.Tests
{
    /// <summary>
    /// Builds transport snapshots and advances them with the same multi-pass
    /// contract currently used by BeltTransferSystem for one fixed tick.
    /// </summary>
    public sealed class TransportScenario : IDisposable
    {
        private readonly World world;
        private readonly List<Entity> beltEntities = new List<Entity>();
        private readonly List<Belt> belts = new List<Belt>();
        private readonly List<Entity> mergerEntities = new List<Entity>();
        private readonly List<Merger> mergers = new List<Merger>();
        private readonly List<Entity> splitterEntities = new List<Entity>();
        private readonly List<Splitter> splitters = new List<Splitter>();
        private readonly HashSet<Entity> processedJunctions =
            new HashSet<Entity>();

        public TransportScenario(string name = "Transport scenario")
        {
            world = new World(name);
        }

        public IReadOnlyList<Belt> Belts => belts;
        public IReadOnlyList<Merger> Mergers => mergers;
        public IReadOnlyList<Splitter> Splitters => splitters;

        public Entity CreateItem(ushort itemType = 1)
        {
            Entity item = world.EntityManager.CreateEntity();
            world.EntityManager.AddComponentData(item, new Item
            {
                ItemType = new ItemId { Value = itemType }
            });
            return item;
        }

        public Entity AddBelt(
            int2 cell,
            int2 direction,
            Entity item = default,
            float progress = 0f,
            float cellsPerSecond = 1f)
        {
            Entity entity = world.EntityManager.CreateEntity();
            beltEntities.Add(entity);
            belts.Add(new Belt
            {
                Cell = cell,
                Direction = direction,
                NextCell = cell + direction,
                CurrentItem = item,
                Progress = progress,
                CellsPerSecond = cellsPerSecond
            });
            return entity;
        }

        public Entity AddMerger(
            int2 cell,
            int2 direction,
            Entity item = default,
            int nextInputIndex = 0)
        {
            Entity entity = world.EntityManager.CreateEntity();
            mergerEntities.Add(entity);
            mergers.Add(new Merger
            {
                Cell = cell,
                Direction = direction,
                CurrentItem = item,
                NextInputIndex = nextInputIndex
            });
            return entity;
        }

        public Entity AddSplitter(
            int2 cell,
            int2 direction,
            Entity item = default,
            int nextOutputIndex = 0)
        {
            Entity entity = world.EntityManager.CreateEntity();
            splitterEntities.Add(entity);
            splitters.Add(new Splitter
            {
                Cell = cell,
                Direction = direction,
                CurrentItem = item,
                NextOutputIndex = nextOutputIndex
            });
            return entity;
        }

        public TransportTickResult ResolveTick()
        {
            Entity[] beltEntitySnapshot = beltEntities.ToArray();
            Belt[] beltSnapshot = belts.ToArray();
            Entity[] mergerEntitySnapshot = mergerEntities.ToArray();
            Merger[] mergerSnapshot = mergers.ToArray();
            Entity[] splitterEntitySnapshot = splitterEntities.ToArray();
            Splitter[] splitterSnapshot = splitters.ToArray();

            processedJunctions.Clear();
            int loopCount = 0;
            int readyRequestCount = 0;
            int acceptedTransferCount = 0;
            int executedPassCount = 0;
            int passLimit = mergerSnapshot.Length +
                splitterSnapshot.Length + 1;

            for (int pass = 0; pass < passLimit; pass++)
            {
                FactoryTransferResolver.Resolve(
                    beltEntitySnapshot,
                    beltSnapshot,
                    mergerEntitySnapshot,
                    mergerSnapshot,
                    splitterEntitySnapshot,
                    splitterSnapshot,
                    processedJunctions,
                    out int passLoopCount,
                    out int passReadyRequestCount,
                    out int passAcceptedTransferCount);

                executedPassCount++;
                loopCount = passLoopCount;
                readyRequestCount += passReadyRequestCount;
                acceptedTransferCount += passAcceptedTransferCount;
                if (passAcceptedTransferCount == 0)
                {
                    break;
                }
            }

            ReplaceContents(belts, beltSnapshot);
            ReplaceContents(mergers, mergerSnapshot);
            ReplaceContents(splitters, splitterSnapshot);

            return new TransportTickResult(
                loopCount,
                readyRequestCount,
                acceptedTransferCount,
                executedPassCount);
        }

        public TransportStateSnapshot CaptureState()
        {
            List<TransportNodeSnapshot> nodes =
                new List<TransportNodeSnapshot>(
                    belts.Count + mergers.Count + splitters.Count);

            for (int i = 0; i < belts.Count; i++)
            {
                Belt belt = belts[i];
                nodes.Add(new TransportNodeSnapshot(
                    TransportNodeKind.Belt,
                    belt.Cell,
                    belt.CurrentItem,
                    belt.Progress,
                    0));
            }
            for (int i = 0; i < mergers.Count; i++)
            {
                Merger merger = mergers[i];
                nodes.Add(new TransportNodeSnapshot(
                    TransportNodeKind.Merger,
                    merger.Cell,
                    merger.CurrentItem,
                    0f,
                    merger.NextInputIndex));
            }
            for (int i = 0; i < splitters.Count; i++)
            {
                Splitter splitter = splitters[i];
                nodes.Add(new TransportNodeSnapshot(
                    TransportNodeKind.Splitter,
                    splitter.Cell,
                    splitter.CurrentItem,
                    0f,
                    splitter.NextOutputIndex));
            }

            nodes.Sort(TransportNodeSnapshot.CompareByCellAndKind);
            return new TransportStateSnapshot(nodes.ToArray());
        }

        public void Dispose()
        {
            if (world != null && world.IsCreated)
            {
                world.Dispose();
            }
        }

        private static void ReplaceContents<T>(List<T> target, T[] source)
        {
            target.Clear();
            target.AddRange(source);
        }
    }

    public readonly struct TransportTickResult
    {
        public TransportTickResult(
            int loopCount,
            int readyRequestCount,
            int acceptedTransferCount,
            int executedPassCount)
        {
            LoopCount = loopCount;
            ReadyRequestCount = readyRequestCount;
            AcceptedTransferCount = acceptedTransferCount;
            ExecutedPassCount = executedPassCount;
        }

        public int LoopCount { get; }
        public int ReadyRequestCount { get; }
        public int AcceptedTransferCount { get; }
        public int ExecutedPassCount { get; }
    }

    public enum TransportNodeKind : byte
    {
        Belt,
        Merger,
        Splitter
    }

    public readonly struct TransportNodeSnapshot
    {
        public TransportNodeSnapshot(
            TransportNodeKind kind,
            int2 cell,
            Entity currentItem,
            float progress,
            int cursor)
        {
            Kind = kind;
            Cell = cell;
            CurrentItem = currentItem;
            Progress = progress;
            Cursor = cursor;
        }

        public TransportNodeKind Kind { get; }
        public int2 Cell { get; }
        public Entity CurrentItem { get; }
        public float Progress { get; }
        public int Cursor { get; }

        internal static int CompareByCellAndKind(
            TransportNodeSnapshot left,
            TransportNodeSnapshot right)
        {
            int x = left.Cell.x.CompareTo(right.Cell.x);
            if (x != 0)
            {
                return x;
            }

            int y = left.Cell.y.CompareTo(right.Cell.y);
            return y != 0 ? y : left.Kind.CompareTo(right.Kind);
        }
    }

    public readonly struct TransportStateSnapshot
    {
        public TransportStateSnapshot(TransportNodeSnapshot[] nodes)
        {
            Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        }

        public TransportNodeSnapshot[] Nodes { get; }
    }
}
