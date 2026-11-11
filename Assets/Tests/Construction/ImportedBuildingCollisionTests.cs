using System;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using Collider = Unity.Physics.Collider;
using RaycastHit = Unity.Physics.RaycastHit;
using Object = UnityEngine.Object;

namespace Factory.Tests
{
    public sealed class ImportedBuildingCollisionTests : FactoryWorldFixture
    {
        [Test]
        public void FourProxyShapes_PreservePoseAndCylinderFlatEnds()
        {
            foreach (string type in new[] { "Box", "Sphere", "Capsule", "Cylinder" })
            {
                var shape = new FactoryColliderProxy { type = type, position = new Vector3(1, 3, 2), rotation = Quaternion.Euler(0, 90, 0), size = new Vector3(2, 4, 6), radius = 1, height = 4, axis = "Y" };
                var blob = FactoryBuildingColliderBuilder.Build(new[] { shape });
                try
                {
                    var bounds = blob.Value.CalculateAabb();
                    Assert.That(math.distance(bounds.Center, new float3(1, 3, 2)), Is.LessThan(.001f));
                    Assert.That(bounds.Extents.y, Is.EqualTo(type == "Sphere" ? 2 : 4).Within(.001f));
                    if (type == "Box") Assert.That(bounds.Extents.x, Is.EqualTo(6).Within(.001f));
                    if (type == "Cylinder")
                    {
                        var ray = new RaycastInput { Start = new float3(1.8f, 8, 2), End = new float3(1.8f, 0, 2), Filter = CollisionFilter.Default };
                        Assert.That(blob.Value.CastRay(ray, out RaycastHit hit), Is.True);
                        Assert.That(hit.Position.y, Is.EqualTo(5).Within(.001f), "Cylinder has a flat cap, unlike a capsule.");
                    }
                    Assert.That(CollisionFilter.IsCollisionEnabled(blob.Value.GetCollisionFilter(), FoundationCollisionCategories.FoundationQueryFilter), Is.False);
                    Assert.That(CollisionFilter.IsCollisionEnabled(blob.Value.GetCollisionFilter(), new CollisionFilter { BelongsTo = FoundationCollisionCategories.Character, CollidesWith = FoundationCollisionCategories.Building }), Is.True);
                }
                finally { blob.Dispose(); }
            }
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void BuildCommands_ImportedCollisionFollowsRotationUpperFloorAndRemoval(int turns)
        {
            var asset = FactoryDatabaseEditor.CreateSnapshot(AssetDatabase.LoadAssetAtPath<FactoryDatabaseAsset>(FactoryDatabaseEditor.DatabaseAssetPath));
            BlobAssetReference<FactoryDatabaseBlob> database = default;
            BlobAssetReference<Collider> collider = default;
            try
            {
                var row = Array.Find(asset.buildings, b => b.kind == BuildingKind.Storage);
                Assert.That(FactoryDatabaseBakingUtility.TryBuild(asset, null, out database), Is.True);
                collider = FactoryBuildingColliderBuilder.Build(new[] { new FactoryColliderProxy { type = "Box", position = new Vector3(.6f, 1, .25f), size = new Vector3(.4f, 2, .6f) } });
                var grid = EntityManager.CreateEntity();
                var config = new WorldGridConfig { CellSize = 1, LayerHeight = 2 };
                EntityManager.AddComponentData(grid, config);
                EntityManager.AddBuffer<GridBuildCommand>(grid);
                EntityManager.AddBuffer<GridBuildResult>(grid);
                var surfaces = EntityManager.AddBuffer<InitialSurfaceCell>(grid);
                var anchor = new GridCell(5, 3, 5);
                for (int x = 0; x < 3; x++) for (int z = 0; z < 3; z++) surfaces.Add(new InitialSurfaceCell { Cell = anchor + new int2(x, z) });
                UpdateSystem(GetOrCreateManagedSystem<SurfaceRegistrySystem>());
                var visual = EntityManager.CreateEntity(typeof(Prefab), typeof(LocalTransform));
                EntityManager.SetComponentData(visual, LocalTransform.Identity);
                var catalog = EntityManager.CreateEntity();
                EntityManager.AddComponentData(catalog, new BuildingPrefabCatalog());
                EntityManager.AddComponentData(catalog, new FactoryDatabase { Value = database });
                EntityManager.AddBuffer<BuildingVisualPrefabEntry>(catalog).Add(new BuildingVisualPrefabEntry { BuildingDefinition = new BuildingDefinitionId { Value = row.id }, Prefab = visual, Collider = collider });
                var occupancy = GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
                UpdateSystem(occupancy);
                EntityManager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand { Type = GridBuildCommandType.Place, Kind = row.kind, BuildingDefinition = new BuildingDefinitionId { Value = row.id }, StartCell = anchor, EndCell = anchor, QuarterTurns = (byte)turns });
                UpdateSystem(GetOrCreateManagedSystem<GridBuildCommandSystem>());
                Assert.That(EntityManager.GetBuffer<GridBuildResult>(grid)[0].Success, Is.EqualTo(1));
                UpdateSystem(occupancy);
                Assert.That(occupancy.TryGetOccupant(anchor, out Entity building), Is.True);
                Assert.That(EntityManager.GetComponentData<PhysicsCollider>(building).Value, Is.EqualTo(collider));
                Assert.That(EntityManager.HasComponent<PhysicsWorldIndex>(building), Is.True);
                var transform = EntityManager.GetComponentData<LocalTransform>(building);
                Assert.That(transform.Position.y, Is.EqualTo(6));
                var expected = transform.Position + math.rotate(EcsGridUtility.RotationFromQuarterTurns(turns), new float3(.6f, 1, .25f));
                var worldBounds = collider.Value.CalculateAabb(new RigidTransform(transform.Rotation, transform.Position));
                Assert.That(math.distance(worldBounds.Center, expected), Is.LessThan(.001f));
                EntityManager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand { Type = GridBuildCommandType.Remove, StartCell = anchor, EndCell = anchor });
                UpdateSystem(GetOrCreateManagedSystem<GridBuildCommandSystem>());
                Assert.That(EntityManager.Exists(building), Is.False);
                using var physics = EntityManager.CreateEntityQuery(typeof(PhysicsCollider));
                Assert.That(physics.CalculateEntityCount(), Is.Zero);
            }
            finally
            {
                // Destroy instances before releasing the shared test blobs.
                EntityManager.DestroyEntity(EntityManager.UniversalQuery);
                if (database.IsCreated) database.Dispose();
                if (collider.IsCreated) collider.Dispose();
                Object.DestroyImmediate(asset);
            }
        }
    }
}
