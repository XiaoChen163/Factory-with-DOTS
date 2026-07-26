using Unity.Entities;
using UnityEngine;
using Unity.Mathematics;

public class MovementAuth : MonoBehaviour
{
    public class MovementAuthBaker : Baker<MovementAuth>
    {
        public override void Bake(MovementAuth authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Movement
            {
                movementVector = new float3(UnityEngine.Random.Range(-1f,1f),0,UnityEngine.Random.Range(-1f,1f))
            });
        }
    }
}

public struct Movement : IComponentData
{
    public float3 movementVector;
}
