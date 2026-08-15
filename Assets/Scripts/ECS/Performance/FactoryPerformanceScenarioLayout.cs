using System;
using System.Collections.Generic;
using Unity.Mathematics;

public enum FactoryPerformanceScenario
{
    Mk4SerpentineHalfLoaded,
    Mk4F16Branches,
    Mk4SerpentineBlockedByMk1,
    ScalableStraight,
    MixedJunctions512,
    Mk4FullLoop4096,
    ProducerConsumer,
    ContinuousBeltBuild
}

public readonly struct FactoryPerformancePlacement
{
    public FactoryPerformancePlacement(
        BuildingKind kind,
        BuildingLevelId buildingLevel,
        GridCell cell,
        byte quarterTurns,
        string recipeKey = null)
    {
        Kind = kind;
        BuildingLevel = buildingLevel;
        Cell = cell;
        QuarterTurns = quarterTurns;
        RecipeKey = recipeKey;
    }

    public BuildingKind Kind { get; }
    public BuildingLevelId BuildingLevel { get; }
    public GridCell Cell { get; }
    public byte QuarterTurns { get; }
    public string RecipeKey { get; }
}

public sealed class FactoryPerformanceScenarioDefinition
{
    internal FactoryPerformanceScenarioDefinition(
        FactoryPerformanceScenario scenario,
        string displayName,
        string description,
        int2 gridSize,
        FactoryPerformancePlacement[] placements,
        GridCell[] initialItemCells,
        int[] branchLengths,
        int scale,
        string scaleUnit)
    {
        Scenario = scenario;
        DisplayName = displayName;
        Description = description;
        GridSize = gridSize;
        Placements = placements;
        InitialItemCells = initialItemCells;
        BranchLengths = branchLengths;
        Scale = scale;
        ScaleUnit = scaleUnit;
        TimeDelaySeconds = 0.25f;
        DragSeconds = 0.25f;
        DemolitionOrder = 0;

        for (int i = 0; i < placements.Length; i++)
        {
            switch (placements[i].Kind)
            {
                case BuildingKind.Belt:
                    BeltCount++;
                    if (placements[i].BuildingLevel.Value == 4)
                    {
                        Mk4BeltCount++;
                    }
                    else if (placements[i].BuildingLevel.Value == 1)
                    {
                        Mk1BeltCount++;
                    }
                    break;
                case BuildingKind.Splitter:
                    SplitterCount++;
                    break;
                case BuildingKind.Merger:
                    MergerCount++;
                    break;
                case BuildingKind.Miner:
                    MinerCount++;
                    ProcessorCount++;
                    break;
                case BuildingKind.Furnace:
                    FurnaceCount++;
                    ProcessorCount++;
                    break;
                case BuildingKind.Storage:
                    StorageCount++;
                    break;
            }
        }
    }

    public FactoryPerformanceScenario Scenario { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public int2 GridSize { get; }
    public FactoryPerformancePlacement[] Placements { get; }
    public GridCell[] InitialItemCells { get; }
    public int[] BranchLengths { get; }
    public int Scale { get; }
    public string ScaleUnit { get; }
    public float TimeDelaySeconds { get; internal set; }
    public float DragSeconds { get; internal set; }
    public int DemolitionOrder { get; internal set; }
    public int BeltCount { get; }
    public int Mk4BeltCount { get; }
    public int Mk1BeltCount { get; }
    public int MergerCount { get; }
    public int SplitterCount { get; }
    public int MinerCount { get; }
    public int FurnaceCount { get; }
    public int ProcessorCount { get; }
    public int StorageCount { get; }
}

public static class FactoryPerformanceScenarioLayout
{
    public const ushort IronOreItemId = 1;
    public const ushort Mk1BeltLevelId = 1;
    public const ushort Mk4BeltLevelId = 4;
    public const ushort MinerLevelId = 5;
    public const ushort FurnaceLevelId = 6;
    public const ushort StorageLevelId = 7;
    public const ushort MergerLevelId = 8;
    public const ushort SplitterLevelId = 9;
    public const ushort FoundationLevelId = 10;

