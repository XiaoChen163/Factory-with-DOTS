using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Factory.Tests
{
    /// <summary>
    /// Builds transport snapshots and can advance them with either the legacy
    /// resolver or the Phase 3 Burst arbitration job for one fixed tick.
    /// </summary>
    public sealed class TransportScenario : IDisposable
    {
        private readonly World world;
        private readonly List<Entity> items = new List<Entity>();
        private readonly List<Entity> beltEntities = new List<Entity>();
        private readonly List<BeltTopology> beltTopologies =
            new List<BeltTopology>();
        private readonly List<BeltState> beltStates =
            new List<BeltState>();
        private readonly List<Entity> mergerEntities =
            new List<Entity>();
        private readonly List<Merger> mergers = new List<Merger>();
        private readonly List<Entity> splitterEntities =
            new List<Entity>();
        private readonly List<Splitter> splitters =
            new List<Splitter>();
        private readonly HashSet<Entity> processedJunctions =
            new HashSet<Entity>();
        private readonly FactoryLinearTransferResolver linearResolver =
            new FactoryLinearTransferResolver();

        public TransportScenario(string name = "Transport scenario")
        {
            world = new World(name);
        }

        public IReadOnlyList<BeltTopology> BeltTopologies =>
            beltTopologies;
        public IReadOnlyList<BeltState> BeltStates => beltStates;
        public IReadOnlyList<Merger> Mergers => mergers;
        public IReadOnlyList<Splitter> Splitters => splitters;
        public int LinearTopologyRebuildCount =>
            linearResolver.TopologyRebuildCount;
        public int LinearCandidateInspectionCount =>
            linearResolver.LastCandidateInspectionCount;
        public int LinearRoutingPassCount =>
            linearResolver.LastRoutingPassCount;

        public Entity CreateItem(ushort itemType = 1)
        {
            Entity item = world.EntityManager.CreateEntity();
            items.Add(item);
            world.EntityManager.AddComponentData(item, new Item
            {
                ItemType = new ItemId { Value = itemType }
            });
            return item;
        }

        public int GetItemOrdinal(Entity item)
        {
            return item == Entity.Null ? -1 : items.IndexOf(item);
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
            beltTopologies.Add(new BeltTopology
            {
                Cell = cell,
                Direction = direction,
                CellsPerSecond = cellsPerSecond
            });
            beltStates.Add(new BeltState
            {
                CurrentItem = item,
                Progress = progress
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
            BeltTopology[] beltTopologySnapshot =
                beltTopologies.ToArray();
            BeltState[] beltStateSnapshot = beltStates.ToArray();
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
                    beltTopologySnapshot,
                    beltStateSnapshot,
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

            ReplaceContents(beltTopologies, beltTopologySnapshot);
            ReplaceContents(beltStates, beltStateSnapshot);
            ReplaceContents(mergers, mergerSnapshot);
            ReplaceContents(splitters, splitterSnapshot);

            return new TransportTickResult(
                loopCount,
                readyRequestCount,
                acceptedTransferCount,
                executedPassCount);
        }
        public TransportTickResult ResolveTickLinear(uint revision = 1)
        {
            using NativeArray<Entity> beltEntitySnapshot =
                new NativeArray<Entity>(
                    beltEntities.ToArray(),
                    Allocator.TempJob);
            using NativeArray<BeltTopology> beltTopologySnapshot =
                new NativeArray<BeltTopology>(
                    beltTopologies.ToArray(),
                    Allocator.TempJob);
            using NativeArray<BeltState> beltStateSnapshot =
                new NativeArray<BeltState>(
                    beltStates.ToArray(),
                    Allocator.TempJob);
            using NativeArray<Entity> mergerEntitySnapshot =
                new NativeArray<Entity>(
                    mergerEntities.ToArray(),
                    Allocator.TempJob);
            using NativeArray<Merger> mergerSnapshot =
                new NativeArray<Merger>(
                    mergers.ToArray(),
                    Allocator.TempJob);
            using NativeArray<Entity> splitterEntitySnapshot =
                new NativeArray<Entity>(
                    splitterEntities.ToArray(),
                    Allocator.TempJob);
            using NativeArray<Splitter> splitterSnapshot =
                new NativeArray<Splitter>(
                    splitters.ToArray(),
                    Allocator.TempJob);

            linearResolver.EnsureTopology(
                revision,
                beltEntitySnapshot,
                beltTopologySnapshot,
                mergerEntitySnapshot,
                mergerSnapshot,
                splitterEntitySnapshot,
                splitterSnapshot);

            FactoryTransferArbitrationJob job =
                linearResolver.CreateArbitrationJob();
            job.EnableInterface = 0;
            job.BeltStates = beltStateSnapshot;
            job.Mergers = mergerSnapshot;
            job.Splitters = splitterSnapshot;
            job.Execute();

            ReplaceContents(beltTopologies, beltTopologySnapshot);
            ReplaceContents(beltStates, beltStateSnapshot);
            ReplaceContents(mergers, mergerSnapshot);
            ReplaceContents(splitters, splitterSnapshot);

            return new TransportTickResult(
                linearResolver.LoopCount,
                linearResolver.LastReadyRequestCount,
                linearResolver.LastAcceptedTransferCount,
                1);
        }

        public TransportStateSnapshot CaptureState()
        {
            List<TransportNodeSnapshot> nodes =
                new List<TransportNodeSnapshot>(
                    beltTopologies.Count + mergers.Count +
                    splitters.Count);

            for (int i = 0; i < beltTopologies.Count; i++)
            {
                BeltTopology topology = beltTopologies[i];
                BeltState state = beltStates[i];
                nodes.Add(new TransportNodeSnapshot(
                    TransportNodeKind.Belt,
                    topology.Cell,
                    state.CurrentItem,
                    state.Progress,
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
            linearResolver.Dispose();
            if (world != null && world.IsCreated)
            {
                world.Dispose();
            }
        }

        private static void ReplaceContents<T>(
            List<T> target,
            T[] source)
        {
            target.Clear();
            target.AddRange(source);
        }

        private static void ReplaceContents<T>(
            List<T> target,
            NativeArray<T> source)
            where T : unmanaged
        {
            target.Clear();
            for (int i = 0; i < source.Length; i++)
            {
                target.Add(source[i]);
            }
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
