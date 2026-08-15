using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
public partial class GridOccupancyIndexSystem : SystemBase
{
    private NativeParallelHashMap<GridCell, Entity> occupancy;
    private EntityQuery gridQuery;
    private EntityQuery placementQuery;
    private EntityQuery pendingAddQuery;
    private int lastPlacementOrderVersion = -1;
    private int lastPlacementCount = -1;
    private uint lastOccupancyRevision = uint.MaxValue;
    private bool reportedInvalidGridDefinition;
    private bool hasIncrementalChange;

    public NativeParallelHashMap<GridCell, Entity>.ReadOnly Occupancy =>
        occupancy.AsReadOnly();

    public bool IsReady { get; private set; }
    public int OccupiedCellCount =>
        occupancy.IsCreated ? occupancy.Count() : 0;
    public int ConflictCount { get; private set; }

    protected override void OnCreate()
    {
        occupancy = new NativeParallelHashMap<GridCell, Entity>(
            16,
            Allocator.Persistent);
        gridQuery = GetEntityQuery(
            ComponentType.ReadOnly<GridDefinition>());
        placementQuery = GetEntityQuery(
            ComponentType.ReadOnly<GridPlacement>(),
            ComponentType.ReadOnly<OccupiedCellOffset>());
        pendingAddQuery = GetEntityQuery(
            ComponentType.ReadOnly<PendingOccupancyAdd>(),
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
        uint occupancyRevision = GetOccupancyRevision();
        bool appliedPending = ApplyPendingAdds(grid);
        if (appliedPending || hasIncrementalChange)
        {
            lastPlacementCount = placementQuery.CalculateEntityCount();
            lastPlacementOrderVersion =
                EntityManager.GetComponentOrderVersion<GridPlacement>();
            lastOccupancyRevision = occupancyRevision;
            hasIncrementalChange = false;
            IsReady = true;
            return;
        }

        int placementCount = placementQuery.CalculateEntityCount();
        int placementOrderVersion =
            EntityManager.GetComponentOrderVersion<GridPlacement>();

        if (IsReady &&
            placementCount == lastPlacementCount &&
            placementOrderVersion == lastPlacementOrderVersion &&
            occupancyRevision == lastOccupancyRevision)
        {
            return;
        }

        Rebuild(grid, placementCount);
        lastPlacementCount = placementCount;
        lastPlacementOrderVersion = placementOrderVersion;
        lastOccupancyRevision = occupancyRevision;
        IsReady = true;
    }

    private uint GetOccupancyRevision()
    {
        Entity gridEntity = gridQuery.GetSingletonEntity();
        return EntityManager.HasComponent<BuildingOccupancyRevision>(gridEntity)
            ? EntityManager
                .GetComponentData<BuildingOccupancyRevision>(gridEntity)
                .Value
            : 0;
    }

    public void RemoveOccupant(GridCell cell, Entity entity)
    {
        if (!occupancy.IsCreated ||
            !occupancy.TryGetValue(cell, out Entity current) ||
            current != entity)
        {
            return;
        }

        occupancy.Remove(cell);
        hasIncrementalChange = true;
    }

    private bool ApplyPendingAdds(in GridDefinition grid)
    {
        if (pendingAddQuery.IsEmptyIgnoreFilter)
        {
            return false;
        }

        using NativeArray<Entity> entities =
            pendingAddQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<GridPlacement> placements =
            pendingAddQuery.ToComponentDataArray<GridPlacement>(
                Allocator.Temp);
        int requiredCapacity = occupancy.Count();
        for (int i = 0; i < entities.Length; i++)
        {
            requiredCapacity += EntityManager
                .GetBuffer<OccupiedCellOffset>(entities[i], true)
                .Length;
        }
        if (occupancy.Capacity < requiredCapacity)
        {
            occupancy.Capacity = math.max(16, requiredCapacity);
        }

        ConflictCount = 0;
        for (int i = 0; i < entities.Length; i++)
        {
            DynamicBuffer<OccupiedCellOffset> offsets =
                EntityManager.GetBuffer<OccupiedCellOffset>(
                    entities[i],
                    true);
            GridPlacement placement = placements[i];
            for (int cellIndex = 0;
                 cellIndex < offsets.Length;
                 cellIndex++)
            {
                GridCell cell = EcsGridUtility.GetBuildingCell(
                    placement,
                    offsets[cellIndex].Value);
                if (!HasBuildableSurface(grid, cell) ||
                    !occupancy.TryAdd(cell, entities[i]))
                {
                    ConflictCount++;
                }
            }
        }

        for (int i = 0; i < entities.Length; i++)
        {
            EntityManager.RemoveComponent<PendingOccupancyAdd>(
                entities[i]);
        }

        return true;
    }

    public bool TryGetOccupant(GridCell cell, out Entity entity)
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
                GridCell cell = EcsGridUtility.GetBuildingCell(
                    placement,
                    occupiedCells[cellIndex].Value);

                if (!HasBuildableSurface(grid, cell))
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
        lastOccupancyRevision = uint.MaxValue;
    }

    private bool HasBuildableSurface(in GridDefinition grid, GridCell cell)
    {
        SurfaceRegistrySystem registry =
            World.GetExistingSystemManaged<SurfaceRegistrySystem>();
        return registry != null && registry.IsReady
            ? registry.HasSurface(cell)
            : EcsGridUtility.Contains(grid, cell);
    }
}