    public static FactoryPerformanceScenarioDefinition Create(
        FactoryPerformanceScenario scenario,
        int scale = 0,
        int loadPercent = -1)
    {
        switch (scenario)
        {
            case FactoryPerformanceScenario.Mk4SerpentineHalfLoaded:
                return CreateHalfLoadedSerpentine(
                    ResolveScale(scale, 64, 2));
            case FactoryPerformanceScenario.Mk4F16Branches:
                return CreateF16Branches();
            case FactoryPerformanceScenario.Mk4SerpentineBlockedByMk1:
                return CreateBlockedSerpentine(
                    ResolveScale(scale, 64, 2));
            case FactoryPerformanceScenario.ScalableStraight:
                return CreateScalableStraight(
                    ResolveScale(scale, 128, 2),
                    ResolveLoadPercent(loadPercent, 50));
            case FactoryPerformanceScenario.MixedJunctions512:
                return CreateMixedJunctions(
                    ResolveScale(scale, 5, 1),
                    ResolveLoadPercent(loadPercent, 50));
            case FactoryPerformanceScenario.Mk4FullLoop4096:
                return CreateFullLoop(
                    ResolveScale(scale, 64, 2),
                    ResolveLoadPercent(loadPercent, 100));
            case FactoryPerformanceScenario.ProducerConsumer:
                return CreateProducerConsumer(
                    ResolveScale(scale, 64, 1));
            case FactoryPerformanceScenario.ContinuousBeltBuild:
                return CreateContinuousBeltBuild(
                    ResolveScale(scale, 64, 1));
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(scenario),
                    scenario,
                    null);
        }
    }

    private static FactoryPerformanceScenarioDefinition CreateScalableStraight(
        int nodeCount,
        int loadPercent)
    {
        Builder builder = new Builder(new int2(nodeCount, 1));
        List<int2> route = new List<int2>(nodeCount);
        for (int x = 0; x < nodeCount; x++)
        {
            route.Add(new int2(x, 0));
        }

        builder.AddBeltRoute(route, Mk4BeltLevelId, new int2(1, 0));
        builder.AddInitialItemsByLoadPercent(loadPercent);
        return builder.Build(
            FactoryPerformanceScenario.ScalableStraight,
            nodeCount + " Mk4 belts - " + loadPercent + "% loaded straight",
            "A parameterized straight chain for scale and occupancy comparisons.",
            nodeCount,
            "belts");
    }

