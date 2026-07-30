using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ItemAuthoring : MonoBehaviour
{
    public GameObject itemType;

    private sealed class ItemBaker : Baker<ItemAuthoring>
    {
        public override void Bake(ItemAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            Vector3 position = authoring.transform.position;

            AddComponent(entity, new Item
            {
                ItemType = authoring.itemType == null
                    ? Entity.Null
                    : GetEntity(authoring.itemType, TransformUsageFlags.None),
                Position = new float3(position.x, position.y, position.z)
            });
        }
    }
}
