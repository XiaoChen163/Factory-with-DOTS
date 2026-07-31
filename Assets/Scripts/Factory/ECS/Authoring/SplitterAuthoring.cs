using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SplitterAuthoring : MonoBehaviour
{
    public Vector2Int direction = Vector2Int.right;
    public GameObject initialItem;

    private sealed class SplitterBaker : Baker<SplitterAuthoring>
    {
        public override void Bake(SplitterAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            Vector3 position = authoring.transform.position;
            TransportGridBakeResult gridData =
                TransportGridBakingUtility.AddGridData(
                    this,
                    entity,
                    BuildingKind.Splitter,
                    position,
                    authoring.direction);

            AddComponent(entity, new Splitter
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
                NextOutputIndex = 0
            });
        }
    }
}