    private static FactoryPerformanceScenarioDefinition CreateMixedJunctions(
        int moduleSide,
        int loadPercent)
    {
        int moduleColumns = moduleSide;
        int moduleRows = moduleSide;
        int gridHeight = moduleRows * 5 - 1;
        int loopStartX = moduleColumns * 6 + 1;
        int gridWidth = loopStartX + 16;
        int loopTop = Math.Clamp(
            moduleSide * 2 + 6,
            2,
            gridHeight - 1);
        Builder builder = new Builder(new int2(gridWidth, gridHeight));

        for (int row = 0; row < moduleRows; row++)
        for (int column = 0; column < moduleColumns; column++)
        {
            int2 origin = new int2(column * 6, row * 5);

            // Feed the merger output around the top of the module and back
            // into the splitter. A closed module keeps junction work active
            // after long warmups instead of draining into a terminal belt.
            List<int2> feedback = new List<int2>
            {
                origin + new int2(4, 1),
                origin + new int2(4, 2),
                origin + new int2(4, 3),
                origin + new int2(3, 3),
                origin + new int2(2, 3),
                origin + new int2(1, 3),
                origin + new int2(0, 3),
                origin + new int2(0, 2),
                origin + new int2(0, 1)
            };
            builder.AddBeltRoute(feedback, Mk4BeltLevelId, new int2(1, 0));
            builder.AddSplitter(origin + new int2(1, 1), new int2(1, 0));

            builder.AddBelt(origin + new int2(2, 1), new int2(1, 0), Mk4BeltLevelId);

            for (int x = 1; x <= 3; x++)
            {
                int2 direction = x == 3 ? new int2(0, -1) : new int2(1, 0);
                builder.AddBelt(origin + new int2(x, 2), direction, Mk4BeltLevelId);
            }

            builder.AddBelt(origin + new int2(1, 0), new int2(1, 0), Mk4BeltLevelId);
            builder.AddBelt(origin + new int2(2, 0), new int2(1, 0), Mk4BeltLevelId);
            builder.AddMerger(origin + new int2(3, 1), new int2(1, 0));
            builder.AddBelt(origin + new int2(3, 0), new int2(0, 1), Mk4BeltLevelId);
        }

        // A separate 62-belt loop keeps the total node count at exactly 512
        // for the default 5 x 5 module grid while preserving the requested
        // junction ratio.
        List<int2> beltLoop = new List<int2>(30 + loopTop * 2);
        for (int x = loopStartX; x <= loopStartX + 15; x++)
        {
            beltLoop.Add(new int2(x, 0));
        }
        for (int y = 1; y <= loopTop; y++)
        {
            beltLoop.Add(new int2(loopStartX + 15, y));
        }
        for (int x = loopStartX + 14; x >= loopStartX; x--)
        {
            beltLoop.Add(new int2(x, loopTop));
        }
        for (int y = loopTop - 1; y >= 1; y--)
        {
            beltLoop.Add(new int2(loopStartX, y));
        }
        builder.AddBeltRoute(beltLoop, Mk4BeltLevelId, new int2(0, -1));

        builder.AddInitialItemsByLoadPercent(loadPercent);
        return builder.Build(
            FactoryPerformanceScenario.MixedJunctions512,
            moduleSide + " x " + moduleSide +
            " mixed junction modules",
            moduleSide * moduleSide +
            " closed splitter/merger modules with deterministic " +
            loadPercent + "% occupancy.",
            moduleSide,
            "modules_per_side");
    }

    private static FactoryPerformanceScenarioDefinition CreateFullLoop(
        int size,
        int loadPercent)
    {
        if ((size & 1) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(size),
                size,
                "Full loop side must be even.");
        }

        Builder builder = new Builder(new int2(size, size));
        List<int2> route = new List<int2>(size * size);

        for (int x = 0; x < size; x++)
        {
            route.Add(new int2(x, 0));
        }

        for (int y = 1; y < size; y++)
        {
            if ((y & 1) != 0)
            {
                for (int x = size - 1; x >= 1; x--)
                {
                    route.Add(new int2(x, y));
                }
            }
            else
            {
                for (int x = 1; x < size; x++)
                {
                    route.Add(new int2(x, y));
                }
            }
        }

        for (int y = size - 1; y >= 1; y--)
        {
            route.Add(new int2(0, y));
        }

