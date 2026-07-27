
using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.EventSystems;

public partial class PlayerShootingSystem : SystemBase
{
    public Action<Entity> OnShoot;
    
    protected override void OnCreate()
    {
        RequireForUpdate<Player>();
    }

    private bool stunEnabled = false;
    
    protected override void OnUpdate()
    {
        if (Input.GetKeyDown(KeyCode.T))
        {
            if (!stunEnabled)
            {
                Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();
                EntityManager.SetComponentEnabled<Stunned>(playerEntity,true);
                stunEnabled = true;
            }
            else
            {
                Entity playerEntity = SystemAPI.GetSingletonEntity<Player>();
                EntityManager.SetComponentEnabled<Stunned>(playerEntity,false);
                stunEnabled = false;
            }
        }
        if (!Input.GetKeyDown(KeyCode.Space)) return;
        
        SpawnCubesConfig spawnCubesConfig = SystemAPI.GetSingleton<SpawnCubesConfig>();
        EntityCommandBuffer entityCommandBuffer = new EntityCommandBuffer(WorldUpdateAllocator);
        foreach ((var localTransfrom, Entity entity) in 
                 SystemAPI.Query<RefRO<LocalTransform>>().WithAll<Player>().WithDisabled<Stunned>().WithEntityAccess())
        {
            Entity spawnedEntity = entityCommandBuffer.Instantiate(spawnCubesConfig.cubePrefabEntity);
            LocalTransform localTransform = new LocalTransform
            {
                Position = localTransfrom.ValueRO.Position,
                Rotation = quaternion.identity,
                Scale = 1f
            };
            entityCommandBuffer.SetComponent (spawnedEntity,localTransform);
            OnShoot?.Invoke(entity);
        }
        
        entityCommandBuffer.Playback(EntityManager);
    }
}
