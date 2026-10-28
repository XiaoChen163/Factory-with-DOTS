using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

namespace Factory.Tests
{
    public sealed class MultiLevelStage7Tests : FactoryWorldFixture
    {
        private BlobAssetReference<FactoryDatabaseBlob> database;
        private Entity grid;
        private GridBuildCommandSystem build;
        private GridOccupancyIndexSystem occupancy;
        private static readonly int2 East = new int2(1,0);

        public override void SetUpWorld()
        {
            base.SetUpWorld();
            grid = EntityManager.CreateEntity();

            EntityManager.AddComponentData(grid, new WorldGridConfig { CellSize = 1f, LayerHeight = 1f });

            EntityManager.AddComponentData(grid, new SurfaceTopologyRevision { Value = 1 });
            EntityManager.AddComponentData(grid, new BuildingOccupancyRevision { Value = 1 });
            EntityManager.AddComponentData(grid, new TransportTopologyRevision { Value = 1 });
            EntityManager.AddBuffer<GridBuildCommand>(grid);
            EntityManager.AddBuffer<GridBuildResult>(grid);
            FactoryDatabaseAsset asset = AssetDatabase.LoadAssetAtPath<FactoryDatabaseAsset>(
                "Assets/Data/Generated/FactoryDatabase.asset");
            Assert.That(FactoryDatabaseBakingUtility.TryBuild(asset, asset, out database), Is.True);
            Entity prefab = EntityManager.CreateEntity(typeof(Prefab), typeof(LocalTransform), typeof(LocalToWorld));
            EntityManager.SetComponentData(prefab, LocalTransform.Identity);
            Entity catalog = EntityManager.CreateEntity();
            EntityManager.AddComponentData(catalog, new BuildingPrefabCatalog());
            EntityManager.AddComponentData(catalog, new FactoryDatabase { Value = database });
            DynamicBuffer<BuildingVisualPrefabEntry> visuals = EntityManager.AddBuffer<BuildingVisualPrefabEntry>(catalog);
            foreach (var level in asset.buildingLevels)
                visuals.Add(new BuildingVisualPrefabEntry { BuildingLevel = new BuildingLevelId { Value = level.id }, Prefab = prefab });
            UpdateSystem(GetOrCreateManagedSystem<SurfaceRegistrySystem>());
            occupancy = GetOrCreateManagedSystem<GridOccupancyIndexSystem>();
            UpdateSystem(occupancy);
            build = GetOrCreateManagedSystem<GridBuildCommandSystem>();
        }

        public override void TearDownWorld()
        {
            base.TearDownWorld();
            if (database.IsCreated) database.Dispose();
        }

        [TestCase(1)]
        [TestCase(-1)]
        public void MixedBatch_BuildsIndependentNodesThroughFoundation(int step)
        {
            int start = step > 0 ? 0 : 5;
            EntityManager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand
            {
                Type = GridBuildCommandType.PlaceFoundation, StartCell = Cell(start + step)
            });
            Enqueue(14, start);
            Enqueue(22, start + step, step < 0 ? (byte)2 : (byte)0);
            Enqueue(22, start + step*2, step < 0 ? (byte)2 : (byte)0);
            Enqueue(18, start + step*3);
            UpdateSystem(build);
            var results = EntityManager.GetBuffer<GridBuildResult>(grid);
            Assert.That(results.Length, Is.EqualTo(5));
            foreach (var result in results) Assert.That(result.Success, Is.EqualTo(1), result.FailureReason.ToString());
            UpdateSystem(occupancy);
            Assert.That(occupancy.OccupiedCellCount, Is.Zero);
            Assert.That(GetOrCreateManagedSystem<VerticalConnectorIndexSystem>().TryGet(Cell(start+step), out _), Is.True);
            UpdateSystem(GetOrCreateManagedSystem<VerticalBeltTopologySystem>());
            Assert.That(EntityManager.GetBuffer<TransportExplicitEdge>(grid).Length, Is.EqualTo(3));
        }

