#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Collider = Unity.Physics.Collider;

namespace Factory.Tests
{
    public sealed class ImportedBuildingPhysicsPlayModeTests
    {
        [Serializable] private sealed class Fixture { public FactoryColliderProxy[] colliders; }

        [UnityTest]
        public IEnumerator EcsScene_TestMachineCollider_EntersPhysicsWorldAndDisappearsOnRemoval()
        {
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            yield return null;
            var world = World.DefaultGameObjectInjectionWorld;
            var manager = world.EntityManager;
            using var catalogQuery = manager.CreateEntityQuery(typeof(FactoryDatabase), typeof(BuildingVisualPrefabEntry));
            for (int frame = 0; frame < 240 && catalogQuery.IsEmptyIgnoreFilter; frame++) yield return null;
            Assert.That(catalogQuery.IsEmptyIgnoreFilter, Is.False);
            var catalog = catalogQuery.GetSingletonEntity();
            var database = manager.GetComponentData<FactoryDatabase>(catalog).Value;
            BuildingDefinitionId id = default;
            for (int i = 1; i < database.Value.BuildingDefinitionsById.Length; i++)
                if (database.Value.BuildingDefinitionsById[i].Kind == BuildingKind.Storage) { id = database.Value.BuildingDefinitionsById[i].Id; break; }
            Assert.That(id.Value, Is.Not.Zero);
            var prefab = BuildingPrefabCatalogUtility.GetPrefab(manager.GetBuffer<BuildingVisualPrefabEntry>(catalog), id);
            var fixture = JsonUtility.FromJson<Fixture>(File.ReadAllText(Path.Combine(Application.dataPath, "../Tools/Blender/Fixtures/testMachine/testMachine.factory.json")));
            BlobAssetReference<Collider> blob = FactoryBuildingColliderBuilder.Build(fixture.colliders);
            var original = default(BuildingVisualPrefabEntry);
            int entryIndex = -1;
            for (int i = 0; i < manager.GetBuffer<BuildingVisualPrefabEntry>(catalog).Length; i++)
                if (manager.GetBuffer<BuildingVisualPrefabEntry>(catalog)[i].BuildingDefinition == id) { entryIndex = i; break; }
            Assert.That(entryIndex, Is.GreaterThanOrEqualTo(0));
            original = manager.GetBuffer<BuildingVisualPrefabEntry>(catalog)[entryIndex];
            Entity placed = Entity.Null;
            try
            {
                manager.CompleteAllTrackedJobs();
                var replacement = original; replacement.Collider = blob;
                var entries = manager.GetBuffer<BuildingVisualPrefabEntry>(catalog);
                entries[entryIndex] = replacement;
                using var gridQuery = manager.CreateEntityQuery(typeof(WorldGridConfig));
                Entity grid = gridQuery.GetSingletonEntity();
                var surfaces = world.GetExistingSystemManaged<SurfaceRegistrySystem>();
                var occupancy = world.GetExistingSystemManaged<GridOccupancyIndexSystem>();
                for (int frame = 0; frame < 240 && (!surfaces.IsReady || !occupancy.IsReady); frame++) yield return null;
                Assert.That(surfaces.IsReady && occupancy.IsReady, Is.True);
                var anchor = new GridCell(130, 3, 130);
                for (int x = 0; x < 3; x++) for (int z = 0; z < 3; z++)
                    surfaces.AddFoundation(grid, anchor + new int2(x, z), 0, SurfacePermission.All, true);
                manager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand { Type = GridBuildCommandType.Place, Kind = BuildingKind.Storage, BuildingDefinition = id, StartCell = anchor, EndCell = anchor, QuarterTurns = 1 });
                string failure = "No result";
                for (int frame = 0; frame < 120 && !occupancy.TryGetOccupant(anchor, out placed); frame++)
                {
                    foreach (var result in manager.GetBuffer<GridBuildResult>(grid)) failure = result.FailureReason.ToString();
                    yield return null;
                }
                Assert.That(placed, Is.Not.EqualTo(Entity.Null), failure);
                yield return new WaitForFixedUpdate(); yield return null;
                var transform = manager.GetComponentData<LocalTransform>(placed);
                float3 local = new float3(.6f, 1.94f, .75f);
                float3 point = transform.Position + math.rotate(transform.Rotation, local);
                var ray = new RaycastInput { Start = point + new float3(0, 5, 0), End = point - new float3(0, 5, 0), Filter = CollisionFilter.Default };
                using var physicsQuery = manager.CreateEntityQuery(typeof(PhysicsWorldSingleton));
                Assert.That(physicsQuery.GetSingleton<PhysicsWorldSingleton>().CastRay(ray, out Unity.Physics.RaycastHit hit), Is.True);
                Assert.That(hit.Entity, Is.EqualTo(placed), "Placed imported proxies must be real static physics bodies.");
                manager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand { Type = GridBuildCommandType.Remove, StartCell = anchor, EndCell = anchor });
                for (int frame = 0; frame < 120 && manager.Exists(placed); frame++) yield return null;
                Assert.That(manager.Exists(placed), Is.False);
                yield return new WaitForFixedUpdate(); yield return null;
                bool anyHit = physicsQuery.GetSingleton<PhysicsWorldSingleton>().CastRay(ray, out hit);
                Assert.That(anyHit && hit.Entity == placed, Is.False);
            }
            finally
            {
                manager.CompleteAllTrackedJobs();
                if (manager.Exists(placed)) manager.DestroyEntity(placed);
                if (manager.Exists(catalog))
                {
                    var entries = manager.GetBuffer<BuildingVisualPrefabEntry>(catalog);
                    entries[entryIndex] = original;
                }
                blob.Dispose();
            }
        }
    }
}
#endif
