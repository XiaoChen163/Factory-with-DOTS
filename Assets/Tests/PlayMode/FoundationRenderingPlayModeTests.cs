using System.Collections;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Factory.Tests
{
    public sealed class FoundationRenderingPlayModeTests
    {
        private World world;
        private Entity grid;
        private WorldGridConfig config;
        private SurfaceRegistrySystem surfaces;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            world = World.DefaultGameObjectInjectionWorld;
            using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldGridConfig>());
            for (int frame = 0; frame < 240 && query.CalculateEntityCount() != 1; frame++) yield return null;
            Assert.That(query.CalculateEntityCount(), Is.EqualTo(1));
            grid = query.GetSingletonEntity();
            config = query.GetSingleton<WorldGridConfig>();
            surfaces = world.GetExistingSystemManaged<SurfaceRegistrySystem>();
            for (int frame = 0; frame < 240 && !surfaces.IsReady; frame++) yield return null;
            Assert.That(surfaces.IsReady, Is.True);
        }

        [UnityTest]
        public IEnumerator RebuiltChunk_UpdatesLocalAndWorldBounds_WhenExpandedAndShrunk()
        {
            var first = new GridCell(160, 3, 160);
            var last = new GridCell(174, 3, 160);
            surfaces.AddFoundation(grid, first, 0, SurfacePermission.All, true);
            yield return null;
            yield return null;
            Entity render = FindRenderChunk(SurfaceChunkUtility.GetChunkKey(first));
            Assert.That(render, Is.Not.EqualTo(Entity.Null));
            AssertBounds(render, 160f, 161f, 3, 160);

            surfaces.AddFoundation(grid, last, 0, SurfacePermission.All, true);
            yield return null;
            yield return null;
            Assert.That(FindRenderChunk(SurfaceChunkUtility.GetChunkKey(first)), Is.EqualTo(render),
                "The regression must exercise a reused render entity.");
            AssertBounds(render, 160f, 175f, 3, 160);

            Assert.That(surfaces.RemoveFoundation(grid, first, out Entity removed), Is.True);
            world.EntityManager.DestroyEntity(removed);
            yield return null;
            yield return null;
            Assert.That(FindRenderChunk(SurfaceChunkUtility.GetChunkKey(last)), Is.EqualTo(render));
            AssertBounds(render, 174f, 175f, 3, 160);
        }

        [UnityTest]
        public IEnumerator SelectingBuildLevel_KeepsAllFoundationLevelsRenderable()
        {
            var lower = new GridCell(160, 0, 160);
            var upper = new GridCell(160, 3, 160);
            surfaces.AddFoundation(grid, lower, 0, SurfacePermission.All, true);
            surfaces.AddFoundation(grid, upper, 0, SurfacePermission.All, true);
            yield return null;
            yield return null;
            Entity lowerRender = FindRenderChunk(SurfaceChunkUtility.GetChunkKey(lower));
            Entity upperRender = FindRenderChunk(SurfaceChunkUtility.GetChunkKey(upper));
            Assert.That(lowerRender, Is.Not.EqualTo(Entity.Null));
            Assert.That(upperRender, Is.Not.EqualTo(Entity.Null));
            var interaction = Object.FindFirstObjectByType<EcsGridInteractionController>();
            interaction.SelectGridLevel(3);
            yield return null;
            yield return null;
            Assert.That(interaction.SelectedGridLevel, Is.EqualTo(3));
            Assert.That(world.EntityManager.HasComponent<DisableRendering>(lowerRender), Is.False);
            Assert.That(world.EntityManager.HasComponent<DisableRendering>(upperRender), Is.False);
            interaction.SelectGridLevel(0);
            yield return null;
            yield return null;
            Assert.That(world.EntityManager.HasComponent<DisableRendering>(lowerRender), Is.False);
            Assert.That(world.EntityManager.HasComponent<DisableRendering>(upperRender), Is.False);
            var document = Object.FindFirstObjectByType<GameUiController>().GetComponent<UIDocument>();
            Assert.That(document.rootVisualElement.Q("grid-layer-visibility"), Is.Null);
        }

        private Entity FindRenderChunk(SurfaceChunkKey key)
        {
            using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<FoundationRenderChunk>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            foreach (Entity entity in entities)
                if (world.EntityManager.GetComponentData<FoundationRenderChunk>(entity).Key.Equals(key)) return entity;
            return Entity.Null;
        }

        private void AssertBounds(Entity render, float minX, float maxX, int level, int z)
        {
            float3 expectedCenter = new float3((minX + maxX) * 0.5f * config.CellSize,
                (level - 0.5f) * config.LayerHeight, (z + 0.5f) * config.CellSize);
            float3 expectedExtents = new float3((maxX - minX) * 0.5f * config.CellSize,
                config.LayerHeight * 0.5f, config.CellSize * 0.5f);
            AABB local = world.EntityManager.GetComponentData<RenderBounds>(render).Value;
            AABB global = world.EntityManager.GetComponentData<WorldRenderBounds>(render).Value;
            Assert.That(math.distance(local.Center, expectedCenter), Is.LessThan(0.001f));
            Assert.That(math.distance(local.Extents, expectedExtents), Is.LessThan(0.001f));
            Assert.That(math.distance(global.Center, expectedCenter + config.Origin), Is.LessThan(0.001f));
            Assert.That(math.distance(global.Extents, expectedExtents), Is.LessThan(0.001f));
        }
    }
}
