using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Rendering;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(GridOccupancyIndexSystem))]
[UpdateBefore(typeof(FixedStepSimulationSystemGroup))]
public partial class GridBuildCommandSystem : SystemBase
{
    public const int MaxFoundationAreaCells = 16384;
    private static readonly int2[] CardinalDirections =
    {
        new int2(1, 0),
        new int2(0, 1),
        new int2(-1, 0),
        new int2(0, -1)
    };

    private sealed class PlacementRecord
    {
        public Entity Entity;
        public bool PendingInstantiation;
        public VerticalBeltPlacementToken PlacementToken;
        public GridPlacement Placement;
        public BuildingDefinitionId BuildingDefinition;
        public GridCell[] OccupiedCells;
        public BuildingPort[] Ports;
    }

    /// <summary>
    /// A command batch overlays its not-yet-played-back additions/removals on
    /// the persistent occupancy index. Existing ECS records are materialized
    /// only when a command touches one of their cells.
    /// </summary>
    private sealed class BatchPlacementState
    {
        private readonly GridBuildCommandSystem owner;
        private GridOccupancyIndexSystem occupancy;
        private readonly Dictionary<Entity, PlacementRecord> recordsByEntity =
            new Dictionary<Entity, PlacementRecord>();
        private readonly Dictionary<GridCell, PlacementRecord> recordsByCell =
            new Dictionary<GridCell, PlacementRecord>();
        private readonly HashSet<Entity> removedEntities =
            new HashSet<Entity>();

        public BatchPlacementState(GridBuildCommandSystem owner)
        {
            this.owner = owner;
        }

        public void Begin(GridOccupancyIndexSystem occupancySystem)
        {
            occupancy = occupancySystem;
            recordsByEntity.Clear();
            recordsByCell.Clear();
            removedEntities.Clear();
        }

        public bool TryGet(GridCell cell, out PlacementRecord record)
        {
            if (recordsByCell.TryGetValue(cell, out record))
            {
                return record.Entity == Entity.Null ||
                       !removedEntities.Contains(record.Entity);
            }

            if (!(occupancy.TryGetOccupant(cell, out Entity entity) ||
                  owner.World.GetOrCreateSystemManaged<VerticalConnectorIndexSystem>().TryGet(cell, out entity)) ||
                removedEntities.Contains(entity) ||
                !owner.EntityManager.Exists(entity) ||
                !owner.EntityManager.HasComponent<GridPlacement>(entity) ||
                !owner.EntityManager.HasBuffer<OccupiedCellOffset>(entity) ||
                !owner.EntityManager.HasBuffer<BuildingPort>(entity))
            {
                record = null;
                return false;
            }

            if (!recordsByEntity.TryGetValue(entity, out record))
            {
                record = CreateRecord(
                    entity,
                    owner.EntityManager.GetComponentData<GridPlacement>(entity),
                    owner.EntityManager.GetBuffer<OccupiedCellOffset>(entity, true),
                    owner.EntityManager.GetBuffer<BuildingPort>(entity, true));
                if (owner.EntityManager.HasComponent<BuildingIdentity>(entity))
                    record.BuildingDefinition = owner.EntityManager.GetComponentData<BuildingIdentity>(entity).BuildingDefinition;
                if (owner.EntityManager.HasComponent<VerticalBeltPlacementToken>(entity))
                    record.PlacementToken = owner.EntityManager.GetComponentData<VerticalBeltPlacementToken>(entity);
                recordsByEntity.Add(entity, record);
                owner.RecordPlacementScan(1);
                owner.RecordTemporaryRecords(1);
                for (int i = 0; i < record.OccupiedCells.Length; i++)
                {
                    recordsByCell[record.OccupiedCells[i]] = record;
                }
            }

            return true;
        }

        public void Add(PlacementRecord record)
        {
            for (int i = 0; i < record.OccupiedCells.Length; i++)
            {
                recordsByCell.Add(record.OccupiedCells[i], record);
            }
        }

        public void Remove(PlacementRecord record)
        {
            if (record.Entity != Entity.Null)
            {
                removedEntities.Add(record.Entity);
            }
            for (int i = 0; i < record.OccupiedCells.Length; i++)
            {
                recordsByCell.Remove(record.OccupiedCells[i]);
            }
        }

        public void RollBack(PlacementRecord record)
        {
            for (int i = 0; i < record.OccupiedCells.Length; i++)
            {
                recordsByCell.Remove(record.OccupiedCells[i]);
            }
        }
    }

    private EntityQuery pendingVerticalRemovalQuery;
    private EntityQuery gridQuery;
    private EntityQuery catalogQuery;
    private EntityQuery itemPoolQuery;
    private EntityQuery playerQuery;
    private BuildingRuntimeIdAllocator runtimeIdAllocator;
    private BatchPlacementState batchPlacements;

    public ulong DiagnosticBatchCount { get; private set; }
    public int LastBatchPlacementScanCount { get; private set; }
    public int LastBatchTemporaryRecordCount { get; private set; }
    public int LastBatchPathValidationCellCount { get; private set; }
    public ulong TotalPlacementScanCount { get; private set; }
    public ulong TotalTemporaryRecordCount { get; private set; }
    public ulong TotalPathValidationCellCount { get; private set; }

    protected override void OnCreate()
    {
        pendingVerticalRemovalQuery = GetEntityQuery(ComponentType.ReadOnly<PendingVerticalRemoval>());
        gridQuery = GetEntityQuery(
            ComponentType.ReadWrite<WorldGridConfig>(),
            ComponentType.ReadWrite<GridBuildCommand>(),
            ComponentType.ReadWrite<GridBuildResult>());
        catalogQuery = GetEntityQuery(
            ComponentType.ReadOnly<BuildingPrefabCatalog>(),
            ComponentType.ReadOnly<BuildingVisualPrefabEntry>(),
            ComponentType.ReadOnly<FactoryDatabase>());
        itemPoolQuery = GetEntityQuery(
            ComponentType.ReadOnly<ItemPool>(),
            ComponentType.ReadOnly<ItemPoolEntry>());
        playerQuery = GetEntityQuery(
            ComponentType.ReadOnly<PlayerIdentity>(),
            ComponentType.ReadWrite<PlayerInventory>(),
            ComponentType.ReadWrite<InventorySlot>());
        batchPlacements = new BatchPlacementState(this);
    }

