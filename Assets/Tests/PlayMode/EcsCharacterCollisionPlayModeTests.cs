using System.Collections;
using NUnit.Framework;
using Unity.CharacterController;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Factory.Tests
{
    public sealed class EcsCharacterCollisionPlayModeTests
    {
        private World world;
        private EcsCharacterMotor motor;
        private Entity entry;
        private WorldGridConfig grid;
        private Vector3 origin;
        private readonly System.Collections.Generic.List<GridCell> createdSurfaces = new();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            var player = Object.FindFirstObjectByType<FirstPersonPlayerController>();
            for (int frame = 0; frame < 240 && !player.IsSpawnFoundationReady; frame++) yield return null;
            Assert.That(player.IsSpawnFoundationReady, Is.True);
            motor = player.GetComponent<EcsCharacterMotor>();
            Assert.That(motor.IsCreated, Is.True);
            Assert.That(player.GetComponent<UnityEngine.CharacterController>(), Is.Null);
            player.enabled = false;
            world = World.DefaultGameObjectInjectionWorld;
            EntityManager manager = world.EntityManager;
            Assert.That(manager.HasComponent<KinematicCharacterBody>(motor.CharacterEntity), Is.True);
            Assert.That(manager.HasComponent<PhysicsCollider>(motor.CharacterEntity), Is.True);
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<WorldGridConfig>());
            grid = query.GetSingleton<WorldGridConfig>();
            origin = new Vector3(100.5f, 0f, 100.5f);
            entry = manager.CreateEntity();
            manager.AddComponentData(entry, LocalTransform.FromPosition(origin));
            manager.AddComponentData(entry, new LocalToWorld { Value = float4x4.Translate(origin) });
            manager.AddSharedComponentManaged(entry, new PhysicsWorldIndex { Value = 0 });
            manager.AddComponentData(entry, new PhysicsCollider
            {
                Value = world.GetOrCreateSystemManaged<VerticalBeltColliderCacheSystem>().GetOrCreate(grid, false)
            });
            yield return new WaitForSeconds(0.1f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            motor?.Suspend();
            if (world != null && world.IsCreated)
            {
                var surfaces = world.GetExistingSystemManaged<SurfaceRegistrySystem>();
                using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldGridConfig>());
                if (query.CalculateEntityCount() == 1)
                    foreach (GridCell cell in createdSurfaces)
                        if (surfaces.RemoveFoundation(query.GetSingletonEntity(), cell, out Entity foundation))
                            world.EntityManager.DestroyEntity(foundation);
            }
            createdSurfaces.Clear();
            if (world != null && world.IsCreated && world.EntityManager.Exists(entry))
                world.EntityManager.DestroyEntity(entry);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EntranceEdge_ZeroFractionContact_AllowsOutwardAndTangentialEscape()
        {
            Vector3 normal = new Vector3(1f, 1f, 0f).normalized;
            Vector3 corner = origin + new Vector3(grid.CellSize * 0.375f, grid.LayerHeight * 0.875f, 0f);
            Vector3 start = corner + normal * (EcsCharacterMotor.Radius + 0.0005f) - Vector3.up * EcsCharacterMotor.Radius;
            foreach (Vector3 direction in new[] { Vector3.right, Vector3.up, Vector3.forward, Vector3.back })
            {
                motor.Teleport(start);
                world.EntityManager.SetComponentData(motor.CharacterEntity, new FactoryCharacterState { IsFlying = true });
                motor.SubmitControl(new FactoryCharacterControl
                {
                    Active = true, Controlling = true, Rotation = quaternion.identity,
                    MoveVector = direction.y == 0f ? (float3)direction : float3.zero,
                    Ascend = direction.y > 0f
                });
                yield return new WaitForSeconds(0.2f);
                Vector3 end = world.EntityManager.GetComponentData<LocalTransform>(motor.CharacterEntity).Position;
                Assert.That(Vector3.Dot(end - start, direction), Is.GreaterThan(0.15f),
                    "The old motor froze in every direction at this 0.5mm edge contact. Direction: " + direction);
                motor.Suspend();
            }
        }

        [UnityTest]
        public IEnumerator ChunkSeam_RemainsGroundedAndStopsWhenControlBlocked()
        {
            motor.SubmitControl(new FactoryCharacterControl
            {
                Active = true, Controlling = true, Rotation = quaternion.identity, MoveVector = new float3(0, 0, 1)
            });
            yield return new WaitForSeconds(0.3f);
            var diagnosticBody = world.EntityManager.GetComponentData<KinematicCharacterBody>(motor.CharacterEntity);
            Assert.That(motor.IsGrounded, Is.True,
                "pose=" + world.EntityManager.GetComponentData<LocalTransform>(motor.CharacterEntity).Position +
                ", velocity=" + diagnosticBody.RelativeVelocity + ", normal=" + diagnosticBody.GroundHit.Normal);
            Assert.That(motor.Velocity.z, Is.GreaterThan(2f));
            float3 before = world.EntityManager.GetComponentData<LocalTransform>(motor.CharacterEntity).Position;
            motor.SubmitControl(new FactoryCharacterControl { Active = true, Rotation = quaternion.identity });
            yield return new WaitForSeconds(0.1f);
            float3 after = world.EntityManager.GetComponentData<LocalTransform>(motor.CharacterEntity).Position;
            Assert.That(math.distance(before.xz, after.xz), Is.LessThan(0.001f));
        }

        [UnityTest]
        public IEnumerator WalkingIntoEntrance_CanReverseAwayAfterContact()
        {
            var surfaces = world.GetExistingSystemManaged<SurfaceRegistrySystem>();
            using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldGridConfig>());
            for (int z = 98; z < 103; z++)
            for (int x = 98; x < 104; x++)
            {
                var cell = new GridCell(x, 0, z);
                if (surfaces.AddFoundation(query.GetSingletonEntity(), cell, 0, SurfacePermission.All, true) != Entity.Null)
                    createdSurfaces.Add(cell);
            }
            yield return new WaitForSeconds(0.1f);
            motor.Teleport(origin + new Vector3(1.2f, 0.02f, 0f));
            motor.SubmitControl(new FactoryCharacterControl
            {
                Active = true, Controlling = true, Rotation = quaternion.identity, MoveVector = new float3(-1, 0, 0)
            });
            yield return new WaitForSeconds(0.5f);
            float blockedX = world.EntityManager.GetComponentData<LocalTransform>(motor.CharacterEntity).Position.x;
            motor.SubmitControl(new FactoryCharacterControl
            {
                Active = true, Controlling = true, Rotation = quaternion.identity, MoveVector = new float3(1, 0, 0)
            });
            yield return new WaitForSeconds(0.3f);
            float escapedX = world.EntityManager.GetComponentData<LocalTransform>(motor.CharacterEntity).Position.x;
            Assert.That(escapedX, Is.GreaterThan(blockedX + 0.3f));
        }
    }
}
