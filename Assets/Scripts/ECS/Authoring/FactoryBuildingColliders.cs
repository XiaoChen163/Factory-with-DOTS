using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;
using Collider = Unity.Physics.Collider;

[Serializable]
public sealed class FactoryColliderProxy
{
    public string name, type, axis;
    public Vector3 position;
    public Quaternion rotation = Quaternion.identity;
    public Vector3 size;
    public float radius, height;
}

// Baked blobs belong to the baking store, and are shared by placed instances.
public struct FactoryBuildingCollider : IComponentData
{
    public BlobAssetReference<Collider> Value;
}

public static class FactoryBuildingColliderBuilder
{
    public static BlobAssetReference<Collider> Build(FactoryColliderProxy[] shapes)
    {
        if (shapes == null || shapes.Length == 0) return default;
        var children = new NativeArray<CompoundCollider.ColliderBlobInstance>(shapes.Length, Allocator.Temp);
        try
        {
            for (int i = 0; i < shapes.Length; i++)
            {
                var s = shapes[i];
                BlobAssetReference<Collider> child;
                switch (s.type)
                {
                    case "Box":
                        child = Unity.Physics.BoxCollider.Create(new BoxGeometry { Size = s.size, Orientation = quaternion.identity, BevelRadius = 0 }, FoundationCollisionCategories.BuildingFilter);
                        break;
                    case "Sphere":
                        child = Unity.Physics.SphereCollider.Create(new SphereGeometry { Radius = s.radius }, FoundationCollisionCategories.BuildingFilter);
                        break;
                    case "Capsule":
                        float half = s.height * .5f - s.radius;
                        child = Unity.Physics.CapsuleCollider.Create(new CapsuleGeometry { Radius = s.radius, Vertex0 = new float3(0, -half, 0), Vertex1 = new float3(0, half, 0) }, FoundationCollisionCategories.BuildingFilter);
                        break;
                    case "Cylinder":
                        // Unity Physics cylinders are Z aligned; the protocol uses Y.
                        child = Unity.Physics.CylinderCollider.Create(new CylinderGeometry { Radius = s.radius, Height = s.height, Orientation = quaternion.RotateX(math.PI * .5f), BevelRadius = 0, SideCount = 32 }, FoundationCollisionCategories.BuildingFilter);
                        break;
                    default: throw new ArgumentException("Unknown building collider: " + s.type);
                }
                children[i] = new CompoundCollider.ColliderBlobInstance { Collider = child, CompoundFromChild = new RigidTransform(s.rotation, s.position) };
            }
            return CompoundCollider.Create(children);
        }
        finally
        {
            for (int i = 0; i < children.Length; i++)
                if (children[i].Collider.IsCreated) children[i].Collider.Dispose();
            children.Dispose();
        }
    }
}
