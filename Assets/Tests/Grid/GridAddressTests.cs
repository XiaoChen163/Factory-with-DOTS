using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Factory.Tests
{
    public sealed class GridAddressTests
    {
        [Test]
        public void GridCell_LevelParticipatesInEqualityAndHashing()
        {
            GridCell ground = new GridCell(7, 0, -3);
            GridCell upper = new GridCell(7, 1, -3);
            HashSet<GridCell> cells = new HashSet<GridCell>
            {
                ground,
                upper
            };

            Assert.That(ground, Is.Not.EqualTo(upper));
            Assert.That(cells.Count, Is.EqualTo(2));
        }

        [Test]
        public void WorldGridConfig_ConvertsAllThreeAddressAxes()
        {
            WorldGridConfig grid = new WorldGridConfig
            {
                Origin = new float3(-4f, 10f, 6f),
                CellSize = 2f,
                LayerHeight = 3f
            };
            GridCell cell = new GridCell(5, -2, -7);

            float3 world = EcsGridUtility.CellToWorldCenter(cell, grid);

            Assert.That(world, Is.EqualTo(new float3(7f, 4f, -7f)));
            Assert.That(EcsGridUtility.WorldToCell(world, grid),
                Is.EqualTo(cell));
        }

        [Test]
        public void BeltPath_CrossLevelEndpointsAreRejected()
        {
            List<BeltPathCell> path = EcsGridUtility.BuildBeltPath(
                new GridCell(0, 0, 0),
                new GridCell(3, 1, 0),
                true,
                new int2(1, 0));

            Assert.That(path, Is.Empty);
        }

        [Test]
        public void TransportResolver_DoesNotConnectSameHorizontalCellsAcrossLevels()
        {
            using TransportScenario scenario = new TransportScenario();
            Entity item = scenario.CreateItem();
            scenario.AddBelt(
                new GridCell(0, 0, 0),
                new int2(1, 0),
                item,
                1f);
            scenario.AddBelt(
                new GridCell(1, 1, 0),
                new int2(1, 0));

            TransportTickResult result = scenario.ResolveTickLinear();

            Assert.That(result.ReadyRequestCount, Is.Zero);
            Assert.That(result.AcceptedTransferCount, Is.Zero);
        }

        [Test]
        public void BuildingRuntimeIds_AreMonotonicAndIndependentOfCell()
        {
            BuildingRuntimeIdAllocator allocator =
                new BuildingRuntimeIdAllocator
                {
                    NextValue = 0x8000000000000000UL
                };

            ulong first = allocator.Allocate();
            ulong second = allocator.Allocate();

            Assert.That(second, Is.EqualTo(first + 1));
        }
    }
}