    protected override void OnUpdate()
    {
        if (gridQuery.CalculateEntityCount() != 1)
        {
            return;
        }

        Entity gridEntity = gridQuery.GetSingletonEntity();
        if (EntityManager.GetBuffer<GridBuildCommand>(gridEntity).IsEmpty)
        {
            return;
        }

        runtimeIdAllocator = EntityManager.HasComponent<BuildingRuntimeIdAllocator>(
            gridEntity)
            ? EntityManager.GetComponentData<BuildingRuntimeIdAllocator>(gridEntity)
            : CreateRuntimeIdAllocator();
        if (!EntityManager.HasComponent<BuildingRuntimeIdAllocator>(gridEntity))
        {
            EntityManager.AddComponentData(gridEntity, runtimeIdAllocator);
        }

        DynamicBuffer<GridBuildCommand> commands =
            EntityManager.GetBuffer<GridBuildCommand>(gridEntity);

        BeginDiagnosticBatch();

        DynamicBuffer<GridBuildResult> results =
            EntityManager.GetBuffer<GridBuildResult>(gridEntity);
        results.Clear();

        SurfaceRegistrySystem surfaceRegistry =
            World.GetExistingSystemManaged<SurfaceRegistrySystem>();
        RampRegistrySystem rampRegistry =
            World.GetOrCreateSystemManaged<RampRegistrySystem>();
        if (catalogQuery.CalculateEntityCount() != 1)
        {
            ProcessFoundationOnlyBatch(
                gridEntity,
                commands,
                surfaceRegistry);
            return;
        }

        GridOccupancyIndexSystem occupancySystem =
            World.GetExistingSystemManaged<
                GridOccupancyIndexSystem>();
        if (occupancySystem == null ||
            !occupancySystem.IsReady ||
            occupancySystem.ConflictCount > 0)
        {
            RejectAll(
                commands,
                results,
                GridBuildFailureReason.GridNotReady);
            commands.Clear();
            return;
        }

        Dependency.Complete();
        WorldGridConfig worldGrid =
            EntityManager.GetComponentData<WorldGridConfig>(
                gridEntity);
        BuildingPrefabCatalog catalog =
            catalogQuery.GetSingleton<BuildingPrefabCatalog>();
        DynamicBuffer<BuildingVisualPrefabEntry> visualPrefabs =
            EntityManager.GetBuffer<BuildingVisualPrefabEntry>(
                catalogQuery.GetSingletonEntity(), true);
        using NativeArray<BuildingVisualPrefabEntry> visualPrefabSnapshot =
            visualPrefabs.ToNativeArray(Allocator.Temp);
        using NativeArray<FoundationDefinitionMaterial> foundationMaterialSnapshot =
            EntityManager.HasBuffer<FoundationDefinitionMaterial>(
                catalogQuery.GetSingletonEntity())
                ? EntityManager.GetBuffer<FoundationDefinitionMaterial>(
                    catalogQuery.GetSingletonEntity(), true)
                    .ToNativeArray(Allocator.Temp)
                : new NativeArray<FoundationDefinitionMaterial>(0, Allocator.Temp);
        BlobAssetReference<FactoryDatabaseBlob> databaseReference =
            catalogQuery.GetSingleton<FactoryDatabase>().Value;
        ref FactoryDatabaseBlob database = ref databaseReference.Value;

        batchPlacements.Begin(occupancySystem);
        BatchPlacementState placements = batchPlacements;
        Dictionary<PlayerId, Entity> players = BuildPlayerSnapshot();

        EntityCommandBuffer ecb =
            new EntityCommandBuffer(Allocator.Temp);
        using NativeArray<GridBuildCommand> commandSnapshot =
            commands.ToNativeArray(Allocator.Temp);
        List<GridBuildResult> stagedResults =
            new List<GridBuildResult>(commandSnapshot.Length);
        bool buildingChanged = false;
        bool transportChanged = false;
        HashSet<GridCell> dirtyCells = new HashSet<GridCell>();

        for (int commandIndex = 0;
             commandIndex < commandSnapshot.Length;
             commandIndex++)
        {
            GridBuildCommand command = commandSnapshot[commandIndex];
            GridBuildFailureReason failureReason;
            bool success;
            int affectedCount = 0;
            bool changesBuildingOccupancy = true;

            switch (command.Type)
            {
                case GridBuildCommandType.PlaceRampFoundation:
                    changesBuildingOccupancy = false;
                    success = TryPlaceRampFoundation(
                        command,
                        worldGrid,
                        catalog,
                        rampRegistry,
                        surfaceRegistry,
                        placements,
                        out failureReason);
                    affectedCount = success ? 1 : 0;
                    break;

                case GridBuildCommandType.RemoveRampFoundation:
                    changesBuildingOccupancy = false;
                    success = TryRemoveRampFoundation(
                        command.StartCell,
                        rampRegistry,
                        out failureReason);
                    affectedCount = success ? 1 : 0;
                    break;

                case GridBuildCommandType.PlaceRampBelt:
                    changesBuildingOccupancy = false;
                    success = TryPlaceRampBelt(
                        command,
                        worldGrid,
                        catalog,
                        visualPrefabSnapshot,
                        ref database,
                        rampRegistry,
                        dirtyCells,
                        ref transportChanged,
                        ref ecb,
                        out failureReason);
                    affectedCount = success ? 1 : 0;
                    break;

                case GridBuildCommandType.PlaceRampBeltPath:
                    changesBuildingOccupancy = false;
                    success = TryPlaceRampBeltPath(
                        command,
                        worldGrid,
                        catalog,
                        visualPrefabSnapshot,
                        ref database,
                        rampRegistry,
                        dirtyCells,
                        ref transportChanged,
                        ref ecb,
                        out affectedCount,
                        out failureReason);
                    break;

                case GridBuildCommandType.RemoveRampBelt:
                    changesBuildingOccupancy = false;
                    success = TryRemoveRampBelt(
                        command.StartCell,
                        GetPlayer(command.Player, players),
                        ref database,
                        rampRegistry,
                        dirtyCells,
                        ref transportChanged,
                        ref ecb,
                        out failureReason);
                    affectedCount = success ? 1 : 0;
                    break;

                case GridBuildCommandType.PlaceFoundationArea:
                    changesBuildingOccupancy = false;
                    success = TryPlaceFoundationArea(
                        gridEntity,
                        command,
                        surfaceRegistry,
                        foundationMaterialSnapshot,
                        ref database,
                        out affectedCount,
                        out failureReason);
                    break;

                case GridBuildCommandType.PlaceFoundation:
                    changesBuildingOccupancy = false;
                    success = TryPlaceFoundation(
                        gridEntity,
                        command,
                        surfaceRegistry,
                        out failureReason);
                    affectedCount = success ? 1 : 0;
                    break;

                case GridBuildCommandType.RemoveFoundation:
                    changesBuildingOccupancy = false;
                    success = TryRemoveFoundation(
                        gridEntity,
                        command.StartCell,
                        surfaceRegistry,
                        occupancySystem,
                        placements,
                        out failureReason);
                    affectedCount = success ? 1 : 0;
                    break;

                case GridBuildCommandType.Remove:
                    success = TryRemove(
                        command.StartCell,
                        GetPlayer(command.Player, players),
                        ref database,
                        placements,
                        dirtyCells,
                        occupancySystem,
                        ref transportChanged,
                        ref ecb,
                        out affectedCount,
                        out failureReason);
                    break;

                case GridBuildCommandType.CancelVerticalBeltPlacement:
                    failureReason = GridBuildFailureReason.None;
                    affectedCount = 0;
                    success = true;
                    if (placements.TryGet(command.StartCell, out PlacementRecord provisional) &&
                        provisional.Placement.Kind == BuildingKind.VerticalBeltEntry &&
                        command.PlacementRequestId != 0 &&
                        provisional.PlacementToken.Player == command.Player &&
                        provisional.PlacementToken.RequestId == command.PlacementRequestId)
                        success = TryRemoveVerticalChain(provisional, GetPlayer(command.Player, players),
                            ref database, placements, dirtyCells, ref transportChanged, ref ecb,
                            out affectedCount, out failureReason);
                    break;

                case GridBuildCommandType.RemoveBeltLine:
                    success = TryRemoveBeltLine(
                        command.StartCell,
                        GetPlayer(command.Player, players),
                        ref database,
                        placements,
                        dirtyCells,
                        occupancySystem,
                        ref transportChanged,
                        ref ecb,
                        out affectedCount,
                        out failureReason);
                    break;

                case GridBuildCommandType.PlaceVerticalBeltPath:
                    success = TryPlaceVerticalBeltPath(command, worldGrid, catalog,
                        visualPrefabSnapshot, ref database, placements, dirtyCells,
                        ref transportChanged, ref ecb, out affectedCount, out failureReason);
                    break;

                case GridBuildCommandType.PlaceBeltPath:
                    success = TryPlaceBeltPath(
                        command,
                        worldGrid,
                        catalog,
                        visualPrefabSnapshot,
                        ref database,
                        placements,
                        dirtyCells,
                        ref transportChanged,
                        ref ecb,
                        out failureReason);
                    affectedCount = success ? 1 : 0;
                    break;

                default:
                    success = TryPlaceSingle(
                        command.BuildingDefinition,
                        command.StartCell,
                        command.QuarterTurns,
                        worldGrid,
                        catalog,
                        visualPrefabSnapshot,
                        ref database,
                        placements,
                        dirtyCells,
                        ref transportChanged,
                        ref ecb,
                        out failureReason, command.Player, command.PlacementRequestId);
                    affectedCount = success ? 1 : 0;
                    break;
            }

            buildingChanged |= success && changesBuildingOccupancy;
            stagedResults.Add(new GridBuildResult
            {
                RequestId = command.RequestId,
                Type = command.Type,
                Kind = command.Kind,
                BuildingDefinition = command.BuildingDefinition,
                Cell = command.Type == GridBuildCommandType.PlaceBeltPath ||
                       command.Type == GridBuildCommandType.PlaceRampBeltPath ||
                       command.Type == GridBuildCommandType.PlaceVerticalBeltPath
                    ? command.EndCell
                    : command.StartCell,
                Success = success ? (byte)1 : (byte)0,
                AffectedCount = affectedCount,
                FailureReason = failureReason
            });
        }

        ecb.Playback(EntityManager);
        ecb.Dispose();
        // LinkedEntityGroup buffers created in this ECB have their entity references fixed up only
        // after playback finishes. Destroy cancelled new roots now, before transport can observe them.
        if (!pendingVerticalRemovalQuery.IsEmptyIgnoreFilter)
        {
            using NativeArray<Entity> cancelled = pendingVerticalRemovalQuery.ToEntityArray(Allocator.Temp);
            EntityManager.DestroyEntity(cancelled);
        }
        EntityManager.GetBuffer<GridBuildCommand>(gridEntity).Clear();
        results = EntityManager.GetBuffer<GridBuildResult>(gridEntity);
        for (int i = 0; i < stagedResults.Count; i++)
            results.Add(stagedResults[i]);
        EntityManager.SetComponentData(gridEntity, runtimeIdAllocator);

        if (buildingChanged)
        {
            IncrementBuildingOccupancyRevision(gridEntity);
        }
        if (transportChanged)
        {
            IncrementTransportTopologyRevision(gridEntity);
            IncrementTransportVisualRevision(gridEntity);
        }
        if (dirtyCells.Count > 0)
        {
            if (!EntityManager.HasBuffer<BeltVisualDirtyCell>(
                    gridEntity))
            {
                EntityManager.AddBuffer<BeltVisualDirtyCell>(
                    gridEntity);
            }

            DynamicBuffer<BeltVisualDirtyCell> dirtyBuffer =
                EntityManager.GetBuffer<BeltVisualDirtyCell>(
                    gridEntity);
            foreach (GridCell cell in dirtyCells)
            {
                dirtyBuffer.Add(new BeltVisualDirtyCell
                {
                    Value = cell
                });
            }
        }
    }

    private void ProcessFoundationOnlyBatch(
        Entity gridEntity,
        DynamicBuffer<GridBuildCommand> commands,
        SurfaceRegistrySystem registry)
    {
        using NativeArray<GridBuildCommand> commandSnapshot =
            commands.ToNativeArray(Allocator.Temp);
        List<GridBuildResult> stagedResults =
            new List<GridBuildResult>(commandSnapshot.Length);
        GridOccupancyIndexSystem occupancy =
            World.GetExistingSystemManaged<GridOccupancyIndexSystem>();
        for (int i = 0; i < commandSnapshot.Length; i++)
        {
            GridBuildCommand command = commandSnapshot[i];
            bool success;
            GridBuildFailureReason failure;
            if (command.Type == GridBuildCommandType.PlaceFoundation)
                success = TryPlaceFoundation(gridEntity, command, registry, out failure);
            else if (command.Type == GridBuildCommandType.RemoveFoundation && occupancy != null)
                success = TryRemoveFoundation(
                    gridEntity, command.StartCell, registry, occupancy, null, out failure);
            else
            {
                success = false;
                failure = GridBuildFailureReason.MissingPrefab;
            }

            stagedResults.Add(new GridBuildResult
            {
                RequestId = command.RequestId,
                Type = command.Type,
                Kind = command.Kind,
                BuildingDefinition = command.BuildingDefinition,
                Cell = command.StartCell,
                Success = success ? (byte)1 : (byte)0,
                AffectedCount = success ? 1 : 0,
                FailureReason = failure
            });
        }
        EntityManager.GetBuffer<GridBuildCommand>(gridEntity).Clear();
        DynamicBuffer<GridBuildResult> results =
            EntityManager.GetBuffer<GridBuildResult>(gridEntity);
        for (int i = 0; i < stagedResults.Count; i++)
            results.Add(stagedResults[i]);
    }

    private bool TryPlaceFoundation(
        Entity grid,
        in GridBuildCommand command,
        SurfaceRegistrySystem registry,
        out GridBuildFailureReason failure)
    {
        GridCell cell = command.StartCell;
        if (registry == null || !registry.IsReady)
        {
            failure = GridBuildFailureReason.GridNotReady;
            return false;
        }
        if (cell.Level < 0)
        {
            failure = GridBuildFailureReason.FoundationUnsupported;
            return false;
        }
        if (registry.HasFoundationVoxel(cell))
        {
            failure = GridBuildFailureReason.FoundationAlreadyExists;
            return false;
        }
        RampRegistrySystem rampRegistry =
            World.GetExistingSystemManaged<RampRegistrySystem>();
        if (rampRegistry != null &&
            rampRegistry.ConflictsWithFoundationVoxel(cell))
        {
            failure = GridBuildFailureReason.RampOccupied;
            return false;
        }

        Entity entity = registry.AddFoundation(
            grid,
            cell,
            command.VisualMaterialId,
            SurfacePermission.All,
            true);
        if (entity == Entity.Null)
        {
            failure = GridBuildFailureReason.FoundationAlreadyExists;
            return false;
        }
        failure = GridBuildFailureReason.None;
        return true;
    }