        [Test]
        public void Placement_RejectsMissingSupportWrongDirectionAndSecondEntryOutput()
        {
            Assert.That(Place(22, 1).FailureReason, Is.EqualTo(GridBuildFailureReason.VerticalSupportMissing));
            Assert.That(Place(18, 1).FailureReason, Is.EqualTo(GridBuildFailureReason.VerticalSupportMissing));
            Assert.That(Place(14, 3).Success, Is.EqualTo(1));
            Assert.That(Place(22, 4).Success, Is.EqualTo(1));
            Assert.That(Place(22, 2, 2).FailureReason, Is.EqualTo(GridBuildFailureReason.VerticalDirectionConflict));
            Assert.That(Place(22, 5, 2).Success, Is.Zero);
            Assert.That(Place(18, 5).Success, Is.EqualTo(1));
        }

        [Test]
        public void IndependentOccupancy_RejectsDuplicateAndNormalBuildingInShaft()
        {
            Assert.That(Place(14, 0).Success, Is.EqualTo(1));
            Assert.That(Place(14, 0).FailureReason, Is.EqualTo(GridBuildFailureReason.CellOccupied));
            EntityManager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand
            { Type = GridBuildCommandType.PlaceFoundation, StartCell = Cell(0) });
            UpdateSystem(build);
            Assert.That(Place(1, 0).FailureReason, Is.EqualTo(GridBuildFailureReason.CellOccupied));
        }

        [Test]
        public void Removal_RecoversItemsFromWholeChainAndClearsTopology()
        {
            Place(14,0); Place(22,1); Place(18,2);
            var player=EntityManager.CreateEntity();
            EntityManager.AddComponentData(player,new PlayerIdentity { Value=new PlayerId { Value=7 } });
            EntityManager.AddComponentData(player,new PlayerInventory { SlotCount=2 });
            var inventory=EntityManager.AddBuffer<InventorySlot>(player);
            inventory.Add(default); inventory.Add(default);
            var index=GetOrCreateManagedSystem<VerticalConnectorIndexSystem>();
            Entity[] items=new Entity[3];
            for(int i=0;i<3;i++)
            {
                Assert.That(index.TryGet(Cell(i),out var node),Is.True);
                items[i]=CreateItem();
                EntityManager.SetComponentData(node,new BeltState { CurrentItem=items[i] });
            }
            EntityManager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand
            { Type=GridBuildCommandType.Remove,StartCell=Cell(1),Player=new PlayerId { Value=7 } });
            UpdateSystem(build);
            Assert.That(EntityManager.GetBuffer<GridBuildResult>(grid)[0].AffectedCount,Is.EqualTo(3));
            foreach(var item in items) Assert.That(EntityManager.Exists(item),Is.False);
            Assert.That(EntityManager.GetBuffer<InventorySlot>(player)[0].Count,Is.EqualTo(3));
            UpdateSystem(GetOrCreateManagedSystem<VerticalBeltTopologySystem>());
            Assert.That(EntityManager.GetBuffer<TransportExplicitEdge>(grid).Length,Is.Zero);
            Assert.That(Place(14,0).Success,Is.EqualTo(1));
        }

        [TestCase(0,1)]
        [TestCase(1,1)]
        [TestCase(2,1)]
        [TestCase(0,-1)]
        [TestCase(1,-1)]
        [TestCase(2,-1)]
        public void Removal_FromAnyNodeRemovesOnlyItsConnectedVerticalChain(int selected,int step)
        {
            int start=step>0 ? 0 : 4;
            Place(14,start); Place(22,start+step,step>0 ? (byte)0 : (byte)2); Place(18,start+step*2);
            int other=step>0 ? 3 : 5;
            Place(14,other); Place(22,other+1); Place(18,other+2);
            Entity horizontal=CreateBelt(new GridCell(-1,start,0),East);
            EntityManager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand
            { Type=GridBuildCommandType.PlaceFoundation,StartCell=Cell(start+step) });
            UpdateSystem(build);
            var result=Remove(start+selected*step);
            Assert.That(result.Success,Is.EqualTo(1));
            Assert.That(result.AffectedCount,Is.EqualTo(3));
            var index=GetOrCreateManagedSystem<VerticalConnectorIndexSystem>();
            for(int i=0;i<3;i++)
            {
                Assert.That(index.TryGet(Cell(start+i*step),out _),Is.False);
                Assert.That(index.TryGet(Cell(other+i),out _),Is.True);
            }
            Assert.That(EntityManager.Exists(horizontal),Is.True);
            Assert.That(GetOrCreateManagedSystem<SurfaceRegistrySystem>().HasFoundationVoxel(Cell(start+step)),Is.True);
        }

        [Test]
        public void Removal_LineModifierOnVerticalNodeStillRemovesWholeVerticalChain()
        {
            Place(14,0); Place(22,1); Place(18,2);
            EntityManager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand
            { Type=GridBuildCommandType.RemoveBeltLine,StartCell=Cell(1) });
            UpdateSystem(build);
            var result=EntityManager.GetBuffer<GridBuildResult>(grid)[0];
            Assert.That(result.Success,Is.EqualTo(1));
            Assert.That(result.AffectedCount,Is.EqualTo(3));
            using var nodes=EntityManager.CreateEntityQuery(ComponentType.ReadOnly<VerticalBeltNode>());
            Assert.That(nodes.CalculateEntityCount(),Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Cancellation_BeforePlaybackRemovesEntryAndAnyQueuedBody(bool complete)
        {
            var commands=EntityManager.GetBuffer<GridBuildCommand>(grid);
            commands.Add(new GridBuildCommand { Type=GridBuildCommandType.Place,
                BuildingLevel=new BuildingLevelId { Value=14 },StartCell=Cell(0),PlacementRequestId=100,
                Player=new PlayerId { Value=1 } });
            if(complete) commands.Add(new GridBuildCommand { Type=GridBuildCommandType.PlaceVerticalBeltPath,
                BuildingLevel=new BuildingLevelId { Value=14 },StartCell=Cell(0),EndCell=Cell(3) });
            commands.Add(new GridBuildCommand { Type=GridBuildCommandType.CancelVerticalBeltPlacement,
                StartCell=Cell(0),PlacementRequestId=100,Player=new PlayerId { Value=1 } });
            UpdateSystem(build);
            var results=EntityManager.GetBuffer<GridBuildResult>(grid);
            foreach(var result in results) Assert.That(result.Success,Is.EqualTo(1),result.FailureReason.ToString());
            Assert.That(results[results.Length-1].AffectedCount,Is.EqualTo(complete ? 4 : 1));
            using var nodes=EntityManager.CreateEntityQuery(ComponentType.ReadOnly<VerticalBeltNode>());
            Assert.That(nodes.CalculateEntityCount(),Is.Zero);
            using var visuals=EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Parent>());
            Assert.That(visuals.CalculateEntityCount(),Is.Zero,"Cancelled roots must not leave visual entities behind.");
        }

        [TestCase(101,1)]
        [TestCase(100,2)]
        [TestCase(0,1)]
        public void Cancellation_MatchesOriginalPlacementAndIsIdempotent(int token,int player)
        {
            var commands=EntityManager.GetBuffer<GridBuildCommand>(grid);
            commands.Add(new GridBuildCommand { Type=GridBuildCommandType.Place,
                BuildingLevel=new BuildingLevelId { Value=14 },StartCell=Cell(0),PlacementRequestId=100,
                Player=new PlayerId { Value=1 } });
            commands.Add(new GridBuildCommand { Type=GridBuildCommandType.CancelVerticalBeltPlacement,
                StartCell=Cell(0),PlacementRequestId=(ulong)token,Player=new PlayerId { Value=(ulong)player } });
            UpdateSystem(build);
            var index=GetOrCreateManagedSystem<VerticalConnectorIndexSystem>();
            Assert.That(index.TryGet(Cell(0),out _),Is.True,"A failed or stale placement must not remove another entry.");
            for(int i=0;i<2;i++)
            {
                EntityManager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand
                { Type=GridBuildCommandType.CancelVerticalBeltPlacement,StartCell=Cell(0),
                    PlacementRequestId=100,Player=new PlayerId { Value=1 } });
                UpdateSystem(build);
                Assert.That(EntityManager.GetBuffer<GridBuildResult>(grid)[0].Success,Is.EqualTo(1));
                Assert.That(index.TryGet(Cell(0),out _),Is.False);
            }
        }

        [TestCase(1)]
        [TestCase(-1)]
        public void Transport_PlaneVerticalPlane_MovesEveryNodeAndPreservesBackpressure(int step)
        {
            int start = step > 0 ? 0 : 4;
            Entity source = CreateBelt(new GridCell(-1,start,0), East);
            Entity entry = Node(start, BuildingKind.VerticalBeltEntry, step);
            Entity middle = Node(start+step, BuildingKind.VerticalBeltSegment, step);
            Entity exit = Node(start+step*2, BuildingKind.VerticalBeltExit, step);
            Entity target = CreateBelt(new GridCell(1,start+step*2,0), East);
            Entity side = CreateBelt(new GridCell(0,start,-1), new int2(0,1), CreateItem(),1f);
            Entity item = CreateItem();
            EntityManager.SetComponentData(source, new BeltState { CurrentItem = item, Progress = 1f });
            UpdateSystem(GetOrCreateManagedSystem<VerticalBeltTopologySystem>());
            foreach (Entity expected in new[] { entry,middle,exit,target })
            {
                UpdateTransferTick();
                Assert.That(EntityManager.GetComponentData<BeltState>(expected).CurrentItem, Is.EqualTo(item));
            }
            Assert.That(EntityManager.GetComponentData<BeltState>(side).CurrentItem, Is.Not.EqualTo(Entity.Null));
            Entity blocked = CreateItem();
            EntityManager.SetComponentData(exit, new BeltState { CurrentItem = blocked, Progress = 1f });
            UpdateTransferTick();
            Assert.That(EntityManager.GetComponentData<BeltState>(exit).CurrentItem, Is.EqualTo(blocked));
            Assert.That(EntityManager.GetComponentData<BeltState>(target).CurrentItem, Is.EqualTo(item));
        }

        [TestCase(1)]
        [TestCase(-1)]
        public void OutputFacePicking_ResolvesNextCell(int step)
        {
            var hit = new GridSurfaceHit { Kind = GridSurfaceHitKind.VerticalNode,
                SourceCell = Cell(3), VerticalNode = new VerticalBeltNode
                { Kind = BuildingKind.VerticalBeltSegment, Step = step } };
            var anchor = GridBuildPlacementResolver.Resolve(hit, BuildingKind.VerticalBeltExit,0,East,
                new WorldGridConfig { CellSize=1,LayerHeight=1 });
            Assert.That(anchor.Cell, Is.EqualTo(Cell(3+step)));
        }

        [Test]
        public void Content_HasTwelvePureVisualPrefabsAndMatchingSpeeds()
        {
            var asset = AssetDatabase.LoadAssetAtPath<FactoryDatabaseAsset>("Assets/Data/Generated/FactoryDatabase.asset");
            var kinds = new[] { BuildingKind.VerticalBeltEntry,BuildingKind.VerticalBeltExit,BuildingKind.VerticalBeltSegment };
            foreach (var kind in kinds)
            {
                var building = asset.buildings.Single(x => x.kind == kind);
                var levels = asset.buildingLevels.Where(x => x.buildingId == building.id).ToArray();
                Assert.That(levels.Length, Is.EqualTo(4));
                foreach (var level in levels)
                {
                    Assert.That(level.visualPrefab, Is.Not.Null);
                    Assert.That(level.icon, Is.Not.Null);
                    Assert.That(level.visualPrefab.GetComponentsInChildren<Collider>(), Is.Empty);
                    Assert.That(level.visualPrefab.GetComponentsInChildren<MonoBehaviour>(), Is.Empty);
                    Assert.That(asset.beltLevels.Single(x => x.buildingLevelId == level.id).cellsPerSecond,
                        Is.EqualTo(math.pow(2,level.level-1)));
                }
            }
        }

        [Test]
        public void FullRing_WithUpAndDownShafts_MovesAtomically()
        {
            Entity a = Node(0,BuildingKind.VerticalBeltEntry,1);
            Entity b = Node(1,BuildingKind.VerticalBeltSegment,1);
            Entity c = Node(2,BuildingKind.VerticalBeltExit,1);
            Entity d = CreateBelt(new GridCell(1,2,0),East);
            Entity e = NodeAt(new GridCell(2,2,0),BuildingKind.VerticalBeltEntry,-1,East);
            Entity f = NodeAt(new GridCell(2,1,0),BuildingKind.VerticalBeltSegment,-1,East);
            Entity g = NodeAt(new GridCell(2,0,0),BuildingKind.VerticalBeltExit,-1,-East);
            Entity h = CreateBelt(new GridCell(1,0,0),-East);
            var topology = EntityManager.GetComponentData<BeltTopology>(a);
            topology.Direction = -East;
            EntityManager.SetComponentData(a,topology);
            Entity[] ring = { a,b,c,d,e,f,g,h };
            Entity[] items = new Entity[ring.Length];
            for (int i=0;i<ring.Length;i++)
            {
                items[i]=CreateItem();
                EntityManager.SetComponentData(ring[i],new BeltState { CurrentItem=items[i],Progress=1f });
            }
            UpdateSystem(GetOrCreateManagedSystem<VerticalBeltTopologySystem>());
            UpdateTransferTick();
            for (int i=0;i<ring.Length;i++)
                Assert.That(EntityManager.GetComponentData<BeltState>(ring[(i+1)%ring.Length]).CurrentItem,Is.EqualTo(items[i]));
            Assert.That(GetOrCreateManagedSystem<BeltTransferSystem>().TransportTopologyEdgeCount,Is.EqualTo(8));
        }

        [Test]
        public void RemovingShaftBelowExit_DoesNotAllowConvergingSecondInput()
        {
            var cells=new Dictionary<GridCell,GridPlacement>();
            cells[Cell(0)]=new GridPlacement { Kind=BuildingKind.VerticalBeltEntry };
            cells[Cell(2)]=new GridPlacement { Kind=BuildingKind.VerticalBeltExit };
            cells[Cell(3)]=new GridPlacement { Kind=BuildingKind.VerticalBeltSegment,QuarterTurns=2 };
            Assert.That(VerticalBeltUtility.Validate(BuildingKind.VerticalBeltSegment,Cell(1),0,
                cell=>cells.TryGetValue(cell,out var placement) ? placement : (GridPlacement?)null,out var failure),Is.False);
            Assert.That(failure,Is.EqualTo(GridBuildFailureReason.VerticalDirectionConflict));
        }

        [TestCase(14, 0, 3)]
        [TestCase(15, 5, 1)]
        [TestCase(16, 0, 4)]
        [TestCase(17, 6, 2)]
        public void UnifiedPath_CreatesMatchingTierAndKeepsEntryRotation(int tier, int start, int end)
        {
            Assert.That(Place((ushort)tier,start,1).Success,Is.EqualTo(1));
            EntityManager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand
            {
                Type=GridBuildCommandType.PlaceVerticalBeltPath,BuildingLevel=new BuildingLevelId { Value=(ushort)tier },
                StartCell=Cell(start),EndCell=Cell(end),QuarterTurns=3
            });
            UpdateSystem(build);
            var result=EntityManager.GetBuffer<GridBuildResult>(grid)[0];
            Assert.That(result.Success,Is.EqualTo(1),result.FailureReason.ToString());
            Assert.That(result.AffectedCount,Is.EqualTo(math.abs(end-start)));
            var index=GetOrCreateManagedSystem<VerticalConnectorIndexSystem>();
            Assert.That(index.TryGet(Cell(start),out var entry),Is.True);
            Assert.That(EntityManager.GetComponentData<GridPlacement>(entry).QuarterTurns,Is.EqualTo(1));
            Assert.That(index.TryGet(Cell(end),out var exit),Is.True);
            Assert.That(EntityManager.GetComponentData<GridPlacement>(exit).QuarterTurns,Is.EqualTo(3));
            int step=end>start ? 1 : -1;
            for(int height=start+step;height!=end;height+=step)
            {
                Assert.That(index.TryGet(Cell(height),out var segment),Is.True);
                Assert.That(EntityManager.GetComponentData<VerticalBeltNode>(segment).Step,Is.EqualTo(step));
                Assert.That(EntityManager.GetComponentData<BuildingIdentity>(segment).Level,Is.EqualTo(tier-13));
            }
        }

        [Test]
        public void UnifiedPath_ConflictRollsBackWholeBodyAndBatchOverlay()
        {
            Place(14,0); Place(14,2);
            var commands=EntityManager.GetBuffer<GridBuildCommand>(grid);
            commands.Add(new GridBuildCommand { Type=GridBuildCommandType.PlaceVerticalBeltPath,
                BuildingLevel=new BuildingLevelId { Value=14 },StartCell=Cell(0),EndCell=Cell(4) });
            commands.Add(new GridBuildCommand { Type=GridBuildCommandType.Place,
                BuildingLevel=new BuildingLevelId { Value=14 },StartCell=Cell(1) });
            UpdateSystem(build);
            var results=EntityManager.GetBuffer<GridBuildResult>(grid);
            Assert.That(results[0].Success,Is.Zero);
            Assert.That(results[0].AffectedCount,Is.Zero);
            Assert.That(results[1].Success,Is.EqualTo(1),"Rolled back temporary nodes must not block the next command.");
            var index=GetOrCreateManagedSystem<VerticalConnectorIndexSystem>();
            Assert.That(index.TryGet(Cell(3),out _),Is.False);
            Assert.That(index.TryGet(Cell(4),out _),Is.False);
            Assert.That(index.TryGet(Cell(0),out _),Is.True);
        }

        [TestCase(0,1,14)]
        [TestCase(0,3,15)]
        [TestCase(0,-3,14)]
        [TestCase(0,300,14)]
        public void UnifiedPath_RejectsInvalidLengthGradeAndNegativeLevels(int start,int end,int tier)
        {
            Place(14,start);
            EntityManager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand
            { Type=GridBuildCommandType.PlaceVerticalBeltPath,BuildingLevel=new BuildingLevelId { Value=(ushort)tier },
                StartCell=Cell(start),EndCell=Cell(end) });
            UpdateSystem(build);
            Assert.That(EntityManager.GetBuffer<GridBuildResult>(grid)[0].Success,Is.Zero);
            var index=GetOrCreateManagedSystem<VerticalConnectorIndexSystem>();
            UpdateSystem(index);
            Assert.That(index.Count,Is.EqualTo(1));
        }

        [Test]
        public void Catalog_GroupsOnlyVisibleVerticalItemsAndKeepsInternalNodes()
        {
            var hub=new UiDataHub();
            UiRuntimeServices.Attach(TestWorld,hub);
            try
            {
                BuildCatalogSnapshot received=default;
                using var subscription=hub.BuildCatalog.Subscribe(0,value=>received=value);
                UpdateSystem(GetOrCreateManagedSystem<UiSnapshotExportSystem>());
                Assert.That(received.Levels.Length,Is.EqualTo(database.Value.BuildingLevelMenu.Length-8));
                foreach(var id in received.Levels)
                {
                    Assert.That(FactoryDatabaseUtility.TryGetBuildingLevel(ref database.Value,id,out _,out var building),Is.True);
                    Assert.That(building.Kind,Is.Not.EqualTo(BuildingKind.VerticalBeltSegment));
                    Assert.That(building.Kind,Is.Not.EqualTo(BuildingKind.VerticalBeltExit));
                }
                Assert.That(FactoryDatabaseUtility.TryGetBuildingLevel(ref database.Value,new BuildingLevelId { Value=25 },out _,out _),Is.True);
            }
            finally { UiRuntimeServices.Detach(TestWorld,hub); }
        }

        private Entity Node(int level, BuildingKind kind, int step) => NodeAt(Cell(level),kind,step,East);
        private Entity NodeAt(GridCell cell, BuildingKind kind, int step,int2 direction)
        {
            Entity entity = CreateBelt(cell, direction);
            EntityManager.AddComponentData(entity, new GridPlacement { AnchorCell=cell, Kind=kind,
                QuarterTurns=step>0 ? (byte)0 : (byte)2, FootprintSize=new int2(1,1) });
            EntityManager.AddComponentData(entity, new VerticalBeltNode { Kind=kind, Step=step });
            var topology = EntityManager.GetComponentData<BeltTopology>(entity);
            topology.ConnectionMode = VerticalBeltUtility.ConnectionMode(kind);
            EntityManager.SetComponentData(entity,topology);
            return entity;
        }
        private static GridCell Cell(int level) => new GridCell(0,level,0);
        private void Enqueue(ushort level, int height, byte turns=0) => EntityManager.GetBuffer<GridBuildCommand>(grid).Add(
            new GridBuildCommand { Type=GridBuildCommandType.Place,BuildingLevel=new BuildingLevelId { Value=level },
                StartCell=Cell(height),QuarterTurns=turns });
        private GridBuildResult Place(ushort level,int height,byte turns=0)
        {
            UpdateSystem(occupancy); Enqueue(level,height,turns); UpdateSystem(build);
            return EntityManager.GetBuffer<GridBuildResult>(grid)[0];
        }
        private GridBuildResult Remove(int height)
        {
            EntityManager.GetBuffer<GridBuildCommand>(grid).Add(new GridBuildCommand { Type=GridBuildCommandType.Remove,StartCell=Cell(height) });
            UpdateSystem(build); return EntityManager.GetBuffer<GridBuildResult>(grid)[0];
        }
    }
}
