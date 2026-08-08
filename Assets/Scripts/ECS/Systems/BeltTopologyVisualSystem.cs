using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[UpdateAfter(typeof(GridOccupancyIndexSystem))]
[UpdateBefore(typeof(GridBuildCommandSystem))]
public partial class BeltTopologyVisualSystem : SystemBase
{
    private ComponentLookup<BeltTopology> beltTopologyLookup;
    private ComponentLookup<GridPlacement> gridPlacementLookup;
    private ComponentLookup<BuildingVisualReference> visualReferenceLookup;
    private ComponentLookup<BeltVisualParts> visualPartsLookup;
    private EntityQuery needsRefreshQuery;
    private EntityCommandBuffer pendingVisualEcb;
    private NativeParallelHashSet<Entity> pendingVisibleEdges;
    private NativeParallelHashSet<Entity> pendingHiddenEdges;

    protected override void OnCreate()
    {
        beltTopologyLookup = GetComponentLookup<BeltTopology>(true);
        gridPlacementLookup = GetComponentLookup<GridPlacement>(true);
        visualReferenceLookup =
            GetComponentLookup<BuildingVisualReference>(true);
        visualPartsLookup = GetComponentLookup<BeltVisualParts>(true);
        needsRefreshQuery = GetEntityQuery(
            ComponentType.ReadOnly<BeltVisualNeedsRefresh>(),
            ComponentType.ReadOnly<BeltTopology>(),
            ComponentType.ReadOnly<GridPlacement>(),
            ComponentType.ReadOnly<BuildingVisualReference>());
        pendingVisibleEdges =
            new NativeParallelHashSet<Entity>(16, Allocator.Persistent);
        pendingHiddenEdges =
            new NativeParallelHashSet<Entity>(16, Allocator.Persistent);
        RequireForUpdate<GridDefinition>();
    }

    protected override void OnDestroy()
    {
        Dependency.Complete();
        if (pendingVisibleEdges.IsCreated)
        {
            pendingVisibleEdges.Dispose();
        }

        if (pendingHiddenEdges.IsCreated)
        {
            pendingHiddenEdges.Dispose();
        }
    }

    protected override void OnUpdate()
    {
        beltTopologyLookup.Update(this);
        gridPlacementLookup.Update(this);
        visualReferenceLookup.Update(this);
        visualPartsLookup.Update(this);

        GridOccupancyIndexSystem occupancySystem =
            World.GetExistingSystemManaged<
                GridOccupancyIndexSystem>();
        if (occupancySystem == null ||
            !occupancySystem.IsReady ||
            occupancySystem.ConflictCount > 0)
        {
            return;
        }

        Entity gridEntity =
            SystemAPI.GetSingletonEntity<GridDefinition>();
        bool hasDirty =
            EntityManager.HasBuffer<BeltVisualDirtyCell>(gridEntity) &&
            !EntityManager.GetBuffer<BeltVisualDirtyCell>(gridEntity)
                .IsEmpty;
        if (!hasDirty && needsRefreshQuery.IsEmptyIgnoreFilter)
        {
            return;
        }

        Dependency.Complete();
        pendingVisualEcb = new EntityCommandBuffer(Allocator.Temp);
        pendingVisibleEdges.Clear();
        pendingHiddenEdges.Clear();
        DynamicBuffer<BeltVisualDirtyCell> dirtyCells = default;
        if (hasDirty)
        {
            dirtyCells =
                EntityManager.GetBuffer<BeltVisualDirtyCell>(gridEntity);
            using NativeParallelHashSet<int2> dirtyCellSet =
                new NativeParallelHashSet<int2>(
                    math.max(16, dirtyCells.Length * 2),
                    Allocator.Temp);
            for (int i = 0; i < dirtyCells.Length; i++)
            {
                dirtyCellSet.Add(dirtyCells[i].Value);
            }

            foreach (int2 cell in dirtyCellSet)
            {
                if (!occupancySystem.TryGetOccupant(
                        cell,
                        out Entity building) ||
                    !beltTopologyLookup.HasComponent(building) ||
                    !gridPlacementLookup.HasComponent(building) ||
                    !visualReferenceLookup.HasComponent(building))
                {
                    continue;
                }

                Entity visualEntity =
                    visualReferenceLookup[building].Value;
                if (visualEntity == Entity.Null ||
                    !visualPartsLookup.HasComponent(visualEntity))
                {
                    continue;
                }

                RefreshBeltVisual(
                    beltTopologyLookup[building],
                    gridPlacementLookup[building],
                    visualPartsLookup[visualEntity],
                    occupancySystem);
            }

            dirtyCells.Clear();
        }

        using NativeArray<Entity> refreshEntities =
            needsRefreshQuery.ToEntityArray(Allocator.Temp);
        List<Entity> processedRefresh =
            new List<Entity>(refreshEntities.Length);
        for (int i = 0; i < refreshEntities.Length; i++)
        {
            Entity building = refreshEntities[i];
            if (!beltTopologyLookup.HasComponent(building) ||
                !gridPlacementLookup.HasComponent(building) ||
                !visualReferenceLookup.HasComponent(building))
            {
                continue;
            }

            Entity visualEntity = visualReferenceLookup[building].Value;
            if (visualEntity == Entity.Null ||
                !visualPartsLookup.HasComponent(visualEntity))
            {
                continue;
            }

            RefreshBeltVisual(
                beltTopologyLookup[building],
                gridPlacementLookup[building],
                visualPartsLookup[visualEntity],
                occupancySystem);
            processedRefresh.Add(building);
        }

        for (int i = 0; i < processedRefresh.Count; i++)
        {
            EntityManager.RemoveComponent<BeltVisualNeedsRefresh>(
                processedRefresh[i]);
        }

        FlushPendingVisibility();
        pendingVisualEcb.Playback(EntityManager);
        pendingVisualEcb.Dispose();
        pendingVisualEcb = default;
    }

