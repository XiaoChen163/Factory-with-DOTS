using System;
using System.Collections.Generic;
using Unity.Mathematics;

public enum FactoryPerformanceScenario
{
    Mk4SerpentineHalfLoaded,
    Mk4F16Branches,
    Mk4SerpentineBlockedByMk1
}

public readonly struct FactoryPerformancePlacement
{
    public FactoryPerformancePlacement(
        BuildingKind kind,
        BuildingLevelId buildingLevel,
        int2 cell,
        byte quarterTurns)
    {
        Kind = kind;
        BuildingLevel = buildingLevel;
        Cell = cell;
        QuarterTurns = quarterTurns;
    }

    public BuildingKind Kind { get; }
    public BuildingLevelId BuildingLevel { get; }
    public int2 Cell { get; }
    public byte QuarterTurns { get; }
}

public sealed class FactoryPerformanceScenarioDefinition
{
    internal FactoryPerformanceScenarioDefinition(
        FactoryPerformanceScenario scenario,
        string displayName,
        string description,
        int2 gridSize,
        FactoryPerformancePlacement[] placements,
        int2[] initialItemCells,
        int[] branchLengths)
    {
        Scenario = scenario;
        DisplayName = displayName;
        Description = description;
        GridSize = gridSize;
        Placements = placements;
        InitialItemCells = initialItemCells;
        BranchLengths = branchLengths;

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
    public int2[] InitialItemCells { get; }
    public int[] BranchLengths { get; }
    public int BeltCount { get; }
    public int Mk4BeltCount { get; }
    public int Mk1BeltCount { get; }
    public int SplitterCount { get; }
    public int StorageCount { get; }
}

public static class FactoryPerformanceScenarioLayout
{
    public const ushort IronOreItemId = 1;
    public const ushort Mk1BeltLevelId = 1;
    public const ushort Mk4BeltLevelId = 4;
    public const ushort StorageLevelId = 7;
    public const ushort SplitterLevelId = 9;

    public static FactoryPerformanceScenarioDefinition Create(
        FactoryPerformanceScenario scenario)
    {
        switch (scenario)
        {
            case FactoryPerformanceScenario.Mk4SerpentineHalfLoaded:
                return CreateHalfLoadedSerpentine();
            case FactoryPerformanceScenario.Mk4F16Branches:
                return CreateF16Branches();
            case FactoryPerformanceScenario.Mk4SerpentineBlockedByMk1:
                return CreateBlockedSerpentine();
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(scenario),
                    scenario,
                    null);
        }
    }

    private static FactoryPerformanceScenarioDefinition
        CreateHalfLoadedSerpentine()
    {
        Builder builder = new Builder(new int2(64, 64));
        List<int2> route = BuildSerpentine(
            int2.zero,
            64,
            64,
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
            "4096 Mk4 belts - half loaded",
            "A 64 x 64 serpentine with 2048 alternating iron ore items.");
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
            branchLengths);
    }

    private static FactoryPerformanceScenarioDefinition
        CreateBlockedSerpentine()
    {
        Builder builder = new Builder(new int2(67, 64));
        List<int2> mainRoute = BuildSerpentine(
            new int2(3, 0),
            64,
            64,
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
            new int2(2, 63),
            new int2(-1, 0),
            Mk1BeltLevelId);
        builder.AddStorage(
            new int2(0, 62),
            new int2(-1, 0));

        return builder.Build(
            FactoryPerformanceScenario.Mk4SerpentineBlockedByMk1,
            "4096 full Mk4 belts -> Mk1 belt -> storage",
            "A full 64 x 64 Mk4 serpentine drains through one Mk1 belt into a storage.");
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
        private readonly List<int2> initialItemCells = new List<int2>();
        private readonly HashSet<int2> occupiedCells = new HashSet<int2>();
        private readonly HashSet<int2> beltCells = new HashSet<int2>();

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
            beltCells.Add(cell);
            placements.Add(new FactoryPerformancePlacement(
                BuildingKind.Belt,
                new BuildingLevelId { Value = levelId },
                cell,
                EcsGridUtility.QuarterTurnsFromDirection(direction)));
        }

        public void AddSplitter(int2 cell, int2 direction)
        {
            ReserveCell(cell, BuildingKind.Splitter);
            placements.Add(new FactoryPerformancePlacement(
                BuildingKind.Splitter,
                new BuildingLevelId { Value = SplitterLevelId },
                cell,
                EcsGridUtility.QuarterTurnsFromDirection(direction)));
        }

        public void AddStorage(int2 anchor, int2 inputDirection)
        {
            if (math.all(inputDirection == new int2(-1, 0)))
            {
                ReserveCell(anchor, BuildingKind.Storage);
                ReserveCell(anchor + new int2(1, 0), BuildingKind.Storage);
                ReserveCell(anchor + new int2(0, 1), BuildingKind.Storage);
                ReserveCell(anchor + new int2(1, 1), BuildingKind.Storage);
            }
            else
            {
                throw new NotSupportedException(
                    "The compact performance layout only needs a west-facing storage input.");
            }

            placements.Add(new FactoryPerformancePlacement(
                BuildingKind.Storage,
                new BuildingLevelId { Value = StorageLevelId },
                anchor,
                EcsGridUtility.QuarterTurnsFromDirection(inputDirection)));
        }

        public void AddInitialItem(int2 cell)
        {
            if (!beltCells.Contains(cell))
            {
                throw new InvalidOperationException(
                    "Initial items must be placed on belt cells: " + cell + ".");
            }

            initialItemCells.Add(cell);
        }

        public FactoryPerformanceScenarioDefinition Build(
            FactoryPerformanceScenario scenario,
            string displayName,
            string description,
            int[] branchLengths = null)
        {
            return new FactoryPerformanceScenarioDefinition(
                scenario,
                displayName,
                description,
                gridSize,
                placements.ToArray(),
                initialItemCells.ToArray(),
                branchLengths ?? Array.Empty<int>());
        }

        private void ReserveCell(int2 cell, BuildingKind kind)
        {
            if (cell.x < 0 || cell.y < 0 ||
                cell.x >= gridSize.x || cell.y >= gridSize.y)
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
    }
}
