using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
[UpdateBefore(typeof(GridOccupancyIndexSystem))]
public partial class RampRegistrySystem : SystemBase
{
    public sealed class Record
    {
        public Entity ConnectorEntity;
        public RampConnector Connector;
        public Entity BeltEntity;
    }

    private readonly Dictionary<GridCell, Record> byCell =
        new Dictionary<GridCell, Record>();
    private readonly Dictionary<DirectedEndpointKey, Record> byEntry =
        new Dictionary<DirectedEndpointKey, Record>();

    private readonly struct DirectedEndpointKey :
        System.IEquatable<DirectedEndpointKey>
    {
        public DirectedEndpointKey(RampEndpointKey endpoint, int2 direction)
        {
            Endpoint = endpoint;
            Direction = direction;
        }

        private RampEndpointKey Endpoint { get; }
        private int2 Direction { get; }
        public bool Equals(DirectedEndpointKey other) =>
            Endpoint.Equals(other.Endpoint) &&
            math.all(Direction == other.Direction);
        public override bool Equals(object obj) =>
            obj is DirectedEndpointKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                return (Endpoint.GetHashCode() * 397) ^
                       (Direction.x * 31 + Direction.y);
            }
        }
    }

    public bool IsReady { get; private set; }

    protected override void OnCreate() { }

    protected override void OnUpdate()
    {
        Dependency.Complete();
        Rebuild();
        IsReady = true;
    }

    public bool TryGet(GridCell cell, out Record record) =>
        byCell.TryGetValue(cell, out record);

    public bool ContainsCell(GridCell cell) => byCell.ContainsKey(cell);

    public void Register(Entity entity, in RampConnector connector)
    {
        Record record = new Record
        {
            ConnectorEntity = entity,
            Connector = connector,
            BeltEntity = Entity.Null
        };
        byCell[connector.Cell] = record;
        IndexEndpoints(record);
        IsReady = true;
    }

    public bool TryBuildBeltPath(
        GridCell start,
        GridCell end,
        int2 travelDirection,
        List<Record> path,
        out GridBuildFailureReason failure)
    {
        path.Clear();
        if (!byCell.TryGetValue(start, out Record current) ||
            !byCell.ContainsKey(end))
        {
            failure = GridBuildFailureReason.RampPathMustStayOnRamp;
            return false;
        }

        int2 direction = EcsGridUtility.SanitizeDirection(travelDirection);
        int2 delta = end.Horizontal - start.Horizontal;
        int length = math.abs(delta.x) + math.abs(delta.y);
        if ((direction.x != 0 &&
             (delta.y != 0 || delta.x != direction.x * length)) ||
            (direction.y != 0 &&
             (delta.x != 0 || delta.y != direction.y * length)))
        {
            failure = GridBuildFailureReason.RampPathNotCollinear;
            return false;
        }

        if (!RampUtility.IsTravelDirectionAllowed(current.Connector, direction))
        {
            failure = GridBuildFailureReason.RampDirectionInvalid;
            return false;
        }
        int signedRise = GetSignedRise(current.Connector, direction);
        for (int i = 0; i <= length; i++)
        {
            GridCell expectedCell = start + direction * i;
            if (!math.all(current.Connector.Cell.Horizontal ==
                          expectedCell.Horizontal))
            {
                failure = GridBuildFailureReason.RampPathIncomplete;
                path.Clear();
                return false;
            }
            if (!RampUtility.IsTravelDirectionAllowed(
                    current.Connector, direction) ||
                GetSignedRise(current.Connector, direction) != signedRise)
            {
                failure = GridBuildFailureReason.RampPathSlopeMismatch;
                path.Clear();
                return false;
            }

            path.Add(current);
            if (i == length)
            {
                if (current.Connector.Cell != end)
                {
                    failure = GridBuildFailureReason.RampPathIncomplete;
                    path.Clear();
                    return false;
                }
                failure = GridBuildFailureReason.None;
                return true;
            }

            RampEndpointKey exit = RampUtility.GetExitEndpoint(
                current.Connector, direction);
            if (!byEntry.TryGetValue(
                    new DirectedEndpointKey(exit, direction),
                    out current))
            {
                failure = GridBuildFailureReason.RampPathIncomplete;
                path.Clear();
                return false;
            }
        }

        failure = GridBuildFailureReason.RampPathIncomplete;
        path.Clear();
        return false;
    }

    public void RegisterBelt(GridCell cell, Entity belt)
    {
        if (byCell.TryGetValue(cell, out Record record))
            record.BeltEntity = belt;
    }

    public void UnregisterBelt(GridCell cell, Entity belt)
    {
        if (byCell.TryGetValue(cell, out Record record) &&
            record.BeltEntity == belt)
            record.BeltEntity = Entity.Null;
    }

    public void Unregister(GridCell cell, Entity connector)
    {
        if (byCell.TryGetValue(cell, out Record record) &&
            record.ConnectorEntity == connector)
        {
            RemoveEndpoints(record);
            byCell.Remove(cell);
        }
    }

    private void Rebuild()
    {
        byCell.Clear();
        byEntry.Clear();
        foreach ((RefRO<RampConnector> value, Entity entity) in
                 SystemAPI.Query<RefRO<RampConnector>>().WithEntityAccess())
        {
            RampConnector connector = value.ValueRO;
            Record record = new Record
            {
                ConnectorEntity = entity,
                Connector = connector,
                BeltEntity = Entity.Null
            };
            byCell[connector.Cell] = record;
            IndexEndpoints(record);
        }

        foreach ((RefRO<RampBelt> value, Entity entity) in
                 SystemAPI.Query<RefRO<RampBelt>>().WithEntityAccess())
        {
            RampBelt belt = value.ValueRO;
            if (EntityManager.Exists(belt.Connector) &&
                EntityManager.HasComponent<RampConnector>(belt.Connector))
            {
                GridCell cell = EntityManager
                    .GetComponentData<RampConnector>(belt.Connector).Cell;
                if (byCell.TryGetValue(cell, out Record record))
                    record.BeltEntity = entity;
            }
        }
    }

    private void IndexEndpoints(Record record)
    {
        RampConnector connector = record.Connector;
        byEntry[new DirectedEndpointKey(
            RampUtility.GetLowEndpoint(connector),
            connector.UphillDirection)] = record;
        byEntry[new DirectedEndpointKey(
            RampUtility.GetHighEndpoint(connector),
            -connector.UphillDirection)] = record;
    }

    private void RemoveEndpoints(Record record)
    {
        RampConnector connector = record.Connector;
        byEntry.Remove(new DirectedEndpointKey(
            RampUtility.GetLowEndpoint(connector),
            connector.UphillDirection));
        byEntry.Remove(new DirectedEndpointKey(
            RampUtility.GetHighEndpoint(connector),
            -connector.UphillDirection));
    }

    private static int GetSignedRise(
        in RampConnector connector,
        int2 direction) =>
        math.all(direction == connector.UphillDirection)
            ? connector.HighHeight.Units - connector.LowHeight.Units
            : connector.LowHeight.Units - connector.HighHeight.Units;
}
