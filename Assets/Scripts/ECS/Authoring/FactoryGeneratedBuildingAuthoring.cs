using Unity.Entities;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class FactoryGeneratedBuildingAuthoring : MonoBehaviour
{
    public string buildingKey;
    public const string ModelNode = "Factory Model";
    public const string CollisionNode = "Factory Collisions";

    private sealed class GeneratedBaker : Baker<FactoryGeneratedBuildingAuthoring>
    {
        public override void Bake(FactoryGeneratedBuildingAuthoring authoring)
        {
            var source = GetComponentInChildren<FactoryBuildingCollisionAuthoring>();
            if (source == null || source.transform.parent != authoring.transform || source.name != CollisionNode) return;
            var blob = FactoryBuildingColliderBuilder.Build(source.shapes);
            if (!blob.IsCreated) return;
            AddBlobAsset(ref blob, out _);
            AddComponent(GetEntity(TransformUsageFlags.Dynamic), new FactoryBuildingCollider { Value = blob });
        }
    }
}
