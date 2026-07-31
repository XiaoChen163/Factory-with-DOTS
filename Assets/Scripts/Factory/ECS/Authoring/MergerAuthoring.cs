using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MergerAuthoring : MonoBehaviour
{
    public Vector2Int direction = Vector2Int.right;
    public GameObject initialItem;

    private sealed class MergerBaker : Baker<MergerAuthoring>
    {
        public override void Bake(MergerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            Vector3 position = authoring.transform.position;
            TransportGridBakeResult gridData =
                TransportGridBakingUtility.AddGridData(
                    this,
                    entity,
                    BuildingKind.Merger,
                    position,
                    authoring.direction);

            AddComponent(entity, new Merger
            {
                Cell = gridData.Cell,
                Direction = gridData.Direction,
                CurrentItem = authoring.initialItem == null
                    ? Entity.Null
                    : GetEntity(
                        authoring.initialItem,
                        TransformUsageFlags.Dynamic),
                TransferElapsed = 0f,
                InputInterval = 0f,
                NextInputIndex = 0
            });
        }
    }
}
