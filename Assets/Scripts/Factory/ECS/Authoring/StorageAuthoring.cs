using Unity.Entities;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class StorageAuthoring : MonoBehaviour
{
    public Vector2Int direction = Vector2Int.right;
    [Min(1)] public int capacity = 100;

    private sealed class StorageBaker : Baker<StorageAuthoring>
    {
        public override void Bake(StorageAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            TransportGridBakingUtility.AddGridData(
                this,
                entity,
                BuildingKind.Storage,
                authoring.transform.position,
                authoring.direction);

            AddComponent(entity, new StorageState
            {
                TotalStored = 0,
                Capacity = Mathf.Max(1, authoring.capacity)
            });
            AddBuffer<StoredItemCount>(entity);
            AddBuffer<ItemInputPortCurrent>(entity);
            AddBuffer<ItemInputPortNext>(entity);
            AddBuffer<ItemOutputPortCurrent>(entity);
            AddBuffer<ItemOutputPortNext>(entity);
            AddBuffer<ItemTransferReceiptCurrent>(entity);
            AddBuffer<ItemTransferReceiptNext>(entity);
        }
    }
}
