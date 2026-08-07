using System.ComponentModel;
using Unity.Entities;
using UnityEngine;

public class RotatingCubeAuth : MonoBehaviour
{
    public class RotatingCubeAuthBaker : Baker<RotatingCubeAuth>
    {
        public override void Bake(RotatingCubeAuth authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new RotatingCube());
        }
    }
}

public struct RotatingCube : IComponentData
{
    
}
