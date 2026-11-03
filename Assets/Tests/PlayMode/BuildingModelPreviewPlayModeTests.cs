using System.Collections;
using System.Linq;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Factory.Tests
{
    public sealed class BuildingModelPreviewPlayModeTests
    {
        private EcsGridInteractionController interaction;
        private World world;
        private Entity gridEntity;
        private WorldGridConfig grid;
        private SurfaceRegistrySystem surfaces;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            world = World.DefaultGameObjectInjectionWorld;
            using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<WorldGridConfig>());
            for (int i = 0; i < 240 && query.CalculateEntityCount() != 1; i++) yield return null;
            Assert.That(query.CalculateEntityCount(), Is.EqualTo(1));
            gridEntity = query.GetSingletonEntity();
            grid = query.GetSingleton<WorldGridConfig>();
            surfaces = world.GetExistingSystemManaged<SurfaceRegistrySystem>();
            for (int i = 0; i < 240 && !surfaces.IsReady; i++) yield return null;
            Assert.That(surfaces.IsReady, Is.True);
            interaction = Object.FindFirstObjectByType<EcsGridInteractionController>();
        }

        [UnityTest]
        public IEnumerator MachineGhost_UsesPrefabMeshesAndRotation_WithValidAndInvalidColors()
        {
            var anchor = new GridCell(320, 3, 320);
            for (int z = -1; z <= 1; z++)
            for (int x = -1; x <= 1; x++)
                surfaces.AddFoundation(gridEntity, anchor + new int2(x, z), 0, SurfacePermission.All, true);
            Assert.That(interaction.TrySelectBuilding("miner"), Is.True);
            interaction.SimulateHover(anchor);
            yield return null;
            yield return null;

            Transform ghost = GameObject.Find("Building Model Preview 0").transform;
            using var query = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<FactoryDatabase>());
            var prefabs = world.EntityManager.GetBuffer<BuildingVisualPrefabEntry>(query.GetSingletonEntity());
            GameObject prefab = null;
            for (int i = 0; i < prefabs.Length; i++)
                if (prefabs[i].BuildingDefinition == interaction.SelectedBuildingDefinition)
                    prefab = prefabs[i].PreviewPrefab.Value;
            Assert.That(prefab, Is.Not.Null);
            Assert.That(ghost.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh),
                Is.EquivalentTo(prefab.GetComponentsInChildren<MeshFilter>().Select(f => f.sharedMesh)));
            Assert.That(ghost.GetComponentsInChildren<Collider>(), Is.Empty);
            Assert.That(ghost.GetComponentsInChildren<MonoBehaviour>(), Is.Empty);
            Assert.That(ghost.position, Is.EqualTo((Vector3)EcsGridUtility.CellToWorldCenter(anchor, grid)));
            var renderer = ghost.GetComponentInChildren<MeshRenderer>();
            Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("Factory/Placement Preview"));
            Color valid = renderer.sharedMaterial.GetColor("_BaseColor");
            Assert.That(valid.b, Is.GreaterThan(valid.r));
            Assert.That(valid.a, Is.InRange(0.1f, 0.9f));
            Assert.That(GameObject.Find("Placement Preview Cell 0").GetComponent<MeshRenderer>().enabled, Is.False);
            var originalMaterials = prefab.GetComponentsInChildren<MeshRenderer>().Select(r => r.sharedMaterial).ToArray();
            interaction.RotateSelectionOrToggleBeltPathOrder();
            yield return null;
            Assert.That(Quaternion.Angle(ghost.rotation, Quaternion.Euler(0, 90, 0)), Is.LessThan(0.001f));
            interaction.SimulateHover(new GridCell(180, 3, 180));
            yield return null;
            Color invalid = renderer.sharedMaterial.GetColor("_BaseColor");
            Assert.That(invalid.r, Is.GreaterThan(invalid.b));
            Assert.That(prefab.GetComponentsInChildren<MeshRenderer>().Select(r => r.sharedMaterial),
                Is.EqualTo(originalMaterials));

            Assert.That(interaction.TrySelectBuilding("foundation"), Is.True);
            yield return null;
            Assert.That(ghost.gameObject.activeSelf, Is.False);
            var foundation = GameObject.Find("Placement Preview Cell 0");
            Assert.That(foundation.GetComponent<MeshRenderer>().enabled, Is.True);
            Assert.That(foundation.transform.localScale,
                Is.EqualTo(new Vector3(grid.CellSize, grid.LayerHeight, grid.CellSize)));
            interaction.enabled = false;
            Assert.That(foundation.activeSelf, Is.False);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (interaction != null) interaction.enabled = false;
            if (world != null && world.IsCreated && surfaces != null && surfaces.IsReady)
                for (int z = -1; z <= 1; z++)
                for (int x = -1; x <= 1; x++)
                    if (surfaces.RemoveFoundation(gridEntity, new GridCell(320 + x, 3, 320 + z), out Entity removed))
                        world.EntityManager.DestroyEntity(removed);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BeltCornerGhost_UsesOneModelArrowFacingTheDiagonal()
        {
            var start = new GridCell(320, 3, 320);
            surfaces.AddFoundation(gridEntity, start, 0, SurfacePermission.All, true);
            Assert.That(interaction.TrySelectBuilding("belt_mk1"), Is.True);
            interaction.SimulateHover(start);
            interaction.SimulatePrimaryClick(start);
            Assert.That(interaction.IsBeltPathStarted, Is.True);
            // Exercise both turn orders and all four diagonal directions.
            foreach (int2 delta in new[] { new int2(2, 2), new int2(-2, 2), new int2(-2, -2), new int2(2, -2) })
            {
                interaction.SimulateHover(start + delta);
                for (int order = 0; order < 2; order++)
                {
                    yield return null;
                    yield return null;
                    Transform corner = GameObject.Find("Building Model Preview 2").transform;
                    Transform arrow = corner.GetComponentsInChildren<Transform>()
                        .Single(t => t.name == "Direction Triangle");
                    Vector3 expected = new Vector3(delta.x, 0, delta.y).normalized;
                    Assert.That(Vector3.Angle(arrow.right, expected), Is.LessThan(0.01f),
                        "The model arrow must face 45 degrees through the corner.");
                    Transform root = corner.parent;
                    Assert.That(root.GetComponentsInChildren<Transform>()
                        .Any(t => t.name == "Preview Direction Triangle"), Is.False,
                        "The extra lower triangle must stay hidden during belt placement.");
                    interaction.RotateSelectionOrToggleBeltPathOrder();
                }
            }
            // Reuse the former corner slot as a straight segment and check its arrow resets.
            interaction.SimulateHover(start + new int2(4, 0));
            yield return null;
            Transform straight = GameObject.Find("Building Model Preview 2").transform;
            Transform straightArrow = straight.GetComponentsInChildren<Transform>()
                .Single(t => t.name == "Direction Triangle");
            Assert.That(Vector3.Angle(straightArrow.right, Vector3.right), Is.LessThan(0.01f));
        }

        [UnityTest]
        public IEnumerator BeltAndVerticalGhosts_ReuseModelsAndHideWhenDisabled()
        {
            interaction.SimulateHover(new GridCell(180, 3, 180));
            foreach (string key in new[] { "belt_mk1", "vertical_belt_entry_mk1", "vertical_belt_segment_mk1" })
            {
                Assert.That(interaction.TrySelectBuilding(key), Is.True);
                yield return null;
                yield return null;
                var ghost = GameObject.Find("Building Model Preview 0");
                Assert.That(ghost, Is.Not.Null);
                Assert.That(ghost.GetComponentsInChildren<MeshRenderer>().Length, Is.GreaterThan(0));
                Assert.That(ghost.GetComponentsInChildren<MeshRenderer>().All(r =>
                    r.sharedMaterials.All(m => m.shader.name == "Factory/Placement Preview")), Is.True);
                if (key == "vertical_belt_segment_mk1")
                {
                    interaction.RotateSelectionOrToggleBeltPathOrder();
                    yield return null;
                    Assert.That(ghost.transform.GetChild(0).localPosition.y,
                        Is.EqualTo(grid.LayerHeight).Within(0.001f));
                }
            }
            interaction.enabled = false;
            Assert.That(GameObject.Find("Building Model Preview 0"), Is.Null);
        }
    }
}
