using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

internal readonly struct TransportGridBakeResult
{
    public TransportGridBakeResult(int2 cell, int2 direction)
    {
        Cell = cell;
        Direction = direction;
    }

    public int2 Cell { get; }
    public int2 Direction { get; }
}

internal static class TransportGridBakingUtility
{
    public static TransportGridBakeResult AddGridData<TAuthoring>(
        Baker<TAuthoring> baker,
        Entity entity,
        BuildingKind kind,
        Vector3 worldPosition,
        Vector2Int configuredDirection)
        where TAuthoring : Component
    {
        int2 direction = EcsGridUtility.SanitizeDirection(
            new int2(
                configuredDirection.x,
                configuredDirection.y));
        int2 cell = EcsGridUtility.WorldToCell(
            new float3(
                worldPosition.x,
                worldPosition.y,
                worldPosition.z));
        byte quarterTurns =
            EcsGridUtility.QuarterTurnsFromDirection(direction);

        baker.AddComponent(entity, new GridPlacement
        {
            AnchorCell = cell,
            QuarterTurns = quarterTurns,
            Kind = kind
        });

        DynamicBuffer<OccupiedCellOffset> occupiedCells =
            baker.AddBuffer<OccupiedCellOffset>(entity);
        AddOccupiedCells(occupiedCells, kind);

        DynamicBuffer<BuildingPort> ports =
            baker.AddBuffer<BuildingPort>(entity);
        AddPorts(ports, kind);

        return new TransportGridBakeResult(cell, direction);
    }

    private static void AddOccupiedCells(
        DynamicBuffer<OccupiedCellOffset> occupiedCells,
        BuildingKind kind)
    {
        occupiedCells.Add(new OccupiedCellOffset
        {
            Value = int2.zero
        });

        if (!IsMachine(kind))
        {
            return;
        }

        occupiedCells.Add(new OccupiedCellOffset
        {
            Value = new int2(0, 1)
        });
        occupiedCells.Add(new OccupiedCellOffset
        {
            Value = new int2(1, 0)
        });
        occupiedCells.Add(new OccupiedCellOffset
        {
            Value = new int2(1, 1)
        });
    }

    private static void AddPorts(
        DynamicBuffer<BuildingPort> ports,
        BuildingKind kind)
    {
        switch (kind)
        {
            case BuildingKind.Belt:
                AddInput(ports, new int2(-1, 0), new int2(1, 0), 0);
                AddInput(ports, new int2(0, -1), new int2(0, 1), 1);
                AddInput(ports, new int2(1, 0), new int2(-1, 0), 2);
                AddInput(ports, new int2(0, 1), new int2(0, -1), 3);
                AddOutput(ports, new int2(1, 0), new int2(1, 0), 0);
                break;

            case BuildingKind.Merger:
                AddInput(ports, new int2(-1, 0), new int2(1, 0), 0);
                AddInput(ports, new int2(0, 1), new int2(0, -1), 1);
                AddInput(ports, new int2(0, -1), new int2(0, 1), 2);
                AddOutput(ports, new int2(1, 0), new int2(1, 0), 0);
                break;

            case BuildingKind.Splitter:
                AddInput(ports, new int2(-1, 0), new int2(1, 0), 0);
                AddOutput(ports, new int2(1, 0), new int2(1, 0), 0);
                AddOutput(ports, new int2(0, 1), new int2(0, 1), 1);
                AddOutput(ports, new int2(0, -1), new int2(0, -1), 2);
                break;

            case BuildingKind.Miner:
                AddOutput(ports, new int2(2, 0), new int2(1, 0), 0);
                break;

            case BuildingKind.Furnace:
                AddInput(ports, new int2(-1, 0), new int2(1, 0), 0);
                AddOutput(ports, new int2(2, 0), new int2(1, 0), 0);
                break;

            case BuildingKind.Storage:
                AddInput(ports, new int2(-1, 0), new int2(1, 0), 0);
                break;
        }
    }

    private static bool IsMachine(BuildingKind kind)
    {
        return kind == BuildingKind.Miner ||
               kind == BuildingKind.Furnace ||
               kind == BuildingKind.Storage;
    }

    private static void AddInput(
        DynamicBuffer<BuildingPort> ports,
        int2 cellOffset,
        int2 direction,
        byte index)
    {
        ports.Add(new BuildingPort
        {
            CellOffset = cellOffset,
            Direction = direction,
            Type = BuildingPortType.Input,
            Index = index
        });
    }

    private static void AddOutput(
        DynamicBuffer<BuildingPort> ports,
        int2 cellOffset,
        int2 direction,
        byte index)
    {
        ports.Add(new BuildingPort
        {
            CellOffset = cellOffset,
            Direction = direction,
            Type = BuildingPortType.Output,
            Index = index
        });
    }
}
