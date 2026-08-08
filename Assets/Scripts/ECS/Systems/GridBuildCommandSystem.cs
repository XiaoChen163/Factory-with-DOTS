using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(GridOccupancyIndexSystem))]
[UpdateBefore(typeof(FixedStepSimulationSystemGroup))]
public partial class GridBuildCommandSystem : SystemBase
{
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
        public GridPlacement Placement;
        public int2[] OccupiedCells;
        public BuildingPort[] Ports;
    }

    private readonly struct PathCell
    {
        public PathCell(int2 cell, int2 direction)
        {
            Cell = cell;
            Direction = direction;
        }

        public int2 Cell { get; }
        public int2 Direction { get; }
    }

    private EntityQuery gridQuery;
    private EntityQuery catalogQuery;
    private EntityQuery placementQuery;
    private EntityQuery itemPoolQuery;

    protected override void OnCreate()
    {
        gridQuery = GetEntityQuery(
            ComponentType.ReadWrite<GridDefinition>(),
            ComponentType.ReadWrite<GridBuildCommand>(),
            ComponentType.ReadWrite<GridBuildResult>());
        catalogQuery = GetEntityQuery(
            ComponentType.ReadOnly<BuildingPrefabCatalog>(),
            ComponentType.ReadOnly<BuildingVisualPrefabEntry>(),
            ComponentType.ReadOnly<FactoryDatabase>());
        placementQuery = GetEntityQuery(
            ComponentType.ReadOnly<GridPlacement>(),
            ComponentType.ReadOnly<OccupiedCellOffset>(),
            ComponentType.ReadOnly<BuildingPort>());
        itemPoolQuery = GetEntityQuery(
            ComponentType.ReadOnly<ItemPool>(),
            ComponentType.ReadOnly<ItemPoolEntry>());
    }

    protected override void OnUpdate()
    {
        if (gridQuery.CalculateEntityCount() != 1)
        {
            return;
        }

        Entity gridEntity = gridQuery.GetSingletonEntity();
        DynamicBuffer<GridBuildCommand> commands =
            EntityManager.GetBuffer<GridBuildCommand>(gridEntity);
        if (commands.IsEmpty)
        {
            return;
        }

        DynamicBuffer<GridBuildResult> results =
            EntityManager.GetBuffer<GridBuildResult>(gridEntity);
        results.Clear();

        if (catalogQuery.CalculateEntityCount() != 1)
        {
            RejectAll(
                commands,
                results,
                GridBuildFailureReason.MissingPrefab);
            commands.Clear();
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
        GridDefinition grid =
            EntityManager.GetComponentData<GridDefinition>(
                gridEntity);
        BuildingPrefabCatalog catalog =
            catalogQuery.GetSingleton<BuildingPrefabCatalog>();
        DynamicBuffer<BuildingVisualPrefabEntry> visualPrefabs =
            EntityManager.GetBuffer<BuildingVisualPrefabEntry>(
                catalogQuery.GetSingletonEntity(), true);
        BlobAssetReference<FactoryDatabaseBlob> databaseReference =
            catalogQuery.GetSingleton<FactoryDatabase>().Value;
        ref FactoryDatabaseBlob database = ref databaseReference.Value;

        BuildSnapshot(
            occupancySystem,
            out List<PlacementRecord> records,
            out Dictionary<int2, PlacementRecord> occupantByCell);

        EntityCommandBuffer ecb =
            new EntityCommandBuffer(Allocator.Temp);
        bool gridChanged = false;
        HashSet<int2> dirtyCells = new HashSet<int2>();

        for (int commandIndex = 0;
             commandIndex < commands.Length;
             commandIndex++)
        {
            GridBuildCommand command = commands[commandIndex];
            GridBuildFailureReason failureReason;
            bool success;
            int affectedCount = 0;

            switch (command.Type)
            {
                case GridBuildCommandType.Remove:
                    success = TryRemove(
                        command.StartCell,
                        records,
                        occupantByCell,
                        dirtyCells,
                        occupancySystem,
                        ref ecb,
                        out failureReason);
                    affectedCount = success ? 1 : 0;
                    break;

                case GridBuildCommandType.RemoveBeltLine:
                    success = TryRemoveBeltLine(
                        command.StartCell,
                        records,
                        occupantByCell,
                        dirtyCells,
                        occupancySystem,
                        ref ecb,
                        out affectedCount,
                        out failureReason);
                    break;

                case GridBuildCommandType.PlaceBeltPath:
                    success = TryPlaceBeltPath(
                        command,
                        grid,
                        catalog,
                        visualPrefabs,
                        ref database,
                        records,
                        occupantByCell,
                        dirtyCells,
                        ref ecb,
                        out failureReason);
                    affectedCount = success ? 1 : 0;
                    break;

                default:
                    success = TryPlaceSingle(
                        command.BuildingLevel,
                        command.StartCell,
                        command.QuarterTurns,
                        grid,
                        catalog,
                        visualPrefabs,
                        ref database,
                        records,
                        occupantByCell,
                        dirtyCells,
                        ref ecb,
                        out failureReason);
                    affectedCount = success ? 1 : 0;
                    break;
            }

            gridChanged |= success;
            results.Add(new GridBuildResult
            {
                RequestId = command.RequestId,
                Type = command.Type,
                Kind = command.Kind,
                BuildingLevel = command.BuildingLevel,
                Cell = command.Type ==
                       GridBuildCommandType.PlaceBeltPath
                    ? command.EndCell
                    : command.StartCell,
                Success = success ? (byte)1 : (byte)0,
                AffectedCount = affectedCount,
                FailureReason = failureReason
            });
        }

        commands.Clear();
        ecb.Playback(EntityManager);
        ecb.Dispose();

        if (gridChanged)
        {
            grid.Revision++;
            EntityManager.SetComponentData(gridEntity, grid);
            if (!EntityManager.HasBuffer<BeltVisualDirtyCell>(
                    gridEntity))
            {
                EntityManager.AddBuffer<BeltVisualDirtyCell>(
                    gridEntity);
            }

            DynamicBuffer<BeltVisualDirtyCell> dirtyBuffer =
                EntityManager.GetBuffer<BeltVisualDirtyCell>(
                    gridEntity);
            foreach (int2 cell in dirtyCells)
            {
                dirtyBuffer.Add(new BeltVisualDirtyCell
                {
                    Value = cell
                });
            }
        }
    }

    private void BuildSnapshot(
        GridOccupancyIndexSystem occupancySystem,
        out List<PlacementRecord> records,
        out Dictionary<int2, PlacementRecord> occupantByCell)
    {
        using NativeArray<Entity> entities =
            placementQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<GridPlacement> placements =
            placementQuery.ToComponentDataArray<GridPlacement>(
                Allocator.Temp);

        records = new List<PlacementRecord>(entities.Length);
        Dictionary<Entity, PlacementRecord> recordsByEntity =
            new Dictionary<Entity, PlacementRecord>(entities.Length);

        for (int i = 0; i < entities.Length; i++)
        {
            Entity entity = entities[i];
            DynamicBuffer<OccupiedCellOffset> occupiedOffsets =
                EntityManager.GetBuffer<OccupiedCellOffset>(
                    entity,
                    true);
            DynamicBuffer<BuildingPort> ports =
                EntityManager.GetBuffer<BuildingPort>(
                    entity,
                    true);
            PlacementRecord record = CreateRecord(
                entity,
                placements[i],
                occupiedOffsets,
                ports);
            records.Add(record);
            recordsByEntity.Add(entity, record);
        }

        occupantByCell =
            new Dictionary<int2, PlacementRecord>(
                occupancySystem.OccupiedCellCount);
        foreach (var pair in occupancySystem.Occupancy)
        {
            if (recordsByEntity.TryGetValue(
                    pair.Value,
                    out PlacementRecord record))
            {
                occupantByCell[pair.Key] = record;
            }
        }
    }

    private bool TryPlaceSingle(
        BuildingLevelId buildingLevelId,
        int2 anchor,
        byte quarterTurns,
        in GridDefinition grid,
        in BuildingPrefabCatalog catalog,
        in DynamicBuffer<BuildingVisualPrefabEntry> visualPrefabs,
        ref FactoryDatabaseBlob database,
        List<PlacementRecord> records,
        Dictionary<int2, PlacementRecord> occupantByCell,
        HashSet<int2> dirtyCells,
        ref EntityCommandBuffer ecb,
        out GridBuildFailureReason failureReason)
    {
        if (!TryCreateStagedRecord(
                buildingLevelId,
                anchor,
                quarterTurns,
                ref database,
                out FactoryBuildingLevelBlob level,
                out FactoryBuildingBlob building,
                out PlacementRecord candidate))
        {
            failureReason =
                GridBuildFailureReason.MissingPrefab;
            return false;
        }

        if (!CanPlace(
                candidate,
                grid,
                records,
                occupantByCell,
                out failureReason))
        {
            return false;
        }

        AddRecord(candidate, records, occupantByCell);
        Entity visualPrefab = BuildingPrefabCatalogUtility.GetPrefab(
            visualPrefabs,
            buildingLevelId);
        if (!IsValidVisualPrefab(visualPrefab))
        {
            RemoveRecord(candidate, records, occupantByCell);
            failureReason = GridBuildFailureReason.MissingPrefab;
            return false;
        }
        Instantiate(
            visualPrefab,
            building,
            level,
            candidate.Placement,
            grid,
            catalog,
            ref database,
            ref ecb);
        MarkCellsDirty(candidate.OccupiedCells, dirtyCells);
        failureReason = GridBuildFailureReason.None;
        return true;
    }

    private bool TryPlaceBeltPath(
        in GridBuildCommand command,
        in GridDefinition grid,
        in BuildingPrefabCatalog catalog,
        in DynamicBuffer<BuildingVisualPrefabEntry> visualPrefabs,
        ref FactoryDatabaseBlob database,
        List<PlacementRecord> records,
        Dictionary<int2, PlacementRecord> occupantByCell,
        HashSet<int2> dirtyCells,
        ref EntityCommandBuffer ecb,
        out GridBuildFailureReason failureReason)
    {
        if (!FactoryDatabaseUtility.TryGetBuildingLevel(
                ref database,
                command.BuildingLevel,
                out FactoryBuildingLevelBlob beltLevel,
                out FactoryBuildingBlob beltBuilding) ||
            beltBuilding.Kind != BuildingKind.Belt ||
            !FactoryDatabaseUtility.TryGetBeltLevel(
                ref database,
                command.BuildingLevel,
                out _))
        {
            failureReason =
                GridBuildFailureReason.MissingPrefab;
            return false;
        }
        Entity beltPrefab = BuildingPrefabCatalogUtility.GetPrefab(
            visualPrefabs,
            command.BuildingLevel);
        if (!IsValidVisualPrefab(beltPrefab))
        {
            failureReason = GridBuildFailureReason.MissingPrefab;
            return false;
        }

        List<PathCell> path = BuildBeltPath(
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
            PathCell pathCell = path[i];
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

            if (!CanPlace(
                    candidate,
                    grid,
                    records,
                    occupantByCell,
                    out failureReason))
            {
                RollBackStaged(
                    staged,
                    records,
                    occupantByCell);
                return false;
            }

            AddRecord(candidate, records, occupantByCell);
            staged.Add(candidate);
        }

        for (int i = 0; i < staged.Count; i++)
        {
            MarkCellsDirty(staged[i].OccupiedCells, dirtyCells);
            Instantiate(
                beltPrefab,
                beltBuilding,
                beltLevel,
                staged[i].Placement,
                grid,
                catalog,
                ref database,
                ref ecb);
        }

        failureReason = GridBuildFailureReason.None;
        return true;
    }

    private bool TryRemove(
        int2 cell,
        List<PlacementRecord> records,
        Dictionary<int2, PlacementRecord> occupantByCell,
        HashSet<int2> dirtyCells,
        GridOccupancyIndexSystem occupancySystem,
        ref EntityCommandBuffer ecb,
        out GridBuildFailureReason failureReason)
    {
        if (!occupantByCell.TryGetValue(
                cell,
                out PlacementRecord record) ||
            record.Entity == Entity.Null)
        {
            failureReason =
                GridBuildFailureReason.NothingToRemove;
            return false;
        }

        RemoveRecord(record, records, occupantByCell);
        RemoveOccupancy(record, occupancySystem);
        MarkCellsDirty(record.OccupiedCells, dirtyCells);
        DestroyOwnedItem(record.Entity, ref ecb);
        ecb.DestroyEntity(record.Entity);
        failureReason = GridBuildFailureReason.None;
        return true;
    }

    private bool TryRemoveBeltLine(
        int2 cell,
        List<PlacementRecord> records,
        Dictionary<int2, PlacementRecord> occupantByCell,
        HashSet<int2> dirtyCells,
        GridOccupancyIndexSystem occupancySystem,
        ref EntityCommandBuffer ecb,
        out int removedCount,
        out GridBuildFailureReason failureReason)
    {
        removedCount = 0;
        if (!occupantByCell.TryGetValue(
                cell,
                out PlacementRecord start) ||
            start.Entity == Entity.Null)
        {
            failureReason =
                GridBuildFailureReason.NothingToRemove;
            return false;
        }

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
                occupantByCell,
                connectedBelts,
                pending);
            TryEnqueueIncomingBelts(
                current,
                occupantByCell,
                connectedBelts,
                pending);
        }

        foreach (PlacementRecord belt in connectedBelts)
        {
            RemoveRecord(belt, records, occupantByCell);
            RemoveOccupancy(belt, occupancySystem);
            MarkCellsDirty(belt.OccupiedCells, dirtyCells);
            DestroyOwnedItem(belt.Entity, ref ecb);
            ecb.DestroyEntity(belt.Entity);
            removedCount++;
        }

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
        int2[] cells,
        HashSet<int2> dirtyCells)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            int2 cell = cells[i];
            dirtyCells.Add(cell);
            dirtyCells.Add(cell + new int2(1, 0));
            dirtyCells.Add(cell + new int2(-1, 0));
            dirtyCells.Add(cell + new int2(0, 1));
            dirtyCells.Add(cell + new int2(0, -1));
        }
    }

    private static void TryEnqueueOutputBelt(
        PlacementRecord source,
        Dictionary<int2, PlacementRecord> occupantByCell,
        HashSet<PlacementRecord> connectedBelts,
        Queue<PlacementRecord> pending)
    {
        if (!TryGetBeltOutputCell(source, out int2 outputCell) ||
            !occupantByCell.TryGetValue(
                outputCell,
                out PlacementRecord target) ||
            target.Placement.Kind != BuildingKind.Belt ||
            !connectedBelts.Add(target))
        {
            return;
        }

        pending.Enqueue(target);
    }

    private static void TryEnqueueIncomingBelts(
        PlacementRecord target,
        Dictionary<int2, PlacementRecord> occupantByCell,
        HashSet<PlacementRecord> connectedBelts,
        Queue<PlacementRecord> pending)
    {
        int2 targetCell = target.Placement.AnchorCell;
        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            int2 sourceCell =
                targetCell - CardinalDirections[i];
            if (!occupantByCell.TryGetValue(
                    sourceCell,
                    out PlacementRecord source) ||
                source.Placement.Kind != BuildingKind.Belt ||
                !TryGetBeltOutputCell(
                    source,
                    out int2 outputCell) ||
                !math.all(outputCell == targetCell) ||
                !connectedBelts.Add(source))
            {
                continue;
            }

            pending.Enqueue(source);
        }
    }

    private static bool TryGetBeltOutputCell(
        PlacementRecord belt,
        out int2 outputCell)
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

        outputCell = int2.zero;
        return false;
    }

    private bool CanPlace(
        PlacementRecord candidate,
        in GridDefinition grid,
        List<PlacementRecord> records,
        Dictionary<int2, PlacementRecord> occupantByCell,
        out GridBuildFailureReason failureReason)
    {
        for (int i = 0;
             i < candidate.OccupiedCells.Length;
             i++)
        {
            int2 cell = candidate.OccupiedCells[i];
            if (!EcsGridUtility.Contains(grid, cell))
            {
                failureReason =
                    GridBuildFailureReason.OutsideGrid;
                return false;
            }

            if (occupantByCell.ContainsKey(cell))
            {
                failureReason =
                    GridBuildFailureReason.CellOccupied;
                return false;
            }
        }

        int2 candidateDirection = EcsGridUtility.Rotate(
            new int2(1, 0),
            candidate.Placement.QuarterTurns);

        if (candidate.Placement.Kind == BuildingKind.Splitter)
        {
            int incomingCount = CountOutputsTo(
                candidate.Placement.AnchorCell,
                records);
            if (incomingCount > 1)
            {
                failureReason =
                    GridBuildFailureReason.SplitterInputConflict;
                return false;
            }

            if (incomingCount == 1 &&
                (!TryGetOnlyIncomingDirection(
                     candidate.Placement.AnchorCell,
                     records,
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
                    records);
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

        List<(int2 Cell, int2 Direction)> outputs =
            GetOutputs(candidate);
        for (int i = 0; i < outputs.Count; i++)
        {
            (int2 outputCell, int2 outputDirection) =
                outputs[i];
            if (!occupantByCell.TryGetValue(
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
                if (CountOutputsTo(outputCell, records) >= 1 ||
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

    private bool TryCreateStagedRecord(
        BuildingLevelId levelId,
        int2 anchor,
        byte quarterTurns,
        ref FactoryDatabaseBlob database,
        out FactoryBuildingLevelBlob level,
        out FactoryBuildingBlob building,
        out PlacementRecord record)
    {
        if (!FactoryDatabaseUtility.TryGetBuildingLevel(
                ref database,
                levelId,
                out level,
                out building) ||
            !HasRequiredLevelStats(
                building.Kind,
                levelId,
                ref database))
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
        return true;
    }

    private static bool HasRequiredLevelStats(
        BuildingKind kind,
        BuildingLevelId levelId,
        ref FactoryDatabaseBlob database)
    {
        switch (kind)
        {
            case BuildingKind.Belt:
                return FactoryDatabaseUtility.TryGetBeltLevel(
                    ref database,
                    levelId,
                    out _);
            case BuildingKind.Miner:
            case BuildingKind.Furnace:
                return FactoryDatabaseUtility.TryGetProcessorLevel(
                    ref database,
                    levelId,
                    out _);
            case BuildingKind.Storage:
                return FactoryDatabaseUtility.TryGetStorageLevel(
                    ref database,
                    levelId,
                    out _);
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
        in FactoryBuildingBlob building,
        in GridPlacement placement,
        ref FactoryDatabaseBlob database)
    {
        int occupiedCount = building.FootprintWidth * building.FootprintHeight;
        int2[] occupiedCells = new int2[occupiedCount];
        int cursor = 0;
        for (int y = 0; y < building.FootprintHeight; y++)
        for (int x = 0; x < building.FootprintWidth; x++)
        {
            occupiedCells[cursor++] = EcsGridUtility.GetBuildingCell(
                placement,
                new int2(x, y));
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
                Index = source.Index
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
        int2[] occupiedCells =
            new int2[occupiedOffsets.Length];
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

    private void Instantiate(
        Entity visualPrefab,
        in FactoryBuildingBlob building,
        in FactoryBuildingLevelBlob level,
        in GridPlacement placement,
        in GridDefinition grid,
        in BuildingPrefabCatalog catalog,
        ref FactoryDatabaseBlob database,
        ref EntityCommandBuffer ecb)
    {
        Entity instance = ecb.CreateEntity();
        ecb.AddComponent(instance, new PendingOccupancyAdd());
        ecb.AddComponent(instance, placement);
        ecb.AddComponent(instance, new BuildingIdentity
        {
            BuildingType = building.Id,
            BuildingLevel = level.Id,
            Level = level.Level
        });
        DynamicBuffer<OccupiedCellOffset> occupied =
            ecb.AddBuffer<OccupiedCellOffset>(instance);
        for (int y = 0; y < building.FootprintHeight; y++)
        for (int x = 0; x < building.FootprintWidth; x++)
            occupied.Add(new OccupiedCellOffset { Value = new int2(x, y) });
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
                Index = source.Index
            });
        }

        int2 direction = EcsGridUtility.Rotate(
            new int2(1, 0),
            placement.QuarterTurns);
        float3 center = EcsGridUtility.CellToWorldCenter(
            placement.AnchorCell,
            grid.Origin.y,
            grid);
        float2 visualOffset = EcsGridUtility.GetVisualCenterOffset(
            placement.FootprintSize) * grid.CellSize;
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
            level,
            placement,
            direction,
            ref database,
            ref ecb);

        DynamicBuffer<LinkedEntityGroup> linked =
            ecb.AddBuffer<LinkedEntityGroup>(instance);
        linked.Add(new LinkedEntityGroup { Value = instance });
        Entity visual = ecb.Instantiate(visualPrefab);
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
            grid,
            catalog,
            ref ecb);
    }

    private static void AddLogicComponents(
        Entity instance,
        in FactoryBuildingBlob building,
        in FactoryBuildingLevelBlob level,
        in GridPlacement placement,
        int2 direction,
        ref FactoryDatabaseBlob database,
        ref EntityCommandBuffer ecb)
    {
        switch (placement.Kind)
        {
            case BuildingKind.Belt:
                FactoryDatabaseUtility.TryGetBeltLevel(
                    ref database,
                    level.Id,
                    out FactoryBeltLevelBlob beltLevel);
                ecb.AddComponent(instance, new BeltTopology
                {
                    CellsPerSecond = beltLevel.CellsPerSecond,
                    Cell = placement.AnchorCell,
                    Direction = direction
                });
                ecb.AddComponent(instance, new BeltState
                {
                    CurrentItem = Entity.Null
                });
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
            case BuildingKind.Miner:
            case BuildingKind.Furnace:
                FactoryDatabaseUtility.TryGetProcessorLevel(
                    ref database,
                    level.Id,
                    out FactoryProcessorLevelBlob processorLevel);
                ecb.AddComponent(instance, new ItemProcessor
                {
                    MachineType = building.MachineType,
                    WorkRatePermille = processorLevel.WorkRatePermille
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
                    RuntimeId = ItemContainerRuntimeIdUtility.FromCell(
                        placement.AnchorCell)
                });
                AddItemPortBuffers(instance, ref ecb);
                break;
            case BuildingKind.Storage:
                FactoryDatabaseUtility.TryGetStorageLevel(
                    ref database,
                    level.Id,
                    out FactoryStorageLevelBlob storageLevel);
                ecb.AddComponent(instance, new StorageState
                {
                    Capacity = storageLevel.Capacity,
                    SlotCount = storageLevel.SlotCount
                });
                DynamicBuffer<InventorySlot> storageSlots =
                    ecb.AddBuffer<InventorySlot>(instance);
                for (int i = 0; i < storageLevel.SlotCount; i++)
                {
                    storageSlots.Add(default);
                }
                ecb.AddComponent(instance, new ItemContainerIdentity
                {
                    RuntimeId = ItemContainerRuntimeIdUtility.FromCell(
                        placement.AnchorCell)
                });
                AddItemPortBuffers(instance, ref ecb);
                break;
        }
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
        in GridDefinition grid,
        in BuildingPrefabCatalog catalog,
        ref EntityCommandBuffer ecb)
    {
        if (!UsesPortVisuals(placement.Kind))
        {
            return;
        }

        float cellSize = math.max(math.EPSILON, grid.CellSize);
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

            int2 portCell = EcsGridUtility.GetBuildingCell(
                placement,
                port.CellOffset);
            int2 direction = EcsGridUtility.Rotate(
                port.Direction,
                placement.QuarterTurns);
            float3 position = EcsGridUtility.CellToWorldCenter(
                portCell,
                grid.Origin.y + 0.375f * cellSize,
                grid);
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
        return kind == BuildingKind.Miner ||
               kind == BuildingKind.Furnace ||
               kind == BuildingKind.Storage;
    }

    private void DestroyOwnedItem(
        Entity building,
        ref EntityCommandBuffer ecb)
    {
        Entity item = Entity.Null;
        if (EntityManager.HasComponent<BeltState>(building))
        {
            item =
                EntityManager.GetComponentData<BeltState>(building)
                    .CurrentItem;
        }
        else if (EntityManager.HasComponent<Merger>(building))
        {
            item =
                EntityManager.GetComponentData<Merger>(building)
                    .CurrentItem;
        }
        else if (EntityManager.HasComponent<Splitter>(building))
        {
            item =
                EntityManager.GetComponentData<Splitter>(building)
                    .CurrentItem;
        }
        if (item != Entity.Null && EntityManager.Exists(item))
        {
            if (!TryReturnToItemPool(item, ref ecb))
            {
                ecb.DestroyEntity(item);
            }
        }
    }

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

    private static void AddRecord(
        PlacementRecord record,
        List<PlacementRecord> records,
        Dictionary<int2, PlacementRecord> occupantByCell)
    {
        records.Add(record);
        for (int i = 0; i < record.OccupiedCells.Length; i++)
        {
            occupantByCell.Add(record.OccupiedCells[i], record);
        }
    }

    private static void RemoveRecord(
        PlacementRecord record,
        List<PlacementRecord> records,
        Dictionary<int2, PlacementRecord> occupantByCell)
    {
        records.Remove(record);
        for (int i = 0; i < record.OccupiedCells.Length; i++)
        {
            occupantByCell.Remove(record.OccupiedCells[i]);
        }
    }

    private static void RollBackStaged(
        List<PlacementRecord> staged,
        List<PlacementRecord> records,
        Dictionary<int2, PlacementRecord> occupantByCell)
    {
        for (int i = staged.Count - 1; i >= 0; i--)
        {
            RemoveRecord(staged[i], records, occupantByCell);
        }
    }

    private static List<(int2 Cell, int2 Direction)> GetOutputs(
        PlacementRecord record)
    {
        List<(int2, int2)> outputs =
            new List<(int2, int2)>(3);
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
        int2 targetCell,
        List<PlacementRecord> records)
    {
        int count = 0;
        for (int i = 0; i < records.Count; i++)
        {
            List<(int2 Cell, int2 Direction)> outputs =
                GetOutputs(records[i]);
            for (int outputIndex = 0;
                 outputIndex < outputs.Count;
                 outputIndex++)
            {
                if (math.all(
                        outputs[outputIndex].Cell ==
                        targetCell))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static List<int2> GetIncomingDirections(
        int2 targetCell,
        List<PlacementRecord> records)
    {
        List<int2> directions = new List<int2>(4);
        for (int i = 0; i < records.Count; i++)
        {
            List<(int2 Cell, int2 Direction)> outputs =
                GetOutputs(records[i]);
            for (int outputIndex = 0;
                 outputIndex < outputs.Count;
                 outputIndex++)
            {
                if (math.all(
                        outputs[outputIndex].Cell ==
                        targetCell))
                {
                    directions.Add(
                        outputs[outputIndex].Direction);
                }
            }
        }

        return directions;
    }

    private static bool TryGetOnlyIncomingDirection(
        int2 targetCell,
        List<PlacementRecord> records,
        out int2 direction)
    {
        List<int2> directions =
            GetIncomingDirections(targetCell, records);
        if (directions.Count == 1)
        {
            direction = directions[0];
            return true;
        }

        direction = int2.zero;
        return false;
    }

    private static List<PathCell> BuildBeltPath(
        int2 start,
        int2 end,
        bool horizontalFirst,
        int2 initialDirection)
    {
        List<int2> cells = new List<int2>();
        int2 corner = horizontalFirst
            ? new int2(end.x, start.y)
            : new int2(start.x, end.y);

        AppendSegment(cells, start, corner, true);
        AppendSegment(cells, corner, end, false);

        List<PathCell> path =
            new List<PathCell>(cells.Count);
        int2 fallbackDirection =
            EcsGridUtility.SanitizeDirection(initialDirection);
        for (int i = 0; i < cells.Count; i++)
        {
            int2 direction;
            if (i < cells.Count - 1)
            {
                direction = cells[i + 1] - cells[i];
                fallbackDirection = direction;
            }
            else
            {
                direction = fallbackDirection;
            }

            path.Add(new PathCell(cells[i], direction));
        }

        return path;
    }

    private static void AppendSegment(
        List<int2> cells,
        int2 from,
        int2 to,
        bool includeStart)
    {
        int2 delta = to - from;
        int2 step = new int2(
            delta.x == 0 ? 0 : delta.x > 0 ? 1 : -1,
            delta.y == 0 ? 0 : delta.y > 0 ? 1 : -1);
        int length = math.abs(delta.x) + math.abs(delta.y);
        int first = includeStart ? 0 : 1;
        for (int i = first; i <= length; i++)
        {
            cells.Add(from + step * i);
        }
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