    private bool TryPlaceFoundationArea(
        Entity grid,
        in GridBuildCommand command,
        SurfaceRegistrySystem registry,
        in NativeArray<FoundationDefinitionMaterial> materialMappings,
        ref FactoryDatabaseBlob database,
        out int affectedCount,
        out GridBuildFailureReason failure)
    {
        affectedCount = 0;
        if (registry == null || !registry.IsReady)
        {
            failure = GridBuildFailureReason.GridNotReady;
            return false;
        }
        if (command.StartCell.Level < 0 ||
            command.EndCell.Level < 0 ||
            command.StartCell.Level != command.EndCell.Level)
        {
            failure = GridBuildFailureReason.FoundationUnsupported;
            return false;
        }
        if (!FactoryDatabaseUtility.TryGetBuildingDefinition(
                ref database,
                command.BuildingDefinition,
                out FactoryBuildingDefinitionBlob building) ||
            building.Kind != BuildingKind.Foundation ||
            !TryResolveFoundationMaterial(
                command.BuildingDefinition,
                materialMappings,
                out ushort visualMaterialId))
        {
            failure = GridBuildFailureReason.InvalidFoundationDefinition;
            return false;
        }

        int minX = math.min(command.StartCell.X, command.EndCell.X);
        int maxX = math.max(command.StartCell.X, command.EndCell.X);
        int minZ = math.min(command.StartCell.Z, command.EndCell.Z);
        int maxZ = math.max(command.StartCell.Z, command.EndCell.Z);
        long width = (long)maxX - minX + 1L;
        long depth = (long)maxZ - minZ + 1L;
        long count = width * depth;
        if (width <= 0 || depth <= 0 ||
            count <= 0 || count > MaxFoundationAreaCells)
        {
            failure = GridBuildFailureReason.FoundationAreaTooLarge;
            return false;
        }

        List<GridCell> cells = new List<GridCell>((int)count);
        for (long z = minZ; z <= maxZ; z++)
        for (long x = minX; x <= maxX; x++)
        {
            GridCell cell = new GridCell(
                (int)x,
                command.StartCell.Level,
                (int)z);
            if (registry.HasFoundationVoxel(cell))
            {
                failure = GridBuildFailureReason.FoundationAlreadyExists;
                return false;
            }
            RampRegistrySystem rampRegistry =
                World.GetExistingSystemManaged<RampRegistrySystem>();
            if (rampRegistry != null &&
                rampRegistry.ConflictsWithFoundationVoxel(cell))
            {
                failure = GridBuildFailureReason.RampOccupied;
                return false;
            }
            cells.Add(cell);
        }

        affectedCount = registry.AddFoundationArea(
            grid,
            cells,
            visualMaterialId,
            SurfacePermission.All,
            true);
        failure = affectedCount == cells.Count
            ? GridBuildFailureReason.None
            : GridBuildFailureReason.GridNotReady;
        if (failure != GridBuildFailureReason.None)
            affectedCount = 0;
        return failure == GridBuildFailureReason.None;
    }

    private static bool TryResolveFoundationMaterial(
        BuildingDefinitionId level,
        in NativeArray<FoundationDefinitionMaterial> mappings,
        out ushort materialId)
    {
        for (int i = 0; i < mappings.Length; i++)
        {
            if (mappings[i].BuildingDefinition != level)
                continue;
            materialId = mappings[i].VisualMaterialId;
            return true;
        }
        materialId = 0;
        return false;
    }

    private bool TryRemoveFoundation(
        Entity grid,
        GridCell cell,
        SurfaceRegistrySystem registry,
        GridOccupancyIndexSystem occupancy,
        BatchPlacementState placements,
        out GridBuildFailureReason failure)
    {
        if (registry == null || !registry.IsReady)
        {
            failure = GridBuildFailureReason.GridNotReady;
            return false;
        }
        if (!registry.TryGetFoundation(cell, out Entity foundation))
        {
            failure = GridBuildFailureReason.NothingToRemove;
            return false;
        }
        if (occupancy == null || !occupancy.IsReady || occupancy.ConflictCount > 0)
        {
            failure = GridBuildFailureReason.GridNotReady;
            return false;
        }
        if (occupancy.TryGetOccupant(cell, out _) ||
            (placements != null && placements.TryGet(cell, out _)))
        {
            failure = GridBuildFailureReason.FoundationSupportsBuilding;
            return false;
        }
        if (!registry.RemoveFoundation(grid, cell, out foundation))
        {
            failure = GridBuildFailureReason.NothingToRemove;
            return false;
        }
        if (EntityManager.Exists(foundation)) EntityManager.DestroyEntity(foundation);
        failure = GridBuildFailureReason.None;
        return true;
    }

    private void BeginDiagnosticBatch()
    {
        DiagnosticBatchCount++;
        LastBatchPlacementScanCount = 0;
        LastBatchTemporaryRecordCount = 0;
        LastBatchPathValidationCellCount = 0;
    }

    private void RecordPlacementScan(int count)
    {
        LastBatchPlacementScanCount += count;
        TotalPlacementScanCount += (ulong)count;
    }

    private void RecordTemporaryRecords(int count)
    {
        LastBatchTemporaryRecordCount += count;
        TotalTemporaryRecordCount += (ulong)count;
    }

    private void RecordPathValidationCells(int count)
    {
        LastBatchPathValidationCellCount += count;
        TotalPathValidationCellCount += (ulong)count;
    }

    private void IncrementBuildingOccupancyRevision(Entity gridEntity)
    {
        bool exists = EntityManager.HasComponent<BuildingOccupancyRevision>(
            gridEntity);
        BuildingOccupancyRevision value = exists
            ? EntityManager.GetComponentData<BuildingOccupancyRevision>(gridEntity)
            : default;
        value.Value++;
        if (exists)
            EntityManager.SetComponentData(gridEntity, value);
        else
            EntityManager.AddComponentData(gridEntity, value);
    }

    private void IncrementTransportTopologyRevision(Entity gridEntity)
    {
        bool exists = EntityManager.HasComponent<TransportTopologyRevision>(
            gridEntity);
        TransportTopologyRevision value = exists
            ? EntityManager.GetComponentData<TransportTopologyRevision>(gridEntity)
            : default;
        value.Value++;
        if (exists)
            EntityManager.SetComponentData(gridEntity, value);
        else
            EntityManager.AddComponentData(gridEntity, value);
    }

    private void IncrementTransportVisualRevision(Entity gridEntity)
    {
        bool exists = EntityManager.HasComponent<TransportVisualRevision>(
            gridEntity);
        TransportVisualRevision value = exists
            ? EntityManager.GetComponentData<TransportVisualRevision>(gridEntity)
            : default;
        value.Value++;
        if (exists)
            EntityManager.SetComponentData(gridEntity, value);
        else
            EntityManager.AddComponentData(gridEntity, value);
    }

    private bool TryPlaceRampFoundation(
        in GridBuildCommand command,
        in WorldGridConfig worldGrid,
        in BuildingPrefabCatalog catalog,
        RampRegistrySystem rampRegistry,
        SurfaceRegistrySystem surfaceRegistry,
        BatchPlacementState placements,
        out GridBuildFailureReason failure)
    {
        if (rampRegistry == null ||
            !RampUtility.IsAllowedRise(command.RampRiseHeightUnits))
        {
            failure = GridBuildFailureReason.InvalidRampSlope;
            return false;
        }
        GridCell cell = command.StartCell;
        if (rampRegistry.ContainsCell(cell) ||
            placements.TryGet(cell, out _))
        {
            failure = GridBuildFailureReason.RampOccupied;
            return false;
        }
        if (!IsValidVisualPrefab(catalog.RampFoundationVisual))
        {
            failure = GridBuildFailureReason.MissingPrefab;
            return false;
        }

        int2 commandDirection = EcsGridUtility.Rotate(
            new int2(1, 0), command.QuarterTurns);
        int startUnits = command.RampStartHeightUnits;
        int signedRise = command.RampRiseHeightUnits;
        int2 uphill = signedRise > 0 ? commandDirection : -commandDirection;
        GridHeight low = new GridHeight(math.min(startUnits, startUnits + signedRise));
        GridHeight high = new GridHeight(math.max(startUnits, startUnits + signedRise));
        RampConnector connector = new RampConnector
        {
            Cell = cell,
            LowHeight = low,
            HighHeight = high,
            UphillDirection = uphill,
            VisualMaterialId = command.VisualMaterialId
        };
        if (surfaceRegistry != null &&
            surfaceRegistry.HasFoundationOverlappingRamp(connector))
        {
            failure = GridBuildFailureReason.RampOccupied;
            return false;
        }

        Entity entity = EntityManager.CreateEntity();
        EntityManager.AddComponentData(entity, connector);
        float3 position = EcsGridUtility.CellToWorldCenter(cell, worldGrid);
        position.y = low.ToWorldY(worldGrid);
        quaternion rotation = RampUtility.GetFoundationVisualRotation(uphill);
        float cellSize = math.max(math.EPSILON, worldGrid.CellSize);
        EntityManager.AddComponentData(entity,
            LocalTransform.FromPositionRotationScale(position, rotation, cellSize));
        EntityManager.AddComponentData(entity, new LocalToWorld
        {
            Value = float4x4.TRS(position, rotation, new float3(cellSize))
        });
        float riseWorld = high.ToWorldY(worldGrid) - low.ToWorldY(worldGrid);
        EntityManager.AddComponentData(entity, new PostTransformMatrix
        {
            Value = float4x4.Scale(new float3(1f, riseWorld / cellSize, 1f))
        });
        BlobAssetReference<Unity.Physics.Collider> rampCollider = World
            .GetOrCreateSystemManaged<RampPhysicsColliderCacheSystem>()
            .GetOrCreate(riseWorld / cellSize);
        EntityManager.AddComponentData(entity, new PhysicsCollider
        {
            Value = rampCollider
        });
        EntityManager.AddSharedComponentManaged(entity,
            new PhysicsWorldIndex { Value = 0 });
        DynamicBuffer<LinkedEntityGroup> linked =
            EntityManager.AddBuffer<LinkedEntityGroup>(entity);
        linked.Add(new LinkedEntityGroup { Value = entity });
        Entity visual = EntityManager.Instantiate(catalog.RampFoundationVisual);
        EntityManager.AddComponentData(visual, new Parent { Value = entity });
        linked = EntityManager.GetBuffer<LinkedEntityGroup>(entity);
        linked.Add(new LinkedEntityGroup { Value = visual });
        rampRegistry.Register(entity, connector);
        failure = GridBuildFailureReason.None;
        return true;
    }

    private bool TryRemoveRampFoundation(
        GridCell cell,
        RampRegistrySystem rampRegistry,
        out GridBuildFailureReason failure)
    {
        if (rampRegistry == null || !rampRegistry.TryGet(cell, out RampRegistrySystem.Record record))
        {
            failure = GridBuildFailureReason.RampMissing;
            return false;
        }
        if (record.BeltEntity != Entity.Null)
        {
            failure = GridBuildFailureReason.RampHasBelt;
            return false;
        }
        rampRegistry.Unregister(cell, record.ConnectorEntity);
        if (EntityManager.Exists(record.ConnectorEntity))
            EntityManager.DestroyEntity(record.ConnectorEntity);
        failure = GridBuildFailureReason.None;
        return true;
    }