    private void RefreshBeltVisual(
        in BeltTopology belt,
        in GridPlacement placement,
        in BeltVisualParts visual,
        GridOccupancyIndexSystem occupancySystem)
    {
        SetAllEdgesVisible(visual, true);

        bool hasInput = TryFindIncomingDirection(
            belt.Cell,
            belt.Direction,
            occupancySystem,
            out int2 incomingDirection);
        if (hasInput)
        {
            SetWorldEdgeVisible(
                visual,
                -incomingDirection,
                placement.QuarterTurns,
                false);
        }

        bool hasOutput = HasOutputConnection(
            belt,
            occupancySystem);
        if (hasOutput)
        {
            SetWorldEdgeVisible(
                visual,
                belt.Direction,
                placement.QuarterTurns,
                false);
        }

        int2 displayDirection = belt.Direction;
        if (hasInput &&
            !math.all(incomingDirection == belt.Direction))
        {
            int2 cornerDirection =
                incomingDirection + belt.Direction;
            if (!math.all(cornerDirection == int2.zero))
            {
                displayDirection = cornerDirection;
            }
        }

        SetTriangleDirection(
            visual.DirectionTriangle,
            displayDirection,
            placement.QuarterTurns);
    }

    private bool TryFindIncomingDirection(
        int2 targetCell,
        int2 targetDirection,
        GridOccupancyIndexSystem occupancySystem,
        out int2 incomingDirection)
    {
        if (HasIncomingFromDirection(
                targetCell,
                targetDirection,
                occupancySystem))
        {
            incomingDirection = targetDirection;
            return true;
        }

        int2 right =
            new int2(targetDirection.y, -targetDirection.x);
        if (HasIncomingFromDirection(
                targetCell,
                right,
                occupancySystem))
        {
            incomingDirection = right;
            return true;
        }

        int2 left = -right;
        if (HasIncomingFromDirection(
                targetCell,
                left,
                occupancySystem))
        {
            incomingDirection = left;
            return true;
        }

        incomingDirection = int2.zero;
        return false;
    }

