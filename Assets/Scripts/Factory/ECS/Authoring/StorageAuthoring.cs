using Unity.Entities;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class StorageAuthoring : MonoBehaviour
{
    public Vector2Int direction = Vector2Int.right;

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
                TotalStored = 0
            });
            AddBuffer<StoredItemCount>(entity);
        }
    }
}
