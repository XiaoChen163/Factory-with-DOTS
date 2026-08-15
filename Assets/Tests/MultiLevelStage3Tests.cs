using System.Collections.Generic;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace Factory.Tests
{
    public sealed class MultiLevelStage3Tests : FactoryWorldFixture
    {
        [TestCase(-1, -1, -1, -1, 255)]
        [TestCase(-16, -16, -1, -1, 0)]
        [TestCase(-17, 0, -2, 0, 15)]
        [TestCase(16, 16, 1, 1, 0)]
        public void ChunkAddressing_UsesFloorDivisionForNegativeCoordinates(
            int x, int z, int chunkX, int chunkZ, int localIndex)
        {
            GridCell cell = new GridCell(x, 0, z);
            Assert.That(SurfaceChunkUtility.GetChunkKey(cell),
                Is.EqualTo(new SurfaceChunkKey(chunkX, 0, chunkZ)));
            Assert.That(SurfaceChunkUtility.GetLocalCellIndex(cell), Is.EqualTo(localIndex));
            Assert.That(
                SurfaceChunkUtility.GetCell(
                    SurfaceChunkUtility.GetChunkKey(cell),
                    SurfaceChunkUtility.GetLocalCellIndex(cell)),
                Is.EqualTo(cell));
        }

        [Test]
        public void SurfaceBitmap_StoresAll256Cells()
        {
            SurfaceChunkOccupancy bitmap = default;
            bitmap.Set(0, true);
            bitmap.Set(63, true);
            bitmap.Set(64, true);
            bitmap.Set(255, true);
            Assert.That(bitmap.IsSet(0));
            Assert.That(bitmap.IsSet(63));
            Assert.That(bitmap.IsSet(64));
            Assert.That(bitmap.IsSet(255));
            bitmap.Set(64, false);
            Assert.That(bitmap.IsSet(64), Is.False);
        }

        [Test]
        public void LegacyRectangle_InitializesSparseRegistryAndStableOwners()
        {
            Entity grid = CreateGrid(new int2(17, 2));
            SurfaceRegistrySystem registry = GetOrCreateManagedSystem<SurfaceRegistrySystem>();
            UpdateSystem(registry);

            Assert.That(registry.SurfaceCount, Is.EqualTo(34));
            Assert.That(registry.ChunkCount, Is.EqualTo(2));
            Assert.That(registry.TryGetFoundation(new GridCell(16, 0, 1), out Entity owner));
            Assert.That(EntityManager.HasComponent<Foundation>(owner));
            Assert.That(EntityManager.HasComponent<LegacySurfaceInitialized>(grid));
        }

        [Test]
        public void TwoAdjacentVoxels_CullBothContactFacesAndKeepMaterials()
        {
            List<FoundationVoxel> voxels = new()
            {
                new FoundationVoxel(new GridCell(15, 0, 0), 2),
                new FoundationVoxel(new GridCell(16, 0, 0), 7)
            };
            List<FoundationQuad> faces = FoundationMeshGenerator.ExtractVisibleFaces(voxels);
            SortedDictionary<ushort, List<FoundationQuad>> groups =
                FoundationMeshGenerator.GroupByMaterial(faces);
            Assert.That(faces.Count, Is.EqualTo(10));
            Assert.That(groups.Keys, Is.EquivalentTo(new ushort[] { 2, 7 }));
            Assert.That(groups[2].Count, Is.EqualTo(5));
            Assert.That(groups[7].Count, Is.EqualTo(5));
            List<FoundationQuad> merged =
                FoundationMeshGenerator.MergeCoplanarFaces(faces);
            Assert.That(merged.Count, Is.EqualTo(10),
                "Different materials must prevent coplanar merging across the seam.");

            voxels[1] = new FoundationVoxel(new GridCell(16, 0, 0), 2);
            merged = FoundationMeshGenerator.MergeCoplanarFaces(
                FoundationMeshGenerator.ExtractVisibleFaces(voxels));
            Assert.That(merged.Count, Is.EqualTo(6));
        }

        [Test]
        public void GreedyBoxes_ExpandXThenZThenLevelDeterministically()
        {
            List<int3> voxels = new();
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            for (int x = 0; x < 3; x++) voxels.Add(new int3(x, y, z));
            voxels.Add(new int3(8, 0, 8));

            List<FoundationBox> boxes = FoundationGreedyBoxBuilder.Build(voxels);
            Assert.That(boxes.Count, Is.EqualTo(2));
            Assert.That(boxes[0].Minimum, Is.EqualTo(int3.zero));
            Assert.That(boxes[0].Size, Is.EqualTo(new int3(3, 2, 2)));
            Assert.That(boxes[1].Minimum, Is.EqualTo(new int3(8, 0, 8)));
        }

        [Test]
        public void CompoundCollider_UsesOneOwnedBlobForGreedyBoxes()
        {
            List<FoundationBox> boxes = new()
            {
                new FoundationBox(int3.zero, new int3(3, 1, 2))
            };
            BlobAssetReference<Collider> collider = FoundationCompoundColliderBuilder.Build(
                boxes,
                FoundationCollisionCategories.FoundationFilter,
                Unity.Physics.Material.Default);
            try
            {
                Assert.That(collider.IsCreated);
                Assert.That(collider.Value.Type, Is.EqualTo(ColliderType.Compound));
            }
            finally
            {
                if (collider.IsCreated) collider.Dispose();
            }
        }

        [Test]
        public void FoundationCommands_ExtendAndRemoveThroughUnifiedBuffer()
        {
            Entity grid = CreateGrid(new int2(1, 1));
            SurfaceRegistrySystem registry = GetOrCreateManagedSystem<SurfaceRegistrySystem>();
            UpdateSystem(registry);
            GridOccupancyIndexSystem occupancy = GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
            UpdateSystem(occupancy);
            GridBuildCommandSystem build = GetOrCreateManagedSystem<GridBuildCommandSystem>();

            EntityManager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand
            {
                RequestId = 1,
                Type = GridBuildCommandType.PlaceFoundation,
                StartCell = new GridCell(1, 0, 0),
                VisualMaterialId = 9
            });
            UpdateSystem(build);
            GridBuildResult placed = EntityManager.GetBuffer<GridBuildResult>(grid)[0];
            Assert.That(placed.Success, Is.EqualTo(1));
            Assert.That(registry.TryGetSurface(new GridCell(1, 0, 0), out var surface));
            Assert.That(surface.VisualMaterialId, Is.EqualTo(9));

            EntityManager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand
            {
                RequestId = 2,
                Type = GridBuildCommandType.RemoveFoundation,
                StartCell = new GridCell(1, 0, 0)
            });
            UpdateSystem(build);
            Assert.That(EntityManager.GetBuffer<GridBuildResult>(grid)[0].Success, Is.EqualTo(1));
            Assert.That(registry.HasSurface(new GridCell(1, 0, 0)), Is.False);
        }

        [Test]
        public void FoundationQuery_ResolvesTopAndSideHitsToSameOwner()
        {
            Entity grid = CreateGrid(new int2(1, 1));
            SurfaceRegistrySystem registry = GetOrCreateManagedSystem<SurfaceRegistrySystem>();
            UpdateSystem(registry);
            WorldGridConfig config = EntityManager.GetComponentData<WorldGridConfig>(grid);

            Assert.That(FoundationQueryUtility.TryResolveFoundation(
                new float3(0.5f, 0f, 0.5f), new float3(0, 1, 0), config,
                registry, out GridCell topCell, out Entity topOwner));
            Assert.That(FoundationQueryUtility.TryResolveFoundation(
                new float3(1f, -0.5f, 0.5f), new float3(1, 0, 0), config,
                registry, out GridCell sideCell, out Entity sideOwner));
            Assert.That(topCell, Is.EqualTo(new GridCell(0, 0, 0)));
            Assert.That(sideCell, Is.EqualTo(topCell));
            Assert.That(sideOwner, Is.EqualTo(topOwner));
        }

        [Test]
        public void PhysicsDirtyChunk_BuildsOneStaticCompoundBody()
        {
            CreateGrid(new int2(2, 1));
            SurfaceRegistrySystem registry = GetOrCreateManagedSystem<SurfaceRegistrySystem>();
            UpdateSystem(registry);
            FoundationPhysicsChunkSystem physics =
                GetOrCreateManagedSystem<FoundationPhysicsChunkSystem>();
            UpdateSystem(physics);

            EntityQuery query = EntityManager.CreateEntityQuery(
                typeof(FoundationPhysicsChunk), typeof(PhysicsCollider),
                typeof(LocalTransform), typeof(LocalToWorld), typeof(PhysicsWorldIndex));
            Assert.That(query.CalculateEntityCount(), Is.EqualTo(1));
            Entity entity = query.GetSingletonEntity();
            Assert.That(EntityManager.GetComponentData<PhysicsCollider>(entity).Value.IsCreated);
            Assert.That(EntityManager.HasComponent<PhysicsVelocity>(entity), Is.False);
            Assert.That(EntityManager.HasComponent<PhysicsMass>(entity), Is.False);
        }

        [Test]
        public void RemovingFoundation_WithBuildingOnSurfaceIsRejected()
        {
            Entity grid = CreateGrid(new int2(1, 1));
            SurfaceRegistrySystem registry = GetOrCreateManagedSystem<SurfaceRegistrySystem>();
            UpdateSystem(registry);
            Entity building = EntityManager.CreateEntity();
            EntityManager.AddComponentData(building, new GridPlacement
            {
                AnchorCell = new GridCell(0, 0, 0),
                FootprintSize = new int2(1, 1),
                Kind = BuildingKind.Storage
            });
            EntityManager.AddBuffer<OccupiedCellOffset>(building).Add(
                new OccupiedCellOffset { Value = int2.zero });
            GridOccupancyIndexSystem occupancy = GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
            UpdateSystem(occupancy);
            GridBuildCommandSystem build = GetOrCreateManagedSystem<GridBuildCommandSystem>();
            EntityManager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand
            {
                RequestId = 1,
                Type = GridBuildCommandType.RemoveFoundation,
                StartCell = new GridCell(0, 0, 0)
            });
            UpdateSystem(build);

            GridBuildResult result = EntityManager.GetBuffer<GridBuildResult>(grid)[0];
            Assert.That(result.Success, Is.Zero);
            Assert.That(result.FailureReason,
                Is.EqualTo(GridBuildFailureReason.FoundationSupportsBuilding));
            Assert.That(registry.HasSurface(new GridCell(0, 0, 0)));
        }

        [Test]
        public void RepeatedFoundationChurn_KeepsColliderBlobCountBounded()
        {
            Entity grid = CreateGrid(new int2(1, 1));
            SurfaceRegistrySystem registry = GetOrCreateManagedSystem<SurfaceRegistrySystem>();
            UpdateSystem(registry);
            GridOccupancyIndexSystem occupancy = GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
            UpdateSystem(occupancy);
            GridBuildCommandSystem build = GetOrCreateManagedSystem<GridBuildCommandSystem>();
            FoundationPhysicsChunkSystem physics =
                GetOrCreateManagedSystem<FoundationPhysicsChunkSystem>();
            UpdateSystem(physics);

            for (uint cycle = 0; cycle < 16; cycle++)
            {
                EntityManager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand
                {
                    RequestId = cycle * 2 + 1,
                    Type = GridBuildCommandType.PlaceFoundation,
                    StartCell = new GridCell(1, 0, 0)
                });
                UpdateSystem(build);
                UpdateSystem(physics);
                EntityManager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand
                {
                    RequestId = cycle * 2 + 2,
                    Type = GridBuildCommandType.RemoveFoundation,
                    StartCell = new GridCell(1, 0, 0)
                });
                UpdateSystem(build);
                UpdateSystem(physics);
            }

            Assert.That(physics.ActiveColliderBlobCount, Is.EqualTo(1));
            Assert.That(
                physics.CreatedColliderBlobCount - physics.DisposedColliderBlobCount,
                Is.EqualTo(1));
            Assert.That(EntityManager.CreateEntityQuery(typeof(FoundationPhysicsChunk))
                .CalculateEntityCount(), Is.EqualTo(1));
        }

        private Entity CreateGrid(int2 size)
        {
            Entity grid = EntityManager.CreateEntity();
            EntityManager.AddComponentData(grid, new GridDefinition
            {
                Size = size,
                Origin = float3.zero,
                CellSize = 1f,
                Revision = 1
            });
            EntityManager.AddComponentData(grid, new WorldGridConfig
            {
                CellSize = 1f,
                LayerHeight = 1f,
                Origin = float3.zero
            });
            EntityManager.AddComponentData(grid, new SurfaceTopologyRevision { Value = 1 });
            EntityManager.AddComponentData(grid, new BuildingOccupancyRevision { Value = 1 });
            EntityManager.AddBuffer<GridBuildCommand>(grid);
            EntityManager.AddBuffer<GridBuildResult>(grid);
            return grid;
        }
    }
}
