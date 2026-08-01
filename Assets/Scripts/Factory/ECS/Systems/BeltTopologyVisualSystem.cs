using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[UpdateAfter(typeof(GridOccupancyIndexSystem))]
[UpdateBefore(typeof(GridBuildCommandSystem))]
public partial class BeltTopologyVisualSystem : SystemBase
{
    private EntityQuery beltQuery;
    private uint lastGridRevision = uint.MaxValue;

    protected override void OnCreate()
    {
        beltQuery = GetEntityQuery(
            ComponentType.ReadOnly<Belt>(),
            ComponentType.ReadOnly<GridPlacement>(),
            ComponentType.ReadOnly<BeltVisualParts>());
        RequireForUpdate<GridDefinition>();
    }

    protected override void OnUpdate()
    {
        GridOccupancyIndexSystem occupancySystem =
            World.GetExistingSystemManaged<
                GridOccupancyIndexSystem>();
        if (occupancySystem == null ||
            !occupancySystem.IsReady ||
            occupancySystem.ConflictCount > 0)
        {
            return;
        }

        GridDefinition grid =
            SystemAPI.GetSingleton<GridDefinition>();
        if (grid.Revision == lastGridRevision)
        {
            return;
        }

        Dependency.Complete();
        using Unity.Collections.NativeArray<Entity> entities =
            beltQuery.ToEntityArray(
                Unity.Collections.Allocator.Temp);
        using Unity.Collections.NativeArray<Belt> belts =
            beltQuery.ToComponentDataArray<Belt>(
                Unity.Collections.Allocator.Temp);
        using Unity.Collections.NativeArray<GridPlacement> placements =
            beltQuery.ToComponentDataArray<GridPlacement>(
                Unity.Collections.Allocator.Temp);
        using Unity.Collections.NativeArray<BeltVisualParts> visuals =
            beltQuery.ToComponentDataArray<BeltVisualParts>(
                Unity.Collections.Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            RefreshBeltVisual(
                belts[i],
                placements[i],
                visuals[i],
                occupancySystem);
        }

        lastGridRevision = grid.Revision;
    }

    private void RefreshBeltVisual(
        in Belt belt,
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

            int2 outputCell =
                sourcePlacement.AnchorCell +
                EcsGridUtility.Rotate(
                    port.CellOffset,
                    sourcePlacement.QuarterTurns);
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
        in Belt belt,
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

        if (EntityManager.HasComponent<Belt>(target))
        {
            Belt targetBelt =
                EntityManager.GetComponentData<Belt>(target);
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

            int2 sourceCell =
                targetPlacement.AnchorCell +
                EcsGridUtility.Rotate(
                    port.CellOffset,
                    targetPlacement.QuarterTurns);
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

        bool isHidden =
            EntityManager.HasComponent<DisableRendering>(
                visualEntity);
        if (visible && isHidden)
        {
            EntityManager.RemoveComponent<DisableRendering>(
                visualEntity);
        }
        else if (!visible && !isHidden)
        {
            EntityManager.AddComponent<DisableRendering>(
                visualEntity);
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