    private bool HasIncomingFromDirection(
        int2 targetCell,
        int2 travelDirection,
        GridOccupancyIndexSystem occupancySystem)
    {
        int2 sourceCell = targetCell - travelDirection;
        if (!occupancySystem.TryGetOccupant(
                sourceCell,
                out Entity source) ||
            !EntityManager.HasComponent<GridPlacement>(source) ||
            !EntityManager.HasBuffer<BuildingPort>(source))
        {
            return false;
        }

        GridPlacement sourcePlacement =
            EntityManager.GetComponentData<GridPlacement>(source);
        DynamicBuffer<BuildingPort> sourcePorts =
            EntityManager.GetBuffer<BuildingPort>(source, true);
        for (int i = 0; i < sourcePorts.Length; i++)
        {
            BuildingPort port = sourcePorts[i];
            if (port.Type != BuildingPortType.Output)
            {
                continue;
            }

            int2 outputCell = EcsGridUtility.GetBuildingCell(
                sourcePlacement,
                port.CellOffset);
            int2 outputDirection = EcsGridUtility.Rotate(
                port.Direction,
                sourcePlacement.QuarterTurns);
            if (math.all(outputCell == targetCell) &&
                math.all(outputDirection == travelDirection))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasOutputConnection(
        in BeltTopology belt,
        GridOccupancyIndexSystem occupancySystem)
    {
        int2 targetCell = belt.Cell + belt.Direction;
        if (!occupancySystem.TryGetOccupant(
                targetCell,
                out Entity target) ||
            !EntityManager.HasComponent<GridPlacement>(target) ||
            !EntityManager.HasBuffer<BuildingPort>(target))
        {
            return false;
        }

        if (EntityManager.HasComponent<BeltTopology>(target))
        {
            BeltTopology targetBelt =
                EntityManager.GetComponentData<BeltTopology>(target);
            return TryFindIncomingDirection(
                       targetBelt.Cell,
                       targetBelt.Direction,
                       occupancySystem,
                       out int2 selectedInputDirection) &&
                   math.all(
                       selectedInputDirection == belt.Direction);
        }

        GridPlacement targetPlacement =
            EntityManager.GetComponentData<GridPlacement>(target);
        DynamicBuffer<BuildingPort> targetPorts =
            EntityManager.GetBuffer<BuildingPort>(target, true);
        for (int i = 0; i < targetPorts.Length; i++)
        {
            BuildingPort port = targetPorts[i];
            if (port.Type != BuildingPortType.Input)
            {
                continue;
            }

            int2 sourceCell = EcsGridUtility.GetBuildingCell(
                targetPlacement,
                port.CellOffset);
            int2 travelDirection = EcsGridUtility.Rotate(
                port.Direction,
                targetPlacement.QuarterTurns);
            if (math.all(sourceCell == belt.Cell) &&
                math.all(travelDirection == belt.Direction))
            {
                return true;
            }
        }

        return false;
    }

    private void SetAllEdgesVisible(
        in BeltVisualParts visual,
        bool visible)
    {
        SetVisible(visual.EastEdge, visible);
        SetVisible(visual.NorthEdge, visible);
        SetVisible(visual.WestEdge, visible);
        SetVisible(visual.SouthEdge, visible);
    }

    private void SetWorldEdgeVisible(
        in BeltVisualParts visual,
        int2 worldDirection,
        int quarterTurns,
        bool visible)
    {
        int2 localDirection = EcsGridUtility.Rotate(
            worldDirection,
            -quarterTurns);
        if (math.all(localDirection == new int2(1, 0)))
        {
            SetVisible(visual.EastEdge, visible);
        }
        else if (math.all(localDirection == new int2(0, 1)))
        {
            SetVisible(visual.NorthEdge, visible);
        }
        else if (math.all(localDirection == new int2(-1, 0)))
        {
            SetVisible(visual.WestEdge, visible);
        }
        else if (math.all(localDirection == new int2(0, -1)))
        {
            SetVisible(visual.SouthEdge, visible);
        }
    }

    private void SetVisible(Entity visualEntity, bool visible)
    {
        if (visualEntity == Entity.Null ||
            !EntityManager.Exists(visualEntity))
        {
            return;
        }

        if (visible)
        {
            pendingVisibleEdges.Add(visualEntity);
            pendingHiddenEdges.Remove(visualEntity);
        }
        else
        {
            pendingHiddenEdges.Add(visualEntity);
            pendingVisibleEdges.Remove(visualEntity);
        }
    }

    private void FlushPendingVisibility()
    {
        foreach (Entity visualEntity in pendingVisibleEdges)
        {
            if (EntityManager.Exists(visualEntity) &&
                EntityManager.HasComponent<DisableRendering>(visualEntity))
            {
                pendingVisualEcb.RemoveComponent<DisableRendering>(
                    visualEntity);
            }
        }

        foreach (Entity visualEntity in pendingHiddenEdges)
        {
            if (EntityManager.Exists(visualEntity) &&
                !EntityManager.HasComponent<DisableRendering>(visualEntity))
            {
                pendingVisualEcb.AddComponent<DisableRendering>(
                    visualEntity);
            }
        }
    }

    private void SetTriangleDirection(
        Entity triangle,
        int2 worldDirection,
        int quarterTurns)
    {
        if (triangle == Entity.Null ||
            !EntityManager.Exists(triangle) ||
            !EntityManager.HasComponent<LocalTransform>(triangle))
        {
            return;
        }

        int2 localDirection = EcsGridUtility.Rotate(
            worldDirection,
            -quarterTurns);
        float angle = -math.atan2(
            localDirection.y,
            localDirection.x);
        LocalTransform transform =
            EntityManager.GetComponentData<LocalTransform>(triangle);
        transform.Rotation = quaternion.RotateY(angle);
        EntityManager.SetComponentData(triangle, transform);
    }
}
