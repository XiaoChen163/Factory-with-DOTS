using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
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

    protected override void OnCreate()
    {
        gridQuery = GetEntityQuery(
            ComponentType.ReadWrite<GridDefinition>(),
            ComponentType.ReadWrite<GridBuildCommand>(),
            ComponentType.ReadWrite<GridBuildResult>());
        catalogQuery = GetEntityQuery(
            ComponentType.ReadOnly<BuildingPrefabCatalog>());
        placementQuery = GetEntityQuery(
            ComponentType.ReadOnly<GridPlacement>(),
            ComponentType.ReadOnly<OccupiedCellOffset>(),
            ComponentType.ReadOnly<BuildingPort>());
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

        BuildSnapshot(
            occupancySystem,
            out List<PlacementRecord> records,
            out Dictionary<int2, PlacementRecord> occupantByCell);

        EntityCommandBuffer ecb =
            new EntityCommandBuffer(Allocator.Temp);
        bool gridChanged = false;

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
                        ref ecb,
                        out failureReason);
                    affectedCount = success ? 1 : 0;
                    break;

                case GridBuildCommandType.RemoveBeltLine:
                    success = TryRemoveBeltLine(
                        command.StartCell,
                        records,
                        occupantByCell,
                        ref ecb,
                        out affectedCount,
                        out failureReason);
                    break;

                case GridBuildCommandType.PlaceBeltPath:
                    success = TryPlaceBeltPath(
                        command,
                        grid,
                        catalog,
                        records,
                        occupantByCell,
                        ref ecb,
                        out failureReason);
                    affectedCount = success ? 1 : 0;
                    break;

                default:
                    success = TryPlaceSingle(
                        command.Kind,
                        command.StartCell,
                        command.QuarterTurns,
                        grid,
                        catalog,
                        records,
                        occupantByCell,
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
        BuildingKind kind,
        int2 anchor,
        byte quarterTurns,
        in GridDefinition grid,
        in BuildingPrefabCatalog catalog,
        List<PlacementRecord> records,
        Dictionary<int2, PlacementRecord> occupantByCell,
        ref EntityCommandBuffer ecb,
        out GridBuildFailureReason failureReason)
    {
        if (!TryCreateStagedRecord(
                kind,
                anchor,
                quarterTurns,
                catalog,
                out Entity prefab,
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
        Instantiate(
            prefab,
            candidate.Placement,
            grid,
            catalog,
            ref ecb);
        failureReason = GridBuildFailureReason.None;
        return true;
    }

    private bool TryPlaceBeltPath(
        in GridBuildCommand command,
        in GridDefinition grid,
        in BuildingPrefabCatalog catalog,
        List<PlacementRecord> records,
        Dictionary<int2, PlacementRecord> occupantByCell,
        ref EntityCommandBuffer ecb,
        out GridBuildFailureReason failureReason)
    {
        Entity beltPrefab =
            BuildingPrefabCatalogUtility.GetPrefab(
                catalog,
                BuildingKind.Belt);
        if (!IsValidBuildingPrefab(beltPrefab))
        {
            failureReason =
                GridBuildFailureReason.MissingPrefab;
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
                QuarterTurns =
                    EcsGridUtility.QuarterTurnsFromDirection(
                        pathCell.Direction),
                Kind = BuildingKind.Belt
            };
            PlacementRecord candidate =
                CreateRecordFromPrefab(
                    beltPrefab,
                    placement);

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
            Instantiate(
                beltPrefab,
                staged[i].Placement,
                grid,
                catalog,
                ref ecb);
        }

        failureReason = GridBuildFailureReason.None;
        return true;
    }

    private bool TryRemove(
        int2 cell,
        List<PlacementRecord> records,
        Dictionary<int2, PlacementRecord> occupantByCell,
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
        DestroyOwnedItem(record.Entity, ref ecb);
        ecb.DestroyEntity(record.Entity);
        failureReason = GridBuildFailureReason.None;
        return true;
    }

    private bool TryRemoveBeltLine(
        int2 cell,
        List<PlacementRecord> records,
        Dictionary<int2, PlacementRecord> occupantByCell,
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
            DestroyOwnedItem(belt.Entity, ref ecb);
            ecb.DestroyEntity(belt.Entity);
            removedCount++;
        }

        failureReason = GridBuildFailureReason.None;
        return removedCount > 0;
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

            outputCell =
                belt.Placement.AnchorCell +
                EcsGridUtility.Rotate(
                    port.CellOffset,
                    belt.Placement.QuarterTurns);
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
        BuildingKind kind,
        int2 anchor,
        byte quarterTurns,
        in BuildingPrefabCatalog catalog,
        out Entity prefab,
        out PlacementRecord record)
    {
        prefab = BuildingPrefabCatalogUtility.GetPrefab(
            catalog,
            kind);
        if (!IsValidBuildingPrefab(prefab))
        {
            record = null;
            return false;
        }

        GridPlacement placement = new GridPlacement
        {
            AnchorCell = anchor,
            QuarterTurns = (byte)(quarterTurns % 4),
            Kind = kind
        };
        record = CreateRecordFromPrefab(prefab, placement);
        return true;
    }

    private bool IsValidBuildingPrefab(Entity prefab)
    {
        return prefab != Entity.Null &&
               EntityManager.Exists(prefab) &&
               EntityManager.HasComponent<GridPlacement>(prefab) &&
               EntityManager.HasBuffer<OccupiedCellOffset>(prefab) &&
               EntityManager.HasBuffer<BuildingPort>(prefab);
    }

    private PlacementRecord CreateRecordFromPrefab(
        Entity prefab,
        GridPlacement placement)
    {
        return CreateRecord(
            Entity.Null,
            placement,
            EntityManager.GetBuffer<OccupiedCellOffset>(
                prefab,
                true),
            EntityManager.GetBuffer<BuildingPort>(
                prefab,
                true));
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
            occupiedCells[i] =
                placement.AnchorCell +
                EcsGridUtility.Rotate(
                    occupiedOffsets[i].Value,
                    placement.QuarterTurns);
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
        Entity prefab,
        GridPlacement placement,
        in GridDefinition grid,
        in BuildingPrefabCatalog catalog,
        ref EntityCommandBuffer ecb)
    {
        Entity instance = ecb.Instantiate(prefab);
        ecb.SetComponent(instance, placement);

        int2 direction = EcsGridUtility.Rotate(
            new int2(1, 0),
            placement.QuarterTurns);
        switch (placement.Kind)
        {
            case BuildingKind.Belt:
                Belt belt =
                    EntityManager.GetComponentData<Belt>(prefab);
                belt.Cell = placement.AnchorCell;
                belt.Direction = direction;
                belt.NextCell = placement.AnchorCell + direction;
                belt.CurrentItem = Entity.Null;
                belt.Progress = 0f;
                belt.IsLoop = false;
                belt.HasOutput = false;
                ecb.SetComponent(instance, belt);
                break;

            case BuildingKind.Merger:
                Merger merger =
                    EntityManager.GetComponentData<Merger>(prefab);
                merger.Cell = placement.AnchorCell;
                merger.Direction = direction;
                merger.CurrentItem = Entity.Null;
                merger.TransferElapsed = 0f;
                merger.InputInterval = 0f;
                merger.NextInputIndex = 0;
                ecb.SetComponent(instance, merger);
                break;

            case BuildingKind.Splitter:
                Splitter splitter =
                    EntityManager.GetComponentData<Splitter>(
                        prefab);
                splitter.Cell = placement.AnchorCell;
                splitter.Direction = direction;
                splitter.CurrentItem = Entity.Null;
                splitter.TransferElapsed = 0f;
                splitter.InputInterval = 0f;
                splitter.NextOutputIndex = 0;
                ecb.SetComponent(instance, splitter);
                break;
        }

        if (!EntityManager.HasComponent<LocalTransform>(prefab))
        {
            return;
        }

        LocalTransform transform =
            EntityManager.GetComponentData<LocalTransform>(prefab);
        float3 center = EcsGridUtility.CellToWorldCenter(
            placement.AnchorCell,
            transform.Position.y,
            grid);
        float2 visualOffset =
            EcsGridUtility.GetVisualCenterOffset(
                placement.Kind,
                placement.QuarterTurns) *
            grid.CellSize;
        center.x += visualOffset.x;
        center.z += visualOffset.y;
        transform.Position = center;
        transform.Rotation =
            EcsGridUtility.RotationFromQuarterTurns(
                placement.QuarterTurns);
        ecb.SetComponent(instance, transform);
        InstantiatePortVisuals(
            instance,
            prefab,
            placement,
            grid,
            catalog,
            ref ecb);
    }

    private void InstantiatePortVisuals(
        Entity owner,
        Entity buildingPrefab,
        in GridPlacement placement,
        in GridDefinition grid,
        in BuildingPrefabCatalog catalog,
        ref EntityCommandBuffer ecb)
    {
        if (!UsesPortVisuals(placement.Kind) ||
            !EntityManager.HasBuffer<LinkedEntityGroup>(buildingPrefab))
        {
            return;
        }

        DynamicBuffer<BuildingPort> ports =
            EntityManager.GetBuffer<BuildingPort>(buildingPrefab, true);
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

            int2 portCell = placement.AnchorCell +
                EcsGridUtility.Rotate(
                    port.CellOffset,
                    placement.QuarterTurns);
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
        if (EntityManager.HasComponent<Belt>(building))
        {
            item =
                EntityManager.GetComponentData<Belt>(building)
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
            ecb.DestroyEntity(item);
        }
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
                record.Placement.AnchorCell +
                EcsGridUtility.Rotate(
                    port.CellOffset,
                    record.Placement.QuarterTurns),
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
