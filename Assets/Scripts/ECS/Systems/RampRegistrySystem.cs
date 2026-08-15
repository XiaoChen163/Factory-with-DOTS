using System.Collections.Generic;
using Unity.Entities;

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
        byCell[connector.Cell] = new Record
        {
            ConnectorEntity = entity,
            Connector = connector,
            BeltEntity = Entity.Null
        };
        IsReady = true;
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
            byCell.Remove(cell);
    }

    private void Rebuild()
    {
        byCell.Clear();
        foreach ((RefRO<RampConnector> value, Entity entity) in
                 SystemAPI.Query<RefRO<RampConnector>>().WithEntityAccess())
        {
            RampConnector connector = value.ValueRO;
            byCell[connector.Cell] = new Record
            {
                ConnectorEntity = entity,
                Connector = connector,
                BeltEntity = Entity.Null
            };
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
}