        builder.AddBeltRoute(route, Mk4BeltLevelId, new int2(0, -1));
        builder.AddInitialItemsByLoadPercent(loadPercent);
        return builder.Build(
            FactoryPerformanceScenario.Mk4FullLoop4096,
            size * size + " Mk4 belts - " + loadPercent +
            "% loaded full loop",
            "A Hamiltonian " + size + " x " + size +
            " loop for atomic high-density commits.",
            size,
            "side");
    }

    private static FactoryPerformanceScenarioDefinition CreateProducerConsumer(
        int laneCount)
    {
        Builder builder = new Builder(new int2(22, laneCount * 3 - 1));
        for (int lane = 0; lane < laneCount; lane++)
        {
            int y = lane * 3;
            builder.AddProcessor(
                BuildingKind.Miner,
                new int2(0, y),
                new int2(1, 0),
                MinerLevelId,
                "mine_iron");

            List<int2> oreRoute = new List<int2>(8);
            for (int x = 2; x <= 9; x++)
            {
                oreRoute.Add(new int2(x, y));
            }
            builder.AddBeltRoute(oreRoute, Mk4BeltLevelId, new int2(1, 0));

            builder.AddProcessor(
                BuildingKind.Furnace,
                new int2(10, y),
                new int2(1, 0),
                FurnaceLevelId,
                "smelt_iron");

            List<int2> ingotRoute = new List<int2>(8);
            for (int x = 12; x <= 19; x++)
            {
                ingotRoute.Add(new int2(x, y));
            }
            builder.AddBeltRoute(ingotRoute, Mk4BeltLevelId, new int2(1, 0));
            builder.AddStorage(new int2(20, y), new int2(1, 0));
        }

        return builder.Build(
            FactoryPerformanceScenario.ProducerConsumer,
            laneCount + " continuous producer-consumer lanes",
            laneCount +
            " miners feed furnaces and storage through " +
            laneCount * 16 + " Mk4 belts.",
            laneCount,
            "lanes");
    }

    private static FactoryPerformanceScenarioDefinition
        CreateContinuousBeltBuild(int beltCount)
    {
        Builder builder = new Builder(
            new int2(beltCount, beltCount * 2 - 1));
        return builder.Build(
            FactoryPerformanceScenario.ContinuousBeltBuild,
            beltCount + " Mk4 belt lines x " + beltCount +
            " cells continuous build",
            "Drag-builds " + beltCount + " Mk4 belt lines of " +
            beltCount + " cells with growing preview, then removes " +
            "them one line at a time.",
            beltCount,
            "belts");
    }

    private static int ResolveLoadPercent(int requested, int defaultValue)
    {
        int value = requested < 0 ? defaultValue : requested;
        if (value < 0 || value > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requested),
                requested,
                "Load percent must be between 0 and 100.");
        }

        return value;
    }

    private static int ResolveScale(
        int requested,
        int defaultValue,
        int minimum)
    {
        int value = requested > 0 ? requested : defaultValue;
        if (value < minimum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requested),
                requested,
                "Scale must be at least " + minimum + ".");
        }

        return value;
    }

    private static FactoryPerformanceScenarioDefinition
        CreateHalfLoadedSerpentine(int side)
    {
        Builder builder = new Builder(new int2(side, side));
        List<int2> route = BuildSerpentine(
            int2.zero,
            side,
            side,
            true);
        builder.AddBeltRoute(
            route,
            Mk4BeltLevelId,
            new int2(-1, 0));

        for (int i = 0; i < route.Count; i += 2)
        {
            builder.AddInitialItem(route[i]);
        }

        return builder.Build(
            FactoryPerformanceScenario.Mk4SerpentineHalfLoaded,
            side * side + " Mk4 belts - half loaded",
            "A " + side + " x " + side +
            " serpentine with " + (side * side + 1) / 2 +
            " alternating iron ore items.",
            side,
            "side");
    }

    private static FactoryPerformanceScenarioDefinition CreateF16Branches()
    {
        Builder builder = new Builder(new int2(96, 96));

        // The 32 x 32 input trunk ends directly below the first splitter.
        List<int2> inputTrunk = BuildSerpentine(
            int2.zero,
            32,
            32,
            false);
        builder.AddBeltRoute(
            inputTrunk,
            Mk4BeltLevelId,
            new int2(0, 1));
        for (int i = 0; i < inputTrunk.Count; i++)
        {
            builder.AddInitialItem(inputTrunk[i]);
        }

        int[] branchLengths = new int[16];
        for (int branchIndex = 0; branchIndex < 16; branchIndex++)
        {
            int y = 32 + branchIndex * 4;
            builder.AddSplitter(
                new int2(31, y),
                new int2(0, 1));

            // Each 256-cell arm is folded into a compact 64 x 4 snake.
            List<int2> branch = BuildSerpentine(
                new int2(32, y),
                64,
                4,
                true);
            builder.AddBeltRoute(
                branch,
                Mk4BeltLevelId,
                new int2(1, 0));
            branchLengths[branchIndex] = branch.Count;

            if (branchIndex < 15)
            {
                for (int offset = 1; offset <= 3; offset++)
                {
                    builder.AddBelt(
                        new int2(31, y + offset),
                        new int2(0, 1),
                        Mk4BeltLevelId);
                }
            }
        }

        return builder.Build(
            FactoryPerformanceScenario.Mk4F16Branches,
            "F-shaped Mk4 network - 16 x 256 branches",
            "A full 1024-cell input trunk feeds 16 compact 256-cell arms.",
            16,
            "branches",
            branchLengths);
    }

    private static FactoryPerformanceScenarioDefinition
        CreateBlockedSerpentine(int side)
    {
        Builder builder = new Builder(new int2(side + 3, side));
        List<int2> mainRoute = BuildSerpentine(
            new int2(3, 0),
            side,
            side,
            true);
        builder.AddBeltRoute(
            mainRoute,
            Mk4BeltLevelId,
            new int2(-1, 0));
        for (int i = 0; i < mainRoute.Count; i++)
        {
            builder.AddInitialItem(mainRoute[i]);
        }

        builder.AddBelt(
            new int2(2, side - 1),
            new int2(-1, 0),
            Mk1BeltLevelId);
        builder.AddStorage(
            new int2(0, side - 2),
            new int2(-1, 0));

        return builder.Build(
            FactoryPerformanceScenario.Mk4SerpentineBlockedByMk1,
            side * side + " full Mk4 belts -> Mk1 belt -> storage",
            "A full " + side + " x " + side +
            " Mk4 serpentine drains through one Mk1 belt into a storage.",
            side,
            "side");
    }

    private static List<int2> BuildSerpentine(
        int2 origin,
        int width,
        int height,
        bool firstRowMovesRight)
    {
        List<int2> result = new List<int2>(width * height);
        for (int y = 0; y < height; y++)
        {
            bool movesRight = (y & 1) == 0
                ? firstRowMovesRight
                : !firstRowMovesRight;
            if (movesRight)
            {
                for (int x = 0; x < width; x++)
                {
                    result.Add(origin + new int2(x, y));
                }
            }
            else
            {
                for (int x = width - 1; x >= 0; x--)
                {
                    result.Add(origin + new int2(x, y));
                }
            }
        }

        return result;
    }

    private sealed class Builder
    {
        private readonly int2 gridSize;
        private readonly List<FactoryPerformancePlacement> placements =
            new List<FactoryPerformancePlacement>();
        private readonly List<GridCell> initialItemCells = new List<GridCell>();
        private readonly HashSet<GridCell> occupiedCells = new HashSet<GridCell>();
        private readonly HashSet<GridCell> transportCells = new HashSet<GridCell>();
        private readonly List<GridCell> orderedTransportCells = new List<GridCell>();

        public Builder(int2 gridSize)
        {
            this.gridSize = gridSize;
        }

        public void AddBeltRoute(
            IReadOnlyList<int2> route,
            ushort levelId,
            int2 finalDirection)
        {
            if (route.Count == 0)
            {
                throw new ArgumentException("A belt route cannot be empty.");
            }

            for (int i = 0; i < route.Count; i++)
            {
                int2 direction = i + 1 < route.Count
                    ? route[i + 1] - route[i]
                    : finalDirection;
                if (math.csum(math.abs(direction)) != 1)
                {
                    throw new InvalidOperationException(
                        "Belt route contains non-adjacent cells at index " + i + ".");
                }

                AddBelt(route[i], direction, levelId);
            }
        }

        public void AddBelt(int2 cell, int2 direction, ushort levelId)
        {
            ReserveCell(cell, BuildingKind.Belt);
            AddTransportCell(cell);
            placements.Add(new FactoryPerformancePlacement(
                BuildingKind.Belt,
                new BuildingLevelId { Value = levelId },
                cell,
                EcsGridUtility.QuarterTurnsFromDirection(direction)));
        }

        public void AddSplitter(int2 cell, int2 direction)
        {
            ReserveCell(cell, BuildingKind.Splitter);
            AddTransportCell(cell);
            placements.Add(new FactoryPerformancePlacement(
                BuildingKind.Splitter,
                new BuildingLevelId { Value = SplitterLevelId },
                cell,
                EcsGridUtility.QuarterTurnsFromDirection(direction)));
        }

        public void AddMerger(int2 cell, int2 direction)
        {
            ReserveCell(cell, BuildingKind.Merger);
            AddTransportCell(cell);
            placements.Add(new FactoryPerformancePlacement(
                BuildingKind.Merger,
                new BuildingLevelId { Value = MergerLevelId },
                cell,
                EcsGridUtility.QuarterTurnsFromDirection(direction)));
        }

        public void AddProcessor(
            BuildingKind kind,
            int2 anchor,
            int2 direction,
            ushort levelId,
            string recipeKey)
        {
            if (kind != BuildingKind.Miner && kind != BuildingKind.Furnace)
            {
                throw new ArgumentException("Processor kind must be Miner or Furnace.");
            }

            ReserveSquareBuilding(anchor, kind);
            placements.Add(new FactoryPerformancePlacement(
                kind,
                new BuildingLevelId { Value = levelId },
                anchor,
                EcsGridUtility.QuarterTurnsFromDirection(direction),
                recipeKey));
        }

        public void AddStorage(int2 anchor, int2 inputDirection)
        {
            ReserveSquareBuilding(anchor, BuildingKind.Storage);

            placements.Add(new FactoryPerformancePlacement(
                BuildingKind.Storage,
                new BuildingLevelId { Value = StorageLevelId },
                anchor,
                EcsGridUtility.QuarterTurnsFromDirection(inputDirection)));
        }

        public void AddInitialItem(GridCell cell)
        {
            if (!transportCells.Contains(cell))
            {
                throw new InvalidOperationException(
                    "Initial items must be placed on transport cells: " + cell + ".");
            }

            initialItemCells.Add(cell);
        }

        public void AddInitialItemsByLoadPercent(int loadPercent)
        {
            int targetCount = (orderedTransportCells.Count * loadPercent + 50) / 100;
            for (int i = 0; i < orderedTransportCells.Count; i++)
            {
                long previous = (long)i * targetCount / orderedTransportCells.Count;
                long next = (long)(i + 1) * targetCount / orderedTransportCells.Count;
                if (next > previous)
                {
                    AddInitialItem(orderedTransportCells[i]);
                }
            }
        }

        public FactoryPerformanceScenarioDefinition Build(
            FactoryPerformanceScenario scenario,
            string displayName,
            string description,
            int scale,
            string scaleUnit,
            int[] branchLengths = null)
        {
            return new FactoryPerformanceScenarioDefinition(
                scenario,
                displayName,
                description,
                gridSize,
                placements.ToArray(),
                initialItemCells.ToArray(),
                branchLengths ?? Array.Empty<int>(),
                scale,
                scaleUnit);
        }

        private void ReserveCell(GridCell cell, BuildingKind kind)
        {
            if (cell.Level != 0 || cell.X < 0 || cell.Z < 0 ||
                cell.X >= gridSize.x || cell.Z >= gridSize.y)
            {
                throw new InvalidOperationException(
                    kind + " lies outside the performance grid at " + cell + ".");
            }

            if (!occupiedCells.Add(cell))
            {
                throw new InvalidOperationException(
                    "Performance layout overlaps at " + cell + ".");
            }
        }

        private void AddTransportCell(GridCell cell)
        {
            transportCells.Add(cell);
            orderedTransportCells.Add(cell);
        }

        private void ReserveSquareBuilding(int2 anchor, BuildingKind kind)
        {
            ReserveCell(anchor, kind);
            ReserveCell(anchor + new int2(1, 0), kind);
            ReserveCell(anchor + new int2(0, 1), kind);
            ReserveCell(anchor + new int2(1, 1), kind);
        }
    }
}
