using System;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

/// <summary>Seeds the player's starting platform through the normal foundation registry.</summary>
internal sealed class PlayerSpawnFoundation : IDisposable
{
    private readonly Vector3 spawnPosition;
    private World world;
    private EntityQuery gridQuery;
    private EntityQuery physicsQuery;
    private bool hasQueries;
    private bool seeded;
    private float surfaceY;
    private float3 supportPoint;

    public bool IsReady { get; private set; }

    public PlayerSpawnFoundation(Vector3 position) => spawnPosition = position;

    public bool TryInitialize()
    {
        if (IsReady) return true;
        World current = World.DefaultGameObjectInjectionWorld;
        if (current != world)
        {
            Dispose();
            world = current;
            seeded = false;
            if (world != null && world.IsCreated)
            {
                gridQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldGridConfig>());
                physicsQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PhysicsWorldSingleton>());
                hasQueries = true;
            }
        }
        if (!hasQueries || world == null || !world.IsCreated || gridQuery.CalculateEntityCount() != 1)
            return false;
        SurfaceRegistrySystem surfaces = world.GetExistingSystemManaged<SurfaceRegistrySystem>();
        if (surfaces == null || !surfaces.IsReady) return false;
        if (!seeded)
        {
            WorldGridConfig config = gridQuery.GetSingleton<WorldGridConfig>();
            GridCell foot = EcsGridUtility.WorldToCell((float3)spawnPosition, config);
            foot.Level = math.max(0, (int)math.floor((spawnPosition.y - config.Origin.y) / config.LayerHeight));
            surfaceY = EcsGridUtility.LevelToWorldY(foot.Level, config);
            supportPoint = EcsGridUtility.CellToWorldCenter(foot, config);
            Entity grid = gridQuery.GetSingletonEntity();
            for (int z = foot.Z - 5; z < foot.Z + 5; z++)
            for (int x = foot.X - 5; x < foot.X + 5; x++)
                surfaces.AddFoundation(grid, new GridCell(x, foot.Level, z), 0, SurfacePermission.All, true);
            seeded = true;
        }
        // Keep gravity paused until the normal dirty-chunk pipeline has built the collision world.
        if (physicsQuery.CalculateEntityCount() != 1) return false;
        world.EntityManager.CompleteDependencyBeforeRO<PhysicsWorldSingleton>();
        PhysicsWorldSingleton physics = physicsQuery.GetSingleton<PhysicsWorldSingleton>();
        var ray = new RaycastInput
        {
            Start = supportPoint + new float3(0f, 0.1f, 0f),
            End = supportPoint - new float3(0f, 0.1f, 0f),
            Filter = FoundationCollisionCategories.FoundationQueryFilter
        };
        if (!physics.CastRay(ray, out Unity.Physics.RaycastHit hit) || math.abs(hit.Position.y - surfaceY) > 0.02f)
            return false;
        IsReady = true;
        Dispose();
        return true;
    }

    public void Dispose()
    {
        if (hasQueries && world != null && world.IsCreated)
        {
            gridQuery.Dispose();
            physicsQuery.Dispose();
        }
        hasQueries = false;
    }
}
