
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public partial class SpawnCubeSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<SpawnCubesConfig>();
    }

    protected override void OnUpdate()
    {
        this.Enabled = false;
        SpawnCubesConfig spawnCubesConfig = SystemAPI.GetSingleton<SpawnCubesConfig>();

        for (int i = 0; i < spawnCubesConfig.spawnAmount; i++)
        {
            Entity spawnedEntity = EntityManager.Instantiate(spawnCubesConfig.cubePrefabEntity);
            SystemAPI.SetComponent(spawnedEntity,new LocalTransform
            {
                Position = new float3(UnityEngine.Random.Range(-10f,5),0.6f,UnityEngine.Random.Range(-4f,7)),
                Rotation = quaternion.identity,
                Scale = 1f
            });
        }
    }
}