    private bool TryPlaceRampBelt(
        in GridBuildCommand command,
        in WorldGridConfig worldGrid,
        in BuildingPrefabCatalog catalog,
        in NativeArray<BuildingVisualPrefabEntry> visualPrefabs,
        ref FactoryDatabaseBlob database,
        RampRegistrySystem rampRegistry,
        HashSet<GridCell> dirtyCells,
        ref bool transportChanged,
        ref EntityCommandBuffer ecb,
        out GridBuildFailureReason failure)
    {
        if (rampRegistry == null ||
            !rampRegistry.TryGet(command.StartCell, out RampRegistrySystem.Record record))
        {
            failure = GridBuildFailureReason.RampMissing;
            return false;
        }
        if (record.BeltEntity != Entity.Null)
        {
            failure = GridBuildFailureReason.RampOccupied;
            return false;
        }
        int2 travel = EcsGridUtility.Rotate(new int2(1, 0), command.QuarterTurns);
        if (!RampUtility.IsTravelDirectionAllowed(record.Connector, travel))
        {
            failure = GridBuildFailureReason.RampDirectionInvalid;
            return false;
        }
        if (!FactoryDatabaseUtility.TryGetBuildingDefinition(
                ref database, command.BuildingDefinition,
            out FactoryBuildingDefinitionBlob building) ||
            building.Kind != BuildingKind.Belt ||
            !FactoryDatabaseUtility.TryGetBeltStats(
                ref database,
                command.BuildingDefinition,
                out FactoryBuildingDefinitionBlob beltStats) ||
            !IsValidVisualPrefab(BuildingPrefabCatalogUtility.GetPrefab(
                visualPrefabs, command.BuildingDefinition)))
        {
            failure = GridBuildFailureReason.MissingPrefab;
            return false;
        }

        GridPlacement placement = new GridPlacement
        {
            AnchorCell = command.StartCell,
            FootprintSize = new int2(1),
            QuarterTurns = EcsGridUtility.QuarterTurnsFromDirection(travel),
            Kind = BuildingKind.Belt
        };
        PlacementRecord candidate = CreateRecordFromDefinition(building, placement, ref database);
        Entity prefab = BuildingPrefabCatalogUtility.GetPrefab(
            visualPrefabs, command.BuildingDefinition);
        Entity instance = Instantiate(
            prefab, building, placement, worldGrid,
            catalog, ref database, ref ecb, false);
        bool uphill = math.all(travel == record.Connector.UphillDirection);
        GridHeight entry = uphill ? record.Connector.LowHeight : record.Connector.HighHeight;
        GridHeight exit = uphill ? record.Connector.HighHeight : record.Connector.LowHeight;
        ecb.AddComponent(instance, new RampBelt
        {
            Connector = record.ConnectorEntity,
            TravelDirection = travel,
            EntryHeight = entry,
            ExitHeight = exit
        });
        ecb.SetComponent(instance, new BeltTopology
        {
            CellsPerSecond = beltStats.CellsPerSecond,
            Cell = command.StartCell,
            Direction = travel,
            ConnectionMode = RampUtility.GetConnectionMode(
                command.StartCell, entry, exit)
        });

        float3 center = RampUtility.GetSurfaceCenter(record.Connector, worldGrid);
        float rise = exit.ToWorldY(worldGrid) - entry.ToWorldY(worldGrid);
        float angle = math.atan2(rise, math.max(math.EPSILON, worldGrid.CellSize));
        quaternion rotation = math.mul(
            EcsGridUtility.RotationFromQuarterTurns(placement.QuarterTurns),
            quaternion.RotateZ(angle));
        float lengthScale = RampUtility.GetSlopeLength(record.Connector, worldGrid) /
                            math.max(math.EPSILON, worldGrid.CellSize);
        ecb.SetComponent(instance,
            LocalTransform.FromPositionRotationScale(center, rotation, 1f));
        ecb.SetComponent(instance, new LocalToWorld
        {
            Value = float4x4.TRS(center, rotation, new float3(1f))
        });
        ecb.AddComponent(instance, new PostTransformMatrix
        {
            Value = float4x4.Scale(new float3(lengthScale, 1f, 1f))
        });
        // The instantiated belt is still an EntityCommandBuffer placeholder.
        // RampRegistrySystem rebuilds on the next frame after playback, so do
        // not expose the deferred entity through the managed registry.
        MarkCellsDirty(candidate.OccupiedCells, dirtyCells);
        transportChanged = true;
        failure = GridBuildFailureReason.None;
        return true;
    }

    private bool TryPlaceRampBeltPath(
        in GridBuildCommand command,
        in WorldGridConfig worldGrid,
        in BuildingPrefabCatalog catalog,
        in NativeArray<BuildingVisualPrefabEntry> visualPrefabs,
        ref FactoryDatabaseBlob database,
        RampRegistrySystem rampRegistry,
        HashSet<GridCell> dirtyCells,
        ref bool transportChanged,
        ref EntityCommandBuffer ecb,
        out int affectedCount,
        out GridBuildFailureReason failure)
    {
        affectedCount = 0;
        if (rampRegistry == null)
        {
            failure = GridBuildFailureReason.RampPathMustStayOnRamp;
            return false;
        }

        int2 direction = EcsGridUtility.Rotate(
            new int2(1, 0), command.QuarterTurns);
        List<RampRegistrySystem.Record> path =
            new List<RampRegistrySystem.Record>();
        if (!rampRegistry.TryBuildBeltPath(
                command.StartCell,
                command.EndCell,
                direction,
                path,
                out failure))
        {
            return false;
        }
        for (int i = 0; i < path.Count; i++)
        {
            if (path[i].BeltEntity != Entity.Null)
            {
                failure = GridBuildFailureReason.RampOccupied;
                return false;
            }
        }

        if (!FactoryDatabaseUtility.TryGetBuildingDefinition(
                ref database,
                command.BuildingDefinition,
                out FactoryBuildingDefinitionBlob building) ||
            building.Kind != BuildingKind.Belt ||
            !FactoryDatabaseUtility.TryGetBeltStats(
                ref database,
                command.BuildingDefinition,
                out _) ||
            !IsValidVisualPrefab(BuildingPrefabCatalogUtility.GetPrefab(
                visualPrefabs,
                command.BuildingDefinition)))
        {
            failure = GridBuildFailureReason.MissingPrefab;
            return false;
        }

        for (int i = 0; i < path.Count; i++)
        {
            GridBuildCommand segment = command;
            segment.Type = GridBuildCommandType.PlaceRampBelt;
            segment.StartCell = path[i].Connector.Cell;
            segment.EndCell = segment.StartCell;
            if (!TryPlaceRampBelt(
                    segment,
                    worldGrid,
                    catalog,
                    visualPrefabs,
                    ref database,
                    rampRegistry,
                    dirtyCells,
                    ref transportChanged,
                    ref ecb,
                    out failure))
            {
                affectedCount = 0;
                return false;
            }
        }

        affectedCount = path.Count;
        failure = GridBuildFailureReason.None;
        return true;
    }

    private bool TryRemoveRampBelt(
        GridCell cell,
        Entity player,
        ref FactoryDatabaseBlob database,
        RampRegistrySystem rampRegistry,
        HashSet<GridCell> dirtyCells,
        ref bool transportChanged,
        ref EntityCommandBuffer ecb,
        out GridBuildFailureReason failure)
    {
        if (rampRegistry == null ||
            !rampRegistry.TryGet(cell, out RampRegistrySystem.Record ramp) ||
            ramp.BeltEntity == Entity.Null ||
            !EntityManager.Exists(ramp.BeltEntity))
        {
            failure = GridBuildFailureReason.RampMissing;
            return false;
        }
        Entity belt = ramp.BeltEntity;
        GridPlacement placement = EntityManager.GetComponentData<GridPlacement>(belt);
        PlacementRecord record = CreateRecord(
            belt,
            placement,
            EntityManager.GetBuffer<OccupiedCellOffset>(belt, true),
            EntityManager.GetBuffer<BuildingPort>(belt, true));
        MarkCellsDirty(record.OccupiedCells, dirtyCells);
        RecoverOwnedItems(belt, player, ref database, ref ecb);
        ecb.DestroyEntity(belt);
        rampRegistry.UnregisterBelt(cell, belt);
        transportChanged = true;
        failure = GridBuildFailureReason.None;
        return true;
    }

    private bool TryPlaceSingle(
        BuildingDefinitionId buildingDefinitionId,
        GridCell anchor,
        byte quarterTurns,
        in WorldGridConfig worldGrid,
        in BuildingPrefabCatalog catalog,
        in NativeArray<BuildingVisualPrefabEntry> visualPrefabs,
        ref FactoryDatabaseBlob database,
        BatchPlacementState placements,
        HashSet<GridCell> dirtyCells,
        ref bool transportChanged,
        ref EntityCommandBuffer ecb,
        out GridBuildFailureReason failureReason, PlayerId player, ulong placementRequestId)
    {
        if (!TryCreateStagedRecord(
                buildingDefinitionId,
                anchor,
                quarterTurns,
                ref database,
                out FactoryBuildingDefinitionBlob building,
                out PlacementRecord candidate))
        {
            failureReason =
                GridBuildFailureReason.MissingPrefab;
            return false;
        }
        if (building.Kind == BuildingKind.Foundation ||
            building.Kind == BuildingKind.RampFoundation)
        {
            failureReason = GridBuildFailureReason.InvalidFoundationDefinition;
            return false;
        }

        RecordTemporaryRecords(1);

        if (!CanPlace(
                candidate,
                placements,
                out failureReason))
        {
            return false;
        }

        placements.Add(candidate);
        Entity visualPrefab = BuildingPrefabCatalogUtility.GetPrefab(
            visualPrefabs,
            buildingDefinitionId);
        if (!IsValidVisualPrefab(visualPrefab))
        {
            placements.RollBack(candidate);
            failureReason = GridBuildFailureReason.MissingPrefab;
            return false;
        }
        Entity instance = Instantiate(
            visualPrefab,
            building,
            candidate.Placement,
            worldGrid,
            catalog,
            ref database,
            ref ecb);
        if (VerticalBeltUtility.IsVertical(building.Kind))
        {
            candidate.Entity = instance;
            candidate.PendingInstantiation = true;
            if (building.Kind == BuildingKind.VerticalBeltEntry && placementRequestId != 0)
            {
                candidate.PlacementToken = new VerticalBeltPlacementToken { Player = player, RequestId = placementRequestId };
                ecb.AddComponent(instance, candidate.PlacementToken);
            }
        }
        MarkCellsDirty(candidate.OccupiedCells, dirtyCells);
        transportChanged |= UsesTransportTopology(candidate);
        failureReason = GridBuildFailureReason.None;
        return true;
    }

