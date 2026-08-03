using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
public partial class GridOccupancyIndexSystem : SystemBase
{
    private NativeParallelHashMap<int2, Entity> occupancy;
    private EntityQuery gridQuery;
    private EntityQuery placementQuery;
    private int lastPlacementOrderVersion = -1;
    private int lastPlacementCount = -1;
    private uint lastGridRevision = uint.MaxValue;
    private bool reportedInvalidGridDefinition;

    public NativeParallelHashMap<int2, Entity>.ReadOnly Occupancy =>
        occupancy.AsReadOnly();

    public bool IsReady { get; private set; }
    public int OccupiedCellCount =>
        occupancy.IsCreated ? occupancy.Count() : 0;
    public int ConflictCount { get; private set; }

    protected override void OnCreate()
    {
        occupancy = new NativeParallelHashMap<int2, Entity>(
            16,
            Allocator.Persistent);
        gridQuery = GetEntityQuery(
            ComponentType.ReadOnly<GridDefinition>());
        placementQuery = GetEntityQuery(
            ComponentType.ReadOnly<GridPlacement>(),
            ComponentType.ReadOnly<OccupiedCellOffset>());
    }

    protected override void OnDestroy()
    {
        Dependency.Complete();
        if (occupancy.IsCreated)
        {
            occupancy.Dispose();
        }
    }

    protected override void OnUpdate()
    {
        int gridCount = gridQuery.CalculateEntityCount();
        if (gridCount != 1)
        {
            ClearIndex();
            if (gridCount > 1 && !reportedInvalidGridDefinition)
            {
                Debug.LogError(
                    "[ECS Grid] Exactly one GridDefinition is required, " +
                    "but " + gridCount + " were found.");
                reportedInvalidGridDefinition = true;
            }

            return;
        }

        reportedInvalidGridDefinition = false;
        GridDefinition grid =
            gridQuery.GetSingleton<GridDefinition>();
        int placementCount = placementQuery.CalculateEntityCount();
        int placementOrderVersion =
            EntityManager.GetComponentOrderVersion<GridPlacement>();

        if (IsReady &&
            placementCount == lastPlacementCount &&
            placementOrderVersion == lastPlacementOrderVersion &&
            grid.Revision == lastGridRevision)
        {
            return;
        }

        Rebuild(grid, placementCount);
        lastPlacementCount = placementCount;
        lastPlacementOrderVersion = placementOrderVersion;
        lastGridRevision = grid.Revision;
        IsReady = true;
    }

    public bool TryGetOccupant(int2 cell, out Entity entity)
    {
        if (!occupancy.IsCreated)
        {
            entity = Entity.Null;
            return false;
        }

        return occupancy.TryGetValue(cell, out entity);
    }

    private void Rebuild(
        in GridDefinition grid,
        int placementCount)
    {
        Dependency.Complete();

        using NativeArray<Entity> entities =
            placementQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<GridPlacement> placements =
            placementQuery.ToComponentDataArray<GridPlacement>(
                Allocator.Temp);

        int requiredCapacity = math.max(16, placementCount);
        for (int i = 0; i < entities.Length; i++)
        {
            requiredCapacity +=
                EntityManager
                    .GetBuffer<OccupiedCellOffset>(
                        entities[i],
                        true)
                    .Length - 1;
        }

        occupancy.Clear();
        if (occupancy.Capacity < requiredCapacity)
        {
            occupancy.Capacity = requiredCapacity;
        }

        ConflictCount = 0;
        for (int i = 0; i < entities.Length; i++)
        {
            Entity building = entities[i];
            GridPlacement placement = placements[i];
            DynamicBuffer<OccupiedCellOffset> occupiedCells =
                EntityManager.GetBuffer<OccupiedCellOffset>(
                    building,
                    true);

            for (int cellIndex = 0;
                 cellIndex < occupiedCells.Length;
                 cellIndex++)
            {
                int2 cell = EcsGridUtility.GetBuildingCell(
                    placement,
                    occupiedCells[cellIndex].Value);

                if (!EcsGridUtility.Contains(grid, cell))
                {
                    ConflictCount++;
                    Debug.LogError(
                        "[ECS Grid] " + building +
                        " occupies out-of-bounds cell " + cell + ".");
                    continue;
                }

                if (!occupancy.TryAdd(cell, building))
                {
                    ConflictCount++;
                    occupancy.TryGetValue(
                        cell,
                        out Entity existingBuilding);
                    Debug.LogError(
                        "[ECS Grid] Cell " + cell +
                        " is occupied by both " + existingBuilding +
                        " and " + building + ".");
                }
            }
        }
    }

    private void ClearIndex()
    {
        Dependency.Complete();
        if (occupancy.IsCreated)
        {
            occupancy.Clear();
        }

        IsReady = false;
        ConflictCount = 0;
        lastPlacementCount = -1;
        lastPlacementOrderVersion = -1;
        lastGridRevision = uint.MaxValue;
    }
}
