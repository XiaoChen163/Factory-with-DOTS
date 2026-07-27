using Unity.Entities;
using UnityEngine;

public class StundedAuth : MonoBehaviour
{
    private class StundedAuthBaker : Baker<StundedAuth>
    {
        public override void Bake(StundedAuth authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Stunned());
            SetComponentEnabled<Stunned>(entity, false);
        }
    }
}

public struct Stunned : IComponentData, IEnableableComponent
{
    
}