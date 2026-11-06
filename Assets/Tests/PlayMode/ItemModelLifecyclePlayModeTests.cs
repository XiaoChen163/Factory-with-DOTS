using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Factory.Tests
{
    public sealed class ItemModelLifecyclePlayModeTests
    {
        [UnityTest]
        public IEnumerator LastFbxItemEnteringStorage_HidesAllMeshEntities()
        {
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            var world = World.DefaultGameObjectInjectionWorld;
            var manager = world.EntityManager;
            using var catalogQuery = manager.CreateEntityQuery(typeof(FactoryDatabase), typeof(ItemPrefabEntry));
            for (int frame = 0; frame < 240 && catalogQuery.IsEmptyIgnoreFilter; frame++) yield return null;
            Assert.That(catalogQuery.IsEmptyIgnoreFilter, Is.False);
            var catalog = catalogQuery.GetSingletonEntity();
            var database = manager.GetComponentData<FactoryDatabase>(catalog).Value;
            BuildingDefinitionId storageId = default, beltId = default;
            for (int i = 1; i < database.Value.BuildingDefinitionsById.Length; i++)
            {
                var definition = database.Value.BuildingDefinitionsById[i];
                if (definition.Kind == BuildingKind.Storage) storageId = definition.Id;
                if (definition.Kind == BuildingKind.Belt && !beltId.IsValid) beltId = definition.Id;
            }
            Assert.That(storageId.IsValid && beltId.IsValid, Is.True);
            var surfaces = world.GetExistingSystemManaged<SurfaceRegistrySystem>();
            var occupancy = world.GetExistingSystemManaged<GridOccupancyIndexSystem>();
            for (int frame = 0; frame < 240 && (!surfaces.IsReady || !occupancy.IsReady); frame++) yield return null;
            Assert.That(surfaces.IsReady && occupancy.IsReady, Is.True);
            using var gridQuery = manager.CreateEntityQuery(typeof(WorldGridConfig));
            var gridEntity = gridQuery.GetSingletonEntity();
            var grid = gridQuery.GetSingleton<WorldGridConfig>();
            var anchor = new GridCell(330, 3, 330);
            var beltCell = anchor + new int2(-1, 1);
            manager.CompleteAllTrackedJobs();
            for (int x = -1; x < 3; x++) for (int z = 0; z < 3; z++)
                surfaces.AddFoundation(gridEntity, anchor + new int2(x, z), 0, SurfacePermission.All, true);
            var commands = manager.GetBuffer<GridBuildCommand>(gridEntity);
            commands.Add(new GridBuildCommand { Type = GridBuildCommandType.Place, Kind = BuildingKind.Storage,
                BuildingDefinition = storageId, StartCell = anchor, EndCell = anchor });
            commands.Add(new GridBuildCommand { Type = GridBuildCommandType.Place, Kind = BuildingKind.Belt,
                BuildingDefinition = beltId, StartCell = beltCell, EndCell = beltCell });
            Entity storage = Entity.Null, belt = Entity.Null;
            for (int frame = 0; frame < 120 &&
                (!occupancy.TryGetOccupant(anchor, out storage) || !occupancy.TryGetOccupant(beltCell, out belt)); frame++) yield return null;
            Assert.That(storage, Is.Not.EqualTo(Entity.Null));
            Assert.That(belt, Is.Not.EqualTo(Entity.Null));
            manager.CompleteAllTrackedJobs();
            var prefab = manager.GetBuffer<ItemPrefabEntry>(catalog)[0].Prefab;
            Entity item = manager.Instantiate(prefab);
            try
            {
                var meshes = new List<Entity>();
                foreach (var member in manager.GetBuffer<LinkedEntityGroup>(item))
                    if (manager.HasComponent<MaterialMeshInfo>(member.Value)) meshes.Add(member.Value);
                Assert.That(meshes.Count, Is.GreaterThan(0));
                Assert.That(meshes.Contains(item), Is.False, "Exercise the real FBX child-mesh hierarchy.");
                var transform = manager.GetComponentData<LocalTransform>(item);
                transform.Position = EcsGridUtility.CellToWorldCenter(beltCell, grid) + new float3(0, .535f, 0);
                manager.SetComponentData(item, transform);
                manager.SetComponentData(item, new ItemVisualState
                { FromPosition = transform.Position, ToPosition = transform.Position, Progress = 1 });
                manager.SetComponentData(belt, new BeltState { CurrentItem = item, Progress = 1 });
                for (int frame = 0; frame < 180 && manager.IsComponentEnabled<Item>(item); frame++) yield return null;
                manager.CompleteAllTrackedJobs();
                Assert.That(manager.IsComponentEnabled<Item>(item), Is.False, "The storage must accept the last item.");
                Assert.That(manager.GetComponentData<BeltState>(belt).CurrentItem, Is.EqualTo(Entity.Null));
                for (int frame = 0; frame < 5; frame++) yield return null;
                foreach (Entity mesh in meshes)
                    Assert.That(manager.HasComponent<DisableRendering>(mesh), Is.True, "No FBX mesh may remain visible at the machine entrance.");
            }
            finally
            {
                manager.CompleteAllTrackedJobs();
                if (manager.Exists(item)) manager.DestroyEntity(item);
                var remove = manager.GetBuffer<GridBuildCommand>(gridEntity);
                remove.Add(new GridBuildCommand { Type = GridBuildCommandType.Remove, StartCell = beltCell, EndCell = beltCell });
                remove.Add(new GridBuildCommand { Type = GridBuildCommandType.Remove, StartCell = anchor, EndCell = anchor });
            }
        }
    }
}
