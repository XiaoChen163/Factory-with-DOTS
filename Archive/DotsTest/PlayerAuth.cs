using System.ComponentModel;
using UnityEngine;
using Unity.Entities;
public class PlayerAuth : MonoBehaviour
{
    public class PlayerAuthBaker : Baker<PlayerAuth>
    {
        public override void Bake(PlayerAuth authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Player());
        }
    }
}

public struct Player : IComponentData
{
    
}