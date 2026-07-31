using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BeltAuthoring : MonoBehaviour
{
    [Min(0f)] public float speed = 1f;
    public Vector2Int direction = Vector2Int.right;
    public GameObject initialItem;
    public GameObject eastEdge;
    public GameObject northEdge;
    public GameObject westEdge;
    public GameObject southEdge;
    public GameObject directionTriangle;

    private sealed class BeltBaker : Baker<BeltAuthoring>
    {
        public override void Bake(BeltAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            Vector3 position = authoring.transform.position;
            TransportGridBakeResult gridData =
                TransportGridBakingUtility.AddGridData(
                    this,
                    entity,
                    BuildingKind.Belt,
                    position,
                    authoring.direction);

            AddComponent(entity, new Belt
            {
                Speed = Mathf.Max(0f, authoring.speed),
                Cell = gridData.Cell,
                Direction = gridData.Direction,
                NextCell = gridData.Cell + gridData.Direction,
                CurrentItem = authoring.initialItem == null
                    ? Entity.Null
                    : GetEntity(authoring.initialItem, TransformUsageFlags.Dynamic),
                Progress = 0f,
                IsLoop = false,
                HasOutput = false
            });

            if (authoring.eastEdge != null &&
                authoring.northEdge != null &&
                authoring.westEdge != null &&
                authoring.southEdge != null &&
                authoring.directionTriangle != null)
            {
                AddComponent(entity, new BeltVisualParts
                {
                    EastEdge = GetEntity(
                        authoring.eastEdge,
                        TransformUsageFlags.Renderable),
                    NorthEdge = GetEntity(
                        authoring.northEdge,
                        TransformUsageFlags.Renderable),
                    WestEdge = GetEntity(
                        authoring.westEdge,
                        TransformUsageFlags.Renderable),
                    SouthEdge = GetEntity(
                        authoring.southEdge,
                        TransformUsageFlags.Renderable),
                    DirectionTriangle = GetEntity(
                        authoring.directionTriangle,
                        TransformUsageFlags.Renderable)
                });
            }
        }
    }
}