    private bool TryPlaceVerticalBeltPath(
        in GridBuildCommand command, in WorldGridConfig worldGrid,
        in BuildingPrefabCatalog catalog, in NativeArray<BuildingVisualPrefabEntry> visualPrefabs,
        ref FactoryDatabaseBlob database, BatchPlacementState placements,
        HashSet<GridCell> dirtyCells, ref bool transportChanged, ref EntityCommandBuffer ecb,
        out int affectedCount, out GridBuildFailureReason failure)
    {
        affectedCount = 0;
        failure = GridBuildFailureReason.InvalidVerticalPath;
        long difference = (long)command.EndCell.Level - command.StartCell.Level;
        long distance = System.Math.Abs(difference);
        if (command.StartCell.X != command.EndCell.X || command.StartCell.Z != command.EndCell.Z ||
            command.EndCell.Level < 0 || distance < 2 || distance > VerticalBeltUtility.MaxPathNodes ||
            !placements.TryGet(command.StartCell, out PlacementRecord entry) ||
            entry.Placement.Kind != BuildingKind.VerticalBeltEntry || entry.BuildingDefinition != command.BuildingDefinition ||
            !VerticalBeltUtility.TryGetPathDefinitions(ref database, command.BuildingDefinition, out var segmentId, out var exitId))
            return false;

        int step = difference > 0 ? 1 : -1;
        List<PlacementRecord> staged = new List<PlacementRecord>((int)distance);
        var definitions = new List<(FactoryBuildingDefinitionBlob Building, Entity Prefab)>((int)distance);
        for (int i = 1; i <= distance; i++)
        {
            GridCell cell = command.StartCell;
            cell.Level += step * i;
            BuildingDefinitionId levelId = i == distance ? exitId : segmentId;
            byte turns = i == distance ? command.QuarterTurns : step > 0 ? (byte)0 : (byte)2;
            Entity prefab = BuildingPrefabCatalogUtility.GetPrefab(visualPrefabs, levelId);
            if (!IsValidVisualPrefab(prefab) || !TryCreateStagedRecord(levelId, cell, turns,
                ref database, out var building, out var record))
            {
                RollBackStaged(staged, placements);
                failure = GridBuildFailureReason.MissingPrefab;
                return false;
            }
            RecordTemporaryRecords(1);
            RecordPathValidationCells(1);
            if (!CanPlace(record, placements, out failure))
            {
                RollBackStaged(staged, placements);
                return false;
            }
            placements.Add(record);
            staged.Add(record);
            definitions.Add((building, prefab));
        }
        for (int i = 0; i < staged.Count; i++)
        {
            var definition = definitions[i];
            staged[i].Entity = Instantiate(definition.Prefab, definition.Building,
                staged[i].Placement, worldGrid, catalog, ref database, ref ecb);
            staged[i].PendingInstantiation = true;
            MarkCellsDirty(staged[i].OccupiedCells, dirtyCells);
        }
        affectedCount = staged.Count;
        transportChanged = true;
        failure = GridBuildFailureReason.None;
        return true;
    }

    private bool TryPlaceBeltPath(
        in GridBuildCommand command,
        in WorldGridConfig worldGrid,
        in BuildingPrefabCatalog catalog,
        in NativeArray<BuildingVisualPrefabEntry> visualPrefabs,
        ref FactoryDatabaseBlob database,
        BatchPlacementState placements,
        HashSet<GridCell> dirtyCells,
        ref bool transportChanged,
        ref EntityCommandBuffer ecb,
        out GridBuildFailureReason failureReason)
    {
        if (!FactoryDatabaseUtility.TryGetBuildingDefinition(
                ref database,
                command.BuildingDefinition,
                out FactoryBuildingDefinitionBlob beltBuilding) ||
            beltBuilding.Kind != BuildingKind.Belt ||
            !FactoryDatabaseUtility.TryGetBeltStats(
                ref database,
                command.BuildingDefinition,
                out _))
        {
            failureReason =
                GridBuildFailureReason.MissingPrefab;
            return false;
        }
        Entity beltPrefab = BuildingPrefabCatalogUtility.GetPrefab(
            visualPrefabs,
            command.BuildingDefinition);
        if (!IsValidVisualPrefab(beltPrefab))
        {
            failureReason = GridBuildFailureReason.MissingPrefab;
            return false;
        }

        List<BeltPathCell> path = EcsGridUtility.BuildBeltPath(
            command.StartCell,
            command.EndCell,
            command.HorizontalFirst != 0,
            EcsGridUtility.Rotate(
                new int2(1, 0),
                command.QuarterTurns));
        if (path.Count == 0)
        {
            failureReason =
                GridBuildFailureReason.InvalidPath;
            return false;
        }

        List<PlacementRecord> staged =
            new List<PlacementRecord>(path.Count);
        for (int i = 0; i < path.Count; i++)
        {
            BeltPathCell pathCell = path[i];
            GridPlacement placement = new GridPlacement
            {
                AnchorCell = pathCell.Cell,
                FootprintSize = new int2(
                    beltBuilding.FootprintWidth,
                    beltBuilding.FootprintHeight),
                QuarterTurns =
                    EcsGridUtility.QuarterTurnsFromDirection(
                        pathCell.Direction),
                Kind = BuildingKind.Belt
            };
            PlacementRecord candidate = CreateRecordFromDefinition(
                beltBuilding,
                placement,
                ref database);
            RecordTemporaryRecords(1);
            RecordPathValidationCells(candidate.OccupiedCells.Length);

            if (!CanPlace(
                    candidate,
                    placements,
                    out failureReason))
            {
                RollBackStaged(
                    staged,
                    placements);
                return false;
            }

            placements.Add(candidate);
            staged.Add(candidate);
        }

        for (int i = 0; i < staged.Count; i++)
        {
            MarkCellsDirty(staged[i].OccupiedCells, dirtyCells);
            Instantiate(
                beltPrefab,
                beltBuilding,
                staged[i].Placement,
                worldGrid,
                catalog,
                ref database,
                ref ecb);
        }

        transportChanged = true;
        failureReason = GridBuildFailureReason.None;
        return true;
    }

    private bool TryRemove(
        GridCell cell,
        Entity player,
        ref FactoryDatabaseBlob database,
        BatchPlacementState placements,
        HashSet<GridCell> dirtyCells,
        GridOccupancyIndexSystem occupancySystem,
        ref bool transportChanged,
        ref EntityCommandBuffer ecb,
        out int removedCount, out GridBuildFailureReason failureReason)
    {
        removedCount = 0;
        if (!placements.TryGet(cell, out PlacementRecord record) ||
            record.Entity == Entity.Null)
        {
            failureReason =
                GridBuildFailureReason.NothingToRemove;
            return false;
        }

        if (VerticalBeltUtility.IsVertical(record.Placement.Kind))
            return TryRemoveVerticalChain(record, player, ref database, placements, dirtyCells,
                ref transportChanged, ref ecb, out removedCount, out failureReason);
        placements.Remove(record);
        RemoveOccupancy(record, occupancySystem);
        MarkCellsDirty(record.OccupiedCells, dirtyCells);
        UnregisterRampBelt(record.Entity);
        RecoverOwnedItems(record.Entity, player, ref database, ref ecb);
        ecb.DestroyEntity(record.Entity);
        transportChanged |= UsesTransportTopology(record);
        removedCount = 1;
        failureReason = GridBuildFailureReason.None;
        return true;
    }

    private bool TryRemoveVerticalChain(PlacementRecord start, Entity player,
        ref FactoryDatabaseBlob database, BatchPlacementState placements,
        HashSet<GridCell> dirtyCells, ref bool transportChanged, ref EntityCommandBuffer ecb,
        out int removedCount, out GridBuildFailureReason failure)
    {
        // Only follow vertical transport connections. Adjacent exit/entry pairs belong to separate chains.
        List<PlacementRecord> chain = new List<PlacementRecord> { start };
        HashSet<GridCell> visited = new HashSet<GridCell> { start.Placement.AnchorCell };
        for (int i = 0; i < chain.Count; i++)
        {
            PlacementRecord current = chain[i];
            for (int step = -1; step <= 1; step += 2)
            {
                GridCell neighbor = current.Placement.AnchorCell;
                if (step < 0 && neighbor.Level == int.MinValue || step > 0 && neighbor.Level == int.MaxValue)
                    continue;
                neighbor.Level += step;
                if (visited.Contains(neighbor) || !placements.TryGet(neighbor, out PlacementRecord next) ||
                    !VerticalBeltUtility.IsVertical(next.Placement.Kind) ||
                    !(HasVerticalConnection(current.Placement, next.Placement) ||
                      HasVerticalConnection(next.Placement, current.Placement))) continue;
                visited.Add(neighbor);
                chain.Add(next);
            }
        }
        foreach (PlacementRecord record in chain)
        {
            placements.Remove(record);
            MarkCellsDirty(record.OccupiedCells, dirtyCells);
            if (record.PendingInstantiation)
                ecb.AddComponent<PendingVerticalRemoval>(record.Entity);
            else
            {
                RecoverOwnedItems(record.Entity, player, ref database, ref ecb);
                ecb.DestroyEntity(record.Entity);
            }
        }
        removedCount = chain.Count;
        transportChanged = true;
        failure = GridBuildFailureReason.None;
        return true;
    }

    private static bool HasVerticalConnection(in GridPlacement source, in GridPlacement target)
    {
        long difference = (long)target.AnchorCell.Level - source.AnchorCell.Level;
        if (source.AnchorCell.X != target.AnchorCell.X || source.AnchorCell.Z != target.AnchorCell.Z ||
            System.Math.Abs(difference) != 1 || source.Kind == BuildingKind.VerticalBeltExit ||
            target.Kind == BuildingKind.VerticalBeltEntry) return false;
        int step = difference > 0 ? 1 : -1;
        if (source.Kind == BuildingKind.VerticalBeltSegment &&
            VerticalBeltUtility.Step(source.QuarterTurns) != step) return false;
        return target.Kind == BuildingKind.VerticalBeltExit && source.Kind == BuildingKind.VerticalBeltSegment ||
            target.Kind == BuildingKind.VerticalBeltSegment && VerticalBeltUtility.Step(target.QuarterTurns) == step;
    }

    private bool TryRemoveBeltLine(
        GridCell cell,
        Entity player,
        ref FactoryDatabaseBlob database,
        BatchPlacementState placements,
        HashSet<GridCell> dirtyCells,
        GridOccupancyIndexSystem occupancySystem,
        ref bool transportChanged,
        ref EntityCommandBuffer ecb,
        out int removedCount,
        out GridBuildFailureReason failureReason)
    {
        removedCount = 0;
        if (!placements.TryGet(cell, out PlacementRecord start) ||
            start.Entity == Entity.Null)
        {
            failureReason =
                GridBuildFailureReason.NothingToRemove;
            return false;
        }

        if (VerticalBeltUtility.IsVertical(start.Placement.Kind))
            return TryRemoveVerticalChain(start, player, ref database, placements, dirtyCells,
                ref transportChanged, ref ecb, out removedCount, out failureReason);

        if (start.Placement.Kind != BuildingKind.Belt)
        {
            failureReason =
                GridBuildFailureReason.TargetIsNotBelt;
            return false;
        }

        Queue<PlacementRecord> pending =
            new Queue<PlacementRecord>();
        HashSet<PlacementRecord> connectedBelts =
            new HashSet<PlacementRecord>();
        pending.Enqueue(start);
        connectedBelts.Add(start);

        while (pending.Count > 0)
        {
            PlacementRecord current = pending.Dequeue();
            TryEnqueueOutputBelt(
                current,
                placements,
                connectedBelts,
                pending);
            TryEnqueueIncomingBelts(
                current,
                placements,
                connectedBelts,
                pending);
        }

        foreach (PlacementRecord belt in connectedBelts)
        {
            placements.Remove(belt);
            RemoveOccupancy(belt, occupancySystem);
            MarkCellsDirty(belt.OccupiedCells, dirtyCells);
            UnregisterRampBelt(belt.Entity);
            RecoverOwnedItems(belt.Entity, player, ref database, ref ecb);
            ecb.DestroyEntity(belt.Entity);
            removedCount++;
        }

        transportChanged |= removedCount > 0;
        failureReason = GridBuildFailureReason.None;
        return removedCount > 0;
    }

