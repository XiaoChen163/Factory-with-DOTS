using System;
using System.IO;
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

public static class FactoryBuildingColliderBuilder
{
    public static void Validate(FactoryColliderProxy c)
    {
        if (c == null) throw new InvalidDataException("碰撞代理不能为 null。");
        foreach (float v in new[] { c.position.x, c.position.y, c.position.z, c.rotation.x, c.rotation.y, c.rotation.z, c.rotation.w,
            c.size.x, c.size.y, c.size.z, c.radius, c.height }) if (float.IsNaN(v) || float.IsInfinity(v)) throw new InvalidDataException("碰撞体包含非有限数值。");
        var q = c.rotation;
        if (System.Math.Abs(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w - 1f) > .001f) throw new InvalidDataException("代理旋转必须为单位四元数。");
        bool valid = c.type == "Box" ? c.size.x > 0 && c.size.y > 0 && c.size.z > 0 :
            c.type == "Sphere" ? c.radius > 0 :
            c.type == "Capsule" ? c.radius > 0 && c.height >= 2 * c.radius && c.axis == "Y" :
            c.type == "Cylinder" && c.radius > 0 && c.height > 0 && c.axis == "Y";
        if (!valid) throw new InvalidDataException($"代理 '{c.name}' 的形状、尺寸或轴无效。");
    }

    public static BlobAssetReference<Collider> Build(FactoryColliderProxy[] shapes)
    {
        if (shapes == null || shapes.Length == 0) return default;
        foreach (var shape in shapes) Validate(shape);
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
