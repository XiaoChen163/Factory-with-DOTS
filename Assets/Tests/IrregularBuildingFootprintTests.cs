using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Factory.Tests
{
    public sealed class IrregularBuildingFootprintTests : FactoryWorldFixture
    {
        private BlobAssetReference<FactoryDatabaseBlob> database;
        private FactoryDatabaseAsset asset;
        private Entity grid;
        private BuildingDefinitionId level;
        private readonly GridCell anchor = new GridCell(5, 0, 5);

        public override void TearDownWorld()
        {
            base.TearDownWorld();
            if (database.IsCreated) database.Dispose();
            if (asset != null) Object.DestroyImmediate(asset);
        }

        private static FactoryBuildingDefinitionRow Shape(string bitmap = "1111/1111/1100") =>
            new FactoryBuildingDefinitionRow
            {
                key = "irregular", footprintWidth = 4, footprintHeight = 3,
                isRectangle = false, footprintBitmap = bitmap
            };

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Placement_Preview_OccupancyAndRemoval_UseOnlyOccupiedCells(int turns)
        {
            Initialize(turns);
            Submit(GridBuildCommandType.Place, anchor, turns);
            Assert.That(Result.Success, Is.EqualTo(1));
            GridOccupancyIndexSystem occupancy = GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
            UpdateSystem(occupancy);
            HashSet<GridCell> expected = Expected(turns);
            Entity owner = Entity.Null;
            foreach (GridCell cell in expected)
            {
                Assert.That(occupancy.TryGetOccupant(cell, out owner), Is.True, cell.ToString());
                Assert.That(EntityManager.GetBuffer<OccupiedCellOffset>(owner).Length, Is.EqualTo(10));
            }
            GridCell hole = anchor + Rotate(new int2(3, 2), turns);
            Assert.That(occupancy.TryGetOccupant(hole, out _), Is.False);
            float3 position = EntityManager.GetComponentData<LocalTransform>(owner).Position;
            Assert.That(position, Is.EqualTo((turns & 1) == 0
                ? new float3(7f, 0f, 6.5f) : new float3(6.5f, 0f, 7f)));

            // Exercise the actual controller's preview rather than a separate shape implementation.
            World previous = World.DefaultGameObjectInjectionWorld;
            GameObject go = new GameObject("Irregular footprint preview");
            try
            {
                World.DefaultGameObjectInjectionWorld = TestWorld;
                EcsGridInteractionController controller = go.AddComponent<EcsGridInteractionController>();
                Assert.That(controller.TrySelectBuildingDefinition(level), Is.True);
                typeof(EcsGridInteractionController).GetField("quarterTurns", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(controller, (byte)turns);
                MethodInfo buildPreview = typeof(EcsGridInteractionController).GetMethod(
                    "BuildPreviewCells", BindingFlags.Instance | BindingFlags.NonPublic);
                // The second parameter is the interaction anchor; normal machine previews use hoveredCell.
                Type anchorType = buildPreview.GetParameters()[1].ParameterType;
                if (anchorType.IsByRef) anchorType = anchorType.GetElementType();
                buildPreview.Invoke(controller, new[] { (object)anchor, Activator.CreateInstance(anchorType) });
                var preview = (List<GridCell>)typeof(EcsGridInteractionController).GetField(
                    "previewCells", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
                CollectionAssert.AreEquivalent(expected, preview);
            }
            finally
            {
                Object.DestroyImmediate(go);
                World.DefaultGameObjectInjectionWorld = previous;
            }

            Submit(GridBuildCommandType.Remove, hole, turns);
            Assert.That(Result.Success, Is.Zero);
            Assert.That(EntityManager.Exists(owner), Is.True);
            Submit(GridBuildCommandType.Remove, anchor + Rotate(new int2(1, 2), turns), turns);
            Assert.That(Result.Success, Is.EqualTo(1));
            UpdateSystem(occupancy);
            foreach (GridCell cell in expected)
                Assert.That(occupancy.TryGetOccupant(cell, out _), Is.False);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Placement_OnlyRequiresSurfaceAndFreeSpaceForOnes(int turns)
        {
            Initialize(turns);
            GridCell hole = anchor + Rotate(new int2(3, 2), turns);
            Entity blocker = EntityManager.CreateEntity();
            EntityManager.AddComponentData(blocker, new GridPlacement
            {
                AnchorCell = hole, FootprintSize = new int2(1), Kind = BuildingKind.Storage
            });
            EntityManager.AddBuffer<OccupiedCellOffset>(blocker).Add(new OccupiedCellOffset { Value = int2.zero });
            EntityManager.AddBuffer<BuildingPort>(blocker);
            // Only occupied cells have surfaces; the blocker is deliberately inside the empty part.
            GetOrCreateManagedSystem<SurfaceRegistrySystem>().AddFoundation(
                grid, hole, 0, SurfacePermission.All, true);
            UpdateSystem(GetOrCreateManagedSystem<GridOccupancyIndexSystem>());
            Submit(GridBuildCommandType.Place, anchor, turns);
            Assert.That(Result.Success, Is.EqualTo(1));
            Submit(GridBuildCommandType.Place, anchor, turns);
            Assert.That(Result.Success, Is.Zero);
            Assert.That(Result.FailureReason, Is.EqualTo(GridBuildFailureReason.CellOccupied));
        }

        [TestCase("")]
        [TestCase("1111/1111")]
        [TestCase("1111/1111/11000")]
        [TestCase("1111/1111/11x0")]
        [TestCase("0000/0000/0000")]
        public void InvalidBitmap_IsRejected(string bitmap)
        {
            Assert.Throws<InvalidDataException>(() => FactoryBuildingFootprintUtility.GetOccupiedOffsets(Shape(bitmap)));
        }

        [Test]
        public void Rectangle_ExpandsWithoutBitmap_AndRejectsConflictingBitmap()
        {
            FactoryBuildingDefinitionRow row = Shape(null);
            row.isRectangle = true;
            Assert.That(FactoryBuildingFootprintUtility.GetOccupiedOffsets(row).Length, Is.EqualTo(12));
            row.footprintBitmap = "1111/1111/1111";
            Assert.Throws<InvalidDataException>(() => FactoryBuildingFootprintUtility.GetOccupiedOffsets(row));
        }

        [Test]
        public void EmptyAnchorAndBorders_ArePreserved()
        {
            CollectionAssert.AreEqual(new[] { new int2(1, 1) },
                FactoryBuildingFootprintUtility.GetOccupiedOffsets(Shape("0000/0100/0000")));
        }

        [TestCase(BuildingPortType.Input, 0, -1)]
        [TestCase(BuildingPortType.Output, 0, 1)]
        [TestCase(BuildingPortType.Input, -1, 0)]
        [TestCase(BuildingPortType.Output, 1, 0)]
        public void ConcavePort_AcceptsEitherBoundaryDirection(BuildingPortType type, int dx, int dz)
        {
            FactoryBuildingDefinitionRow row = Shape();
            row.ports = new[] { new FactoryBuildingPortTableRow
            {
                type = type, cellOffset = new Vector2Int(2, 2), direction = new Vector2Int(dx, dz)
            }};
            Assert.DoesNotThrow(() => FactoryBuildingFootprintUtility.ValidatePorts(row,
                FactoryBuildingFootprintUtility.GetOccupiedOffsets(row)));
        }

        [TestCase(1, 1, 1, 0)] // Inside occupied cells.
        [TestCase(3, 2, 1, 0)] // No adjacent building cell.
        [TestCase(2, 2, 1, 1)] // Diagonal.
        [TestCase(2, 2, 0, 0)]
        public void InvalidPort_IsRejected(int x, int z, int dx, int dz)
        {
            FactoryBuildingDefinitionRow row = Shape();
            row.ports = new[] { new FactoryBuildingPortTableRow
            {
                type = BuildingPortType.Input, cellOffset = new Vector2Int(x, z), direction = new Vector2Int(dx, dz)
            }};
            Assert.Throws<InvalidDataException>(() => FactoryBuildingFootprintUtility.ValidatePorts(row,
                FactoryBuildingFootprintUtility.GetOccupiedOffsets(row)));
        }

        private GridBuildResult Result => EntityManager.GetBuffer<GridBuildResult>(grid)[0];

        private void Submit(GridBuildCommandType type, GridCell cell, int turns)
        {
            UpdateSystem(GetOrCreateManagedSystem<GridOccupancyIndexSystem>());
            EntityManager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand
            {
                Type = type, Kind = BuildingKind.Storage, BuildingDefinition = level,
                StartCell = cell, EndCell = cell, QuarterTurns = (byte)turns
            });
            UpdateSystem(GetOrCreateManagedSystem<GridBuildCommandSystem>());
        }

        private void Initialize(int turns)
        {
            asset = Object.Instantiate(AssetDatabase.LoadAssetAtPath<FactoryDatabaseAsset>(
                FactoryDatabaseCsvImporter.DatabaseAssetPath));
            int index = Array.FindIndex(asset.buildings, b => b.kind == BuildingKind.Storage);
            FactoryBuildingDefinitionRow row = asset.buildings[index];
            row.footprintWidth = 4;
            row.footprintHeight = 3;
            row.isRectangle = false;
            row.footprintBitmap = "1111/1111/1100";
            row.ports = new[] { new FactoryBuildingPortTableRow
            {
                type = BuildingPortType.Input, cellOffset = new Vector2Int(2, 2), direction = Vector2Int.down
            }};
            asset.buildings[index] = row;
            level = new BuildingDefinitionId { Value = row.id };
            Assert.That(FactoryDatabaseBakingUtility.TryBuild(asset, null, out database), Is.True);
            grid = EntityManager.CreateEntity();
            EntityManager.AddComponentData(grid, new WorldGridConfig { CellSize = 1f, LayerHeight = 1f });
            EntityManager.AddBuffer<GridBuildCommand>(grid);
            EntityManager.AddBuffer<GridBuildResult>(grid);
            DynamicBuffer<InitialSurfaceCell> surfaces = EntityManager.AddBuffer<InitialSurfaceCell>(grid);
            foreach (GridCell cell in Expected(turns)) surfaces.Add(new InitialSurfaceCell { Cell = cell });
            UpdateSystem(GetOrCreateManagedSystem<SurfaceRegistrySystem>());
            Entity visual = EntityManager.CreateEntity(typeof(Prefab), typeof(LocalTransform));
            EntityManager.SetComponentData(visual, LocalTransform.Identity);
            Entity catalog = EntityManager.CreateEntity();
            EntityManager.AddComponentData(catalog, new BuildingPrefabCatalog());
            EntityManager.AddComponentData(catalog, new FactoryDatabase { Value = database });
            EntityManager.AddBuffer<BuildingVisualPrefabEntry>(catalog).Add(
                new BuildingVisualPrefabEntry { BuildingDefinition = level, Prefab = visual });
            UpdateSystem(GetOrCreateManagedSystem<GridOccupancyIndexSystem>());
        }

        private HashSet<GridCell> Expected(int turns)
        {
            HashSet<GridCell> result = new HashSet<GridCell>();
            for (int z = 0; z < 3; z++)
            for (int x = 0; x < (z == 2 ? 2 : 4); x++) result.Add(anchor + Rotate(new int2(x, z), turns));
            return result;
        }

        // Independent expected coordinate transform for the 4x3 test shape.
        private static int2 Rotate(int2 p, int turns) => turns switch
        {
            1 => new int2(2 - p.y, p.x),
            2 => new int2(3 - p.x, 2 - p.y),
            3 => new int2(p.y, 3 - p.x),
            _ => p
        };
    }
}
