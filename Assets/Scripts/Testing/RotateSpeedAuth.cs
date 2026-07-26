using System.Collections.Generic;
using System.Collections;
using Unity.Entities;
using UnityEngine;

public class RotateSpeedAuth : MonoBehaviour
{
    public float Value;
    private class RotateSpeedAuthBaker : Baker<RotateSpeedAuth>
    {
        public override void Bake(RotateSpeedAuth authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new RotateSpeed { Value = authoring.Value });
        }
    }
}

public struct RotateSpeed : IComponentData
{
    public float Value;
}