    private static void RemoveOccupancy(
        PlacementRecord record,
        GridOccupancyIndexSystem occupancySystem)
    {
        for (int i = 0; i < record.OccupiedCells.Length; i++)
        {
            occupancySystem.RemoveOccupant(
                record.OccupiedCells[i],
                record.Entity);
        }
    }

    private static void MarkCellsDirty(
        GridCell[] cells,
        HashSet<GridCell> dirtyCells)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            GridCell cell = cells[i];
            dirtyCells.Add(cell);
            dirtyCells.Add(cell + new int2(1, 0));
            dirtyCells.Add(cell + new int2(-1, 0));
            dirtyCells.Add(cell + new int2(0, 1));
            dirtyCells.Add(cell + new int2(0, -1));
        }
    }

    private static void TryEnqueueOutputBelt(
        PlacementRecord source,
        BatchPlacementState placements,
        HashSet<PlacementRecord> connectedBelts,
        Queue<PlacementRecord> pending)
    {
        if (!TryGetBeltOutputCell(source, out GridCell outputCell) ||
            !placements.TryGet(outputCell, out PlacementRecord target) ||
            target.Placement.Kind != BuildingKind.Belt ||
            !connectedBelts.Add(target))
        {
            return;
        }

        pending.Enqueue(target);
    }

    private static void TryEnqueueIncomingBelts(
        PlacementRecord target,
        BatchPlacementState placements,
        HashSet<PlacementRecord> connectedBelts,
        Queue<PlacementRecord> pending)
    {
        GridCell targetCell = target.Placement.AnchorCell;
        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            GridCell sourceCell =
                targetCell - CardinalDirections[i];
            if (!placements.TryGet(sourceCell, out PlacementRecord source) ||
                source.Placement.Kind != BuildingKind.Belt ||
                !TryGetBeltOutputCell(
                    source,
                    out GridCell outputCell) ||
                outputCell != targetCell ||
                !connectedBelts.Add(source))
            {
                continue;
            }

            pending.Enqueue(source);
        }
    }

    private static bool TryGetBeltOutputCell(
        PlacementRecord belt,
        out GridCell outputCell)
    {
        for (int i = 0; i < belt.Ports.Length; i++)
        {
            BuildingPort port = belt.Ports[i];
            if (port.Type != BuildingPortType.Output)
            {
                continue;
            }

            outputCell = EcsGridUtility.GetBuildingCell(
                belt.Placement,
                port.CellOffset);
            return true;
        }

        outputCell = default;
        return false;
    }

    private bool CanPlace(
        PlacementRecord candidate,
        BatchPlacementState placements,
        out GridBuildFailureReason failureReason)
    {
        for (int i = 0;
             i < candidate.OccupiedCells.Length;
             i++)
        {
            GridCell cell = candidate.OccupiedCells[i];
            RampRegistrySystem rampRegistry =
                World.GetExistingSystemManaged<RampRegistrySystem>();
            if (rampRegistry != null && rampRegistry.ContainsCell(cell))
            {
                failureReason = GridBuildFailureReason.CellOccupied;
                return false;
            }
            SurfaceRegistrySystem registry =
                World.GetExistingSystemManaged<SurfaceRegistrySystem>();
            if (cell.Level != candidate.Placement.AnchorCell.Level)
            {
                failureReason = GridBuildFailureReason.OutsideGrid;
                return false;
            }
            bool hasPermission = VerticalBeltUtility.IsVertical(candidate.Placement.Kind)
                ? cell.Level >= 0
                : registry != null && registry.IsReady
                && registry.AllowsFlat(
                    cell,
                    candidate.Placement.Kind == BuildingKind.Belt
                        ? SurfacePermission.Belts
                        : SurfacePermission.Buildings);
            if (!hasPermission)
            {
                failureReason =
                    GridBuildFailureReason.OutsideGrid;
                return false;
            }

            if (placements.TryGet(cell, out _))
            {
                failureReason =
                    GridBuildFailureReason.CellOccupied;
                return false;
            }
        }

        if (VerticalBeltUtility.IsVertical(candidate.Placement.Kind) &&
            !ValidateVerticalPlacement(candidate, placements, out failureReason))
            return false;

        int2 candidateDirection = EcsGridUtility.Rotate(
            new int2(1, 0),
            candidate.Placement.QuarterTurns);

        if (candidate.Placement.Kind == BuildingKind.Splitter)
        {
            int incomingCount = CountOutputsTo(
                candidate.Placement.AnchorCell,
                placements);
            if (incomingCount > 1)
            {
                failureReason =
                    GridBuildFailureReason.SplitterInputConflict;
                return false;
            }

            if (incomingCount == 1 &&
                (!TryGetOnlyIncomingDirection(
                     candidate.Placement.AnchorCell,
                     placements,
                     out int2 incomingDirection) ||
                 !math.all(
                     incomingDirection == candidateDirection)))
            {
                failureReason =
                    GridBuildFailureReason
                        .SplitterDirectionConflict;
                return false;
            }
        }

        if (candidate.Placement.Kind == BuildingKind.Merger)
        {
            List<int2> incomingDirections =
                GetIncomingDirections(
                    candidate.Placement.AnchorCell,
                    placements);
            for (int i = 0;
                 i < incomingDirections.Count;
                 i++)
            {
                if (math.all(
                        incomingDirections[i] ==
                        -candidateDirection))
                {
                    failureReason =
                        GridBuildFailureReason
                            .MergerOutputFaceConflict;
                    return false;
                }
            }
        }

        List<(GridCell Cell, int2 Direction)> outputs =
            GetOutputs(candidate);
        for (int i = 0; i < outputs.Count; i++)
        {
            (GridCell outputCell, int2 outputDirection) =
                outputs[i];
            if (!placements.TryGet(
                    outputCell,
                    out PlacementRecord target))
            {
                continue;
            }

            int2 targetDirection = EcsGridUtility.Rotate(
                new int2(1, 0),
                target.Placement.QuarterTurns);
            if (target.Placement.Kind == BuildingKind.Splitter)
            {
                if (CountOutputsTo(outputCell, placements) >= 1 ||
                    !math.all(
                        outputDirection == targetDirection))
                {
                    failureReason =
                        GridBuildFailureReason
                            .SplitterInputConflict;
                    return false;
                }
            }
            else if (
                target.Placement.Kind == BuildingKind.Merger &&
                math.all(
                    outputDirection == -targetDirection))
            {
                failureReason =
                    GridBuildFailureReason
                        .MergerOutputFaceConflict;
                return false;
            }
        }

        failureReason = GridBuildFailureReason.None;
        return true;
    }

    private bool ValidateVerticalPlacement(PlacementRecord candidate,
        BatchPlacementState placements, out GridBuildFailureReason failure) =>
        VerticalBeltUtility.Validate(candidate.Placement.Kind,
            candidate.Placement.AnchorCell, candidate.Placement.QuarterTurns,
            cell => placements.TryGet(cell, out PlacementRecord record)
                ? record.Placement : (GridPlacement?)null, out failure);

    private bool TryCreateStagedRecord(
        BuildingDefinitionId levelId,
        GridCell anchor,
        byte quarterTurns,
        ref FactoryDatabaseBlob database,
        out FactoryBuildingDefinitionBlob building,
        out PlacementRecord record)
    {
        if (!FactoryDatabaseUtility.TryGetBuildingDefinition(
                ref database,
                levelId,
                out building) ||
            !HasRequiredStats(building))
        {
            record = null;
            return false;
        }

        GridPlacement placement = new GridPlacement
        {
            AnchorCell = anchor,
            FootprintSize = new int2(
                building.FootprintWidth,
                building.FootprintHeight),
            QuarterTurns = (byte)(quarterTurns % 4),
            Kind = building.Kind
        };
        record = CreateRecordFromDefinition(building, placement, ref database);
        record.BuildingDefinition = levelId;
        return true;
    }

    private static bool HasRequiredStats(in FactoryBuildingDefinitionBlob building)
    {
        switch (building.Kind)
        {
            case BuildingKind.Belt:
            case BuildingKind.VerticalBeltEntry:
            case BuildingKind.VerticalBeltExit:
            case BuildingKind.VerticalBeltSegment:
                return building.BeltTier > 0 && building.CellsPerSecond > 0f &&
                    !float.IsNaN(building.CellsPerSecond) && !float.IsInfinity(building.CellsPerSecond);
            case BuildingKind.Processor:
                return building.MachineType.IsValid;
            case BuildingKind.Storage:
                return building.Capacity > 0 && building.SlotCount > 0;
            default:
                return true;
        }
    }

    private bool IsValidVisualPrefab(Entity prefab)
    {
        return prefab != Entity.Null &&
               EntityManager.Exists(prefab) &&
               EntityManager.HasComponent<Prefab>(prefab) &&
               EntityManager.HasComponent<LocalTransform>(prefab);
    }

    private static PlacementRecord CreateRecordFromDefinition(
        in FactoryBuildingDefinitionBlob building,
        in GridPlacement placement,
        ref FactoryDatabaseBlob database)
    {
        int occupiedCount = building.OccupiedCellCount;
        GridCell[] occupiedCells = new GridCell[occupiedCount];
        for (int i = 0; i < occupiedCount; i++)
        {
            occupiedCells[i] = EcsGridUtility.GetBuildingCell(
                placement,
                database.BuildingOccupiedCells[building.OccupiedCellStart + i]);
        }

        BuildingPort[] ports = new BuildingPort[building.PortCount];
        for (int i = 0; i < building.PortCount; i++)
        {
            FactoryBuildingPortBlob source =
                database.BuildingPorts[building.PortStart + i];
            ports[i] = new BuildingPort
            {
                CellOffset = source.CellOffset,
                Direction = source.Direction,
                Type = source.Type,
                Index = source.Index,
                Number = source.Number,
                OutputSlot = source.OutputSlot
            };
        }

        return new PlacementRecord
        {
            Entity = Entity.Null,
            Placement = placement,
            OccupiedCells = occupiedCells,
            Ports = ports
        };
    }

    private static PlacementRecord CreateRecord(
        Entity entity,
        GridPlacement placement,
        DynamicBuffer<OccupiedCellOffset> occupiedOffsets,
        DynamicBuffer<BuildingPort> ports)
    {
        GridCell[] occupiedCells =
            new GridCell[occupiedOffsets.Length];
        for (int i = 0; i < occupiedOffsets.Length; i++)
        {
            occupiedCells[i] = EcsGridUtility.GetBuildingCell(
                placement,
                occupiedOffsets[i].Value);
        }

        BuildingPort[] portArray =
            new BuildingPort[ports.Length];
        for (int i = 0; i < ports.Length; i++)
        {
            portArray[i] = ports[i];
        }

        return new PlacementRecord
        {
            Entity = entity,
            Placement = placement,
            OccupiedCells = occupiedCells,
            Ports = portArray
        };
    }

    private Entity Instantiate(
        Entity visualPrefab,
        in FactoryBuildingDefinitionBlob building,
        in GridPlacement placement,
        in WorldGridConfig worldGrid,
        in BuildingPrefabCatalog catalog,
        ref FactoryDatabaseBlob database,
        ref EntityCommandBuffer ecb,
        bool registerPlanarOccupancy = true)
    {
        Entity instance = ecb.CreateEntity();
        if (registerPlanarOccupancy && !VerticalBeltUtility.IsVertical(placement.Kind))
            ecb.AddComponent(instance, new PendingOccupancyAdd());
        ecb.AddComponent(instance, placement);
        ecb.AddComponent(instance, new BuildingIdentity
        {
            BuildingDefinition = building.Id
        });
        DynamicBuffer<OccupiedCellOffset> occupied =
            ecb.AddBuffer<OccupiedCellOffset>(instance);
        for (int i = 0; i < building.OccupiedCellCount; i++)
            occupied.Add(new OccupiedCellOffset
            {
                Value = database.BuildingOccupiedCells[building.OccupiedCellStart + i]
            });
        DynamicBuffer<BuildingPort> ports = ecb.AddBuffer<BuildingPort>(instance);
        for (int i = 0; i < building.PortCount; i++)
        {
            FactoryBuildingPortBlob source =
                database.BuildingPorts[building.PortStart + i];
            ports.Add(new BuildingPort
            {
                CellOffset = source.CellOffset,
                Direction = source.Direction,
                Type = source.Type,
                Index = source.Index,
                Number = source.Number,
                OutputSlot = source.OutputSlot
            });
        }

        if (VerticalBeltUtility.IsVertical(placement.Kind))
        {
            ecb.AddComponent(instance, new PhysicsCollider
            {
                Value = World.GetOrCreateSystemManaged<VerticalBeltColliderCacheSystem>()
                    .GetOrCreate(worldGrid, placement.Kind == BuildingKind.VerticalBeltSegment)
            });
            ecb.AddSharedComponent(instance, new PhysicsWorldIndex { Value = 0 });
        }
        int2 direction = EcsGridUtility.Rotate(
            new int2(1, 0),
            placement.QuarterTurns);
        float3 center = EcsGridUtility.CellToWorldCenter(
            placement.AnchorCell,
            worldGrid);
        float2 visualOffset = EcsGridUtility.GetVisualCenterOffset(
            placement.FootprintSize, placement.QuarterTurns) * worldGrid.CellSize;
        center.x += visualOffset.x;
        center.z += visualOffset.y;
        ecb.AddComponent(instance, LocalTransform.FromPositionRotationScale(
            center,
            EcsGridUtility.RotationFromQuarterTurns(placement.QuarterTurns),
            1f));
        // Runtime-created transform roots are not completed by baking. ParentSystem
        // and LocalToWorldSystem require this component to propagate the root
        // transform to the visual prefab attached below.
        ecb.AddComponent(instance, new LocalToWorld
        {
            Value = float4x4.TRS(
                center,
                EcsGridUtility.RotationFromQuarterTurns(
                    placement.QuarterTurns),
                new float3(1f))
        });

        AddLogicComponents(
            instance,
            building,
            placement,
            direction,
            ref ecb);

        DynamicBuffer<LinkedEntityGroup> linked =
            ecb.AddBuffer<LinkedEntityGroup>(instance);
        linked.Add(new LinkedEntityGroup { Value = instance });
        Entity visual = ecb.Instantiate(visualPrefab);
        if (placement.Kind == BuildingKind.VerticalBeltSegment &&
            VerticalBeltUtility.Step(placement.QuarterTurns) < 0)
        {
            LocalTransform visualTransform = EntityManager.GetComponentData<LocalTransform>(visualPrefab);
            visualTransform.Position = new float3(0f, worldGrid.LayerHeight, 0f);
            visualTransform.Rotation = quaternion.RotateX(math.PI);
            ecb.SetComponent(visual, visualTransform);
        }
        if (VerticalBeltUtility.IsVertical(placement.Kind))
        {
            PostTransformMatrix dimensions = new PostTransformMatrix
            {
                Value = float4x4.Scale(new float3(worldGrid.CellSize, worldGrid.LayerHeight, worldGrid.CellSize))
            };
            if (EntityManager.HasComponent<PostTransformMatrix>(visualPrefab))
            {
                dimensions.Value = math.mul(dimensions.Value,
                    EntityManager.GetComponentData<PostTransformMatrix>(visualPrefab).Value);
                ecb.SetComponent(visual, dimensions);
            }
            else ecb.AddComponent(visual, dimensions);
        }
        ecb.AddComponent(visual, new Parent { Value = instance });
        ecb.AddComponent(instance, new BuildingVisualReference
        {
            Value = visual
        });
        linked.Add(new LinkedEntityGroup { Value = visual });

        InstantiatePortVisuals(
            instance,
            ports,
            placement,
            worldGrid,
            catalog,
            ref ecb);
        return instance;
    }

    private void UnregisterRampBelt(Entity entity)
    {
        if (!EntityManager.Exists(entity) ||
            !EntityManager.HasComponent<RampBelt>(entity))
            return;
        RampBelt belt = EntityManager.GetComponentData<RampBelt>(entity);
        if (!EntityManager.Exists(belt.Connector) ||
            !EntityManager.HasComponent<RampConnector>(belt.Connector))
            return;
        GridCell cell = EntityManager
            .GetComponentData<RampConnector>(belt.Connector).Cell;
        World.GetExistingSystemManaged<RampRegistrySystem>()
            ?.UnregisterBelt(cell, entity);
    }

    private void AddLogicComponents(
        Entity instance,
        in FactoryBuildingDefinitionBlob building,
        in GridPlacement placement,
        int2 direction,
        ref EntityCommandBuffer ecb)
    {
        switch (placement.Kind)
        {
            case BuildingKind.VerticalBeltEntry:
            case BuildingKind.VerticalBeltExit:
            case BuildingKind.VerticalBeltSegment:
            case BuildingKind.Belt:
                ecb.AddComponent(instance, new BeltTopology
                {
                    CellsPerSecond = building.CellsPerSecond,
                    Cell = placement.AnchorCell,
                    Direction = direction,
                    ConnectionMode = VerticalBeltUtility.IsVertical(placement.Kind)
                        ? VerticalBeltUtility.ConnectionMode(placement.Kind) : TransportConnectionMode.Planar
                });
                if (VerticalBeltUtility.IsVertical(placement.Kind))
                    ecb.AddComponent(instance, new VerticalBeltNode
                    {
                        Kind = placement.Kind,
                        Step = VerticalBeltUtility.Step(placement.QuarterTurns)
                    });
                ecb.AddComponent(instance, new BeltState
                {
                    CurrentItem = Entity.Null
                });
                if (placement.Kind == BuildingKind.Belt)
                    ecb.AddComponent(instance, new BeltVisualNeedsRefresh());
                break;
            case BuildingKind.Merger:
                ecb.AddComponent(instance, new Merger
                {
                    Cell = placement.AnchorCell,
                    Direction = direction,
                    CurrentItem = Entity.Null
                });
                break;
            case BuildingKind.Splitter:
                ecb.AddComponent(instance, new Splitter
                {
                    Cell = placement.AnchorCell,
                    Direction = direction,
                    CurrentItem = Entity.Null
                });
                break;
            case BuildingKind.Processor:
                ecb.AddComponent(instance, new ItemProcessor
                {
                    MachineType = building.MachineType
                });
                ecb.AddBuffer<ProcessorItemSlot>(instance);
                ecb.AddComponent(instance, new ItemProcessState
                {
                    ActiveRecipeIndex = -1,
                    SelectedRecipeIndex = -1,
                    Status = ItemProcessStatus.Idle
                });
                ecb.AddComponent(instance, new ItemContainerIdentity
                {
                    RuntimeId = runtimeIdAllocator.Allocate()
                });
                AddItemPortBuffers(instance, ref ecb);
                break;
            case BuildingKind.Storage:
                ecb.AddComponent(instance, new StorageState
                {
                    Capacity = building.Capacity,
                    SlotCount = building.SlotCount
                });
                DynamicBuffer<InventorySlot> storageSlots =
                    ecb.AddBuffer<InventorySlot>(instance);
                for (int i = 0; i < building.SlotCount; i++)
                {
                    storageSlots.Add(default);
                }
                ecb.AddComponent(instance, new ItemContainerIdentity
                {
                    RuntimeId = runtimeIdAllocator.Allocate()
                });
                AddItemPortBuffers(instance, ref ecb);
                break;
        }
    }

    private BuildingRuntimeIdAllocator CreateRuntimeIdAllocator()
    {
        ulong next = 0x8000000000000000UL;
        using EntityQuery identities = EntityManager.CreateEntityQuery(
            ComponentType.ReadOnly<ItemContainerIdentity>());
        using NativeArray<ItemContainerIdentity> values =
            identities.ToComponentDataArray<ItemContainerIdentity>(Allocator.Temp);
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i].RuntimeId >= next && values[i].RuntimeId != ulong.MaxValue)
            {
                next = values[i].RuntimeId + 1;
            }
        }

        return new BuildingRuntimeIdAllocator { NextValue = next };
    }

    private static void AddItemPortBuffers(
        Entity instance,
        ref EntityCommandBuffer ecb)
    {
        ecb.AddComponent(instance, new ItemPortBufferGeneration());
        ecb.AddBuffer<ItemInputPortCurrent>(instance);
        ecb.AddBuffer<ItemInputPortNext>(instance);
        ecb.AddBuffer<ItemOutputPortCurrent>(instance);
        ecb.AddBuffer<ItemOutputPortNext>(instance);
        ecb.AddBuffer<ItemTransferReceiptCurrent>(instance);
        ecb.AddBuffer<ItemTransferReceiptNext>(instance);
    }

    private void InstantiatePortVisuals(
        Entity owner,
        in DynamicBuffer<BuildingPort> ports,
        in GridPlacement placement,
        in WorldGridConfig worldGrid,
        in BuildingPrefabCatalog catalog,
        ref EntityCommandBuffer ecb)
    {
        if (!UsesPortVisuals(placement.Kind))
        {
            return;
        }

        float cellSize = math.max(math.EPSILON, worldGrid.CellSize);
        for (int i = 0; i < ports.Length; i++)
        {
            BuildingPort port = ports[i];
            Entity visualPrefab = port.Type == BuildingPortType.Input
                ? catalog.InputPortVisual
                : catalog.OutputPortVisual;
            if (visualPrefab == Entity.Null ||
                !EntityManager.Exists(visualPrefab) ||
                !EntityManager.HasComponent<Prefab>(visualPrefab) ||
                !EntityManager.HasComponent<LocalTransform>(visualPrefab))
            {
                continue;
            }

            GridCell portCell = EcsGridUtility.GetBuildingCell(
                placement,
                port.CellOffset);
            int2 direction = EcsGridUtility.Rotate(
                port.Direction,
                placement.QuarterTurns);
            float3 position = EcsGridUtility.CellToWorldCenter(
                portCell,
                worldGrid);
            position.y += 0.375f * cellSize;
            float boundaryDirection =
                port.Type == BuildingPortType.Input ? 0.5f : -0.5f;
            position.x += direction.x * boundaryDirection * cellSize;
            position.z += direction.y * boundaryDirection * cellSize;

            Entity visual = ecb.Instantiate(visualPrefab);
            LocalTransform visualTransform =
                EntityManager.GetComponentData<LocalTransform>(visualPrefab);
            visualTransform.Position = position;
            visualTransform.Rotation =
                EcsGridUtility.RotationFromQuarterTurns(
                    EcsGridUtility.QuarterTurnsFromDirection(direction));
            visualTransform.Scale *= cellSize;
            ecb.SetComponent(visual, visualTransform);
            ecb.AddComponent(visual, new BuildingPortVisual
            {
                Owner = owner,
                Type = port.Type,
                PortIndex = port.Index
            });
            ecb.AppendToBuffer(owner, new LinkedEntityGroup
            {
                Value = visual
            });
        }
    }

    private static bool UsesPortVisuals(BuildingKind kind)
    {
        return kind == BuildingKind.VerticalBeltEntry ||
               kind == BuildingKind.VerticalBeltExit ||
               kind == BuildingKind.Processor ||
               kind == BuildingKind.Storage;
    }

    private void DestroyOwnedItem(
        Entity building,
        ref EntityCommandBuffer ecb)
    {
        Entity item = GetOwnedItem(building);
        if (item != Entity.Null && EntityManager.Exists(item))
        {
            if (!TryReturnToItemPool(item, ref ecb))
            {
                ecb.DestroyEntity(item);
            }
        }
    }

    private void RecoverOwnedItems(
        Entity building,
        Entity player,
        ref FactoryDatabaseBlob database,
        ref EntityCommandBuffer ecb)
    {
        if (player == Entity.Null ||
            !EntityManager.Exists(player) ||
            !EntityManager.HasComponent<PlayerInventory>(player) ||
            !EntityManager.HasBuffer<InventorySlot>(player))
        {
            DestroyOwnedItem(building, ref ecb);
            return;
        }

        DynamicBuffer<InventorySlot> playerSlots =
            EntityManager.GetBuffer<InventorySlot>(player);
        PlayerInventory inventory =
            EntityManager.GetComponentData<PlayerInventory>(player);
        bool recoveredAny = false;

        if (EntityManager.HasBuffer<InventorySlot>(building))
        {
            DynamicBuffer<InventorySlot> buildingSlots =
                EntityManager.GetBuffer<InventorySlot>(building, true);
            for (int i = 0; i < buildingSlots.Length; i++)
            {
                InventorySlot slot = buildingSlots[i];
                recoveredAny |= AddRecoveredItem(
                    slot.ItemType,
                    slot.Count,
                    ref database,
                    playerSlots,
                    ref inventory);
            }
        }

        if (EntityManager.HasBuffer<ProcessorItemSlot>(building))
        {
            DynamicBuffer<ProcessorItemSlot> processorSlots =
                EntityManager.GetBuffer<ProcessorItemSlot>(building, true);
            for (int i = 0; i < processorSlots.Length; i++)
            {
                ProcessorItemSlot slot = processorSlots[i];
                recoveredAny |= AddRecoveredItem(
                    slot.AcceptedItemType,
                    slot.Count,
                    ref database,
                    playerSlots,
                    ref inventory);
            }

            if (EntityManager.HasComponent<ItemProcessState>(building))
            {
                ItemProcessState process =
                    EntityManager.GetComponentData<ItemProcessState>(building);
                if ((process.Status == ItemProcessStatus.Processing ||
                     process.Status == ItemProcessStatus.Completed) &&
                    process.ActiveRecipeIndex >= 0 &&
                    process.ActiveRecipeIndex < database.Recipes.Length)
                {
                    FactoryRecipeBlob recipe =
                        database.Recipes[process.ActiveRecipeIndex];
                    for (int i = 0; i < recipe.InputCount; i++)
                    {
                        FactoryRecipeIngredientBlob ingredient =
                            database.Inputs[recipe.InputStart + i];
                        recoveredAny |= AddRecoveredItem(
                            ingredient.ItemId,
                            ingredient.Count,
                            ref database,
                            playerSlots,
                            ref inventory);
                    }
                }
            }
        }

        Entity carriedItem = GetOwnedItem(building);
        if (carriedItem != Entity.Null &&
            EntityManager.Exists(carriedItem) &&
            EntityManager.HasComponent<Item>(carriedItem))
        {
            recoveredAny |= AddRecoveredItem(
                EntityManager.GetComponentData<Item>(carriedItem).ItemType,
                1,
                ref database,
                playerSlots,
                ref inventory);
        }

        if (recoveredAny)
        {
            inventory.Revision++;
            EntityManager.SetComponentData(player, inventory);
        }

        DestroyOwnedItem(building, ref ecb);
    }

    private static bool AddRecoveredItem(
        ItemId itemType,
        int count,
        ref FactoryDatabaseBlob database,
        DynamicBuffer<InventorySlot> playerSlots,
        ref PlayerInventory inventory)
    {
        if (count <= 0 ||
            !FactoryDatabaseUtility.IsValidItem(ref database, itemType))
        {
            return false;
        }

        ItemProcessUtility.AddToInventory(
            itemType,
            count,
            ref database,
            playerSlots,
            ref inventory);
        return true;
    }

    private Entity GetOwnedItem(Entity building)
    {
        if (EntityManager.HasComponent<BeltState>(building))
            return EntityManager.GetComponentData<BeltState>(building).CurrentItem;
        if (EntityManager.HasComponent<Merger>(building))
            return EntityManager.GetComponentData<Merger>(building).CurrentItem;
        if (EntityManager.HasComponent<Splitter>(building))
            return EntityManager.GetComponentData<Splitter>(building).CurrentItem;
        return Entity.Null;
    }

    private Dictionary<PlayerId, Entity> BuildPlayerSnapshot()
    {
        Dictionary<PlayerId, Entity> players = new Dictionary<PlayerId, Entity>();
        using NativeArray<Entity> entities =
            playerQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<PlayerIdentity> identities =
            playerQuery.ToComponentDataArray<PlayerIdentity>(Allocator.Temp);
        for (int i = 0; i < entities.Length; i++)
            players[identities[i].Value] = entities[i];
        return players;
    }

    private static Entity GetPlayer(
        PlayerId playerId,
        Dictionary<PlayerId, Entity> players) =>
        playerId.IsValid && players.TryGetValue(playerId, out Entity player)
            ? player
            : Entity.Null;

    private bool TryReturnToItemPool(
        Entity item,
        ref EntityCommandBuffer ecb)
    {
        if (!EntityManager.HasComponent<Item>(item))
        {
            return false;
        }

        ItemId itemType =
            EntityManager.GetComponentData<Item>(item).ItemType;
        using NativeArray<Entity> pools =
            itemPoolQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<ItemPool> poolData =
            itemPoolQuery.ToComponentDataArray<ItemPool>(Allocator.Temp);
        for (int i = 0; i < pools.Length; i++)
        {
            if (poolData[i].ItemType != itemType)
            {
                continue;
            }

            ecb.SetComponentEnabled<Item>(item, false);
            if (!EntityManager.HasComponent<DisableRendering>(item))
            {
                ecb.AddComponent<DisableRendering>(item);
            }
            ecb.AppendToBuffer(
                pools[i],
                new ItemPoolEntry
                {
                    Entity = item
                });
            return true;
        }

        return false;
    }

    private static void RollBackStaged(
        List<PlacementRecord> staged,
        BatchPlacementState placements)
    {
        for (int i = staged.Count - 1; i >= 0; i--)
        {
            placements.RollBack(staged[i]);
        }
    }

    private static bool UsesTransportTopology(PlacementRecord record)
    {
        return VerticalBeltUtility.IsVertical(record.Placement.Kind) ||
               record.Placement.Kind == BuildingKind.Belt ||
               record.Placement.Kind == BuildingKind.Merger ||
               record.Placement.Kind == BuildingKind.Splitter;
    }

    private static List<(GridCell Cell, int2 Direction)> GetOutputs(
        PlacementRecord record)
    {
        List<(GridCell, int2)> outputs =
            new List<(GridCell, int2)>(3);
        for (int i = 0; i < record.Ports.Length; i++)
        {
            BuildingPort port = record.Ports[i];
            if (port.Type != BuildingPortType.Output)
            {
                continue;
            }

            outputs.Add((
                EcsGridUtility.GetBuildingCell(
                    record.Placement,
                    port.CellOffset),
                EcsGridUtility.Rotate(
                    port.Direction,
                    record.Placement.QuarterTurns)));
        }

        return outputs;
    }

    private static int CountOutputsTo(
        GridCell targetCell,
        BatchPlacementState placements)
    {
        int count = 0;
        HashSet<PlacementRecord> visited = new HashSet<PlacementRecord>();
        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            if (!placements.TryGet(
                    targetCell - CardinalDirections[i],
                    out PlacementRecord record) ||
                !visited.Add(record))
            {
                continue;
            }
            List<(GridCell Cell, int2 Direction)> outputs =
                GetOutputs(record);
            for (int outputIndex = 0;
                 outputIndex < outputs.Count;
                 outputIndex++)
            {
                if (outputs[outputIndex].Cell == targetCell)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static List<int2> GetIncomingDirections(
        GridCell targetCell,
        BatchPlacementState placements)
    {
        List<int2> directions = new List<int2>(4);
        HashSet<PlacementRecord> visited = new HashSet<PlacementRecord>();
        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            if (!placements.TryGet(
                    targetCell - CardinalDirections[i],
                    out PlacementRecord record) ||
                !visited.Add(record))
            {
                continue;
            }
            List<(GridCell Cell, int2 Direction)> outputs =
                GetOutputs(record);
            for (int outputIndex = 0;
                 outputIndex < outputs.Count;
                 outputIndex++)
            {
                if (outputs[outputIndex].Cell == targetCell)
                {
                    directions.Add(
                        outputs[outputIndex].Direction);
                }
            }
        }

        return directions;
    }

    private static bool TryGetOnlyIncomingDirection(
        GridCell targetCell,
        BatchPlacementState placements,
        out int2 direction)
    {
        List<int2> directions =
            GetIncomingDirections(targetCell, placements);
        if (directions.Count == 1)
        {
            direction = directions[0];
            return true;
        }

        direction = int2.zero;
        return false;
    }

    private static void RejectAll(
        DynamicBuffer<GridBuildCommand> commands,
        DynamicBuffer<GridBuildResult> results,
        GridBuildFailureReason reason)
    {
        for (int i = 0; i < commands.Length; i++)
        {
            GridBuildCommand command = commands[i];
            results.Add(new GridBuildResult
            {
                RequestId = command.RequestId,
                Type = command.Type,
                Kind = command.Kind,
                Cell = command.StartCell,
                Success = 0,
                AffectedCount = 0,
                FailureReason = reason
            });
        }
    }
}
