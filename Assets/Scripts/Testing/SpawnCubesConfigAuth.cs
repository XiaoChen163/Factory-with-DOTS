using System.ComponentModel;
using Unity.Entities;
using UnityEngine;

public class SpawnCubesConfigAuth : MonoBehaviour
{
    public GameObject cubePrefab;
    public int spawnAmount;

    private class SpawnCubesConfigAuthBaker : Baker<SpawnCubesConfigAuth>
    {
        public override void Bake(SpawnCubesConfigAuth authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent(entity, new SpawnCubesConfig
            {
                cubePrefabEntity = GetEntity(authoring.cubePrefab, TransformUsageFlags.Dynamic),
                spawnAmount = authoring.spawnAmount
            });
        }
    }
}

public struct SpawnCubesConfig : IComponentData
{
    public Entity cubePrefabEntity;
    public int spawnAmount;
}
