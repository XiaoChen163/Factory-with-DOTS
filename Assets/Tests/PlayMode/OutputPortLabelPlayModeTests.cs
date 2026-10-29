using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Factory.Tests
{
    public sealed class OutputPortLabelPlayModeTests
    {
        [UnityTest]
        public IEnumerator FirstPersonEye_CanAimAtOutletWithoutHittingOwnCharacter()
        {
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            var player = Object.FindFirstObjectByType<FirstPersonPlayerController>();
            for (int frame = 0; frame < 240 && !player.IsSpawnFoundationReady; frame++) yield return null;
            Assert.That(player.IsSpawnFoundationReady, Is.True);
            player.enabled = false;
            var interaction = Object.FindFirstObjectByType<EcsGridInteractionController>();
            var camera = interaction.GetComponent<Camera>();
            var manager = World.DefaultGameObjectInjectionWorld.EntityManager;
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<WorldGridConfig>());
            var grid = query.GetSingleton<WorldGridConfig>();
            GridCell anchor = EcsGridUtility.WorldToCell(camera.transform.position, grid);
            anchor.X -= 6;
            anchor.Z -= 1;
            anchor.Level = EcsGridUtility.WorldYToLevel(player.transform.position.y, grid);
            var placement = new GridPlacement { AnchorCell = anchor, FootprintSize = new int2(3, 3), Kind = BuildingKind.Processor };
            var port = new BuildingPort { Type = BuildingPortType.Output, Index = 0, Number = 17,
                OutputSlot = 1, CellOffset = new int2(3, 1), Direction = new int2(1, 0) };
            Entity owner = manager.CreateEntity();
            manager.AddComponentData(owner, placement);
            manager.AddBuffer<BuildingPort>(owner).Add(port);
            var occupied = manager.AddBuffer<OccupiedCellOffset>(owner);
            for (int z = 0; z < 3; z++)
            for (int x = 0; x < 3; x++) occupied.Add(new OccupiedCellOffset { Value = new int2(x, z) });
            Vector3 center = EcsGridUtility.CellToWorldCenter(EcsGridUtility.GetBuildingCell(placement, port.CellOffset), grid);
            center += new Vector3(-0.5f, 0.375f, 0f) * grid.CellSize;
            camera.transform.rotation = Quaternion.LookRotation(center - camera.transform.position);
            bool hovered = interaction.TryGetHoveredOutputPort(out ushort number, out _);
            camera.transform.rotation = Quaternion.LookRotation(center + Vector3.up * (0.3f * grid.CellSize) - camera.transform.position);
            bool upperEdgeHovered = interaction.TryGetHoveredOutputPort(out _, out _);
            manager.DestroyEntity(owner);
            Assert.That(hovered, Is.True);
            Assert.That(number, Is.EqualTo(17));
            Assert.That(upperEdgeHovered, Is.True, "The visible port is 0.75 cells high; aiming at its upper part must hit.");
        }

        [UnityTest]
        public IEnumerator WorldTextMesh_ShowsNumberAtOutlet_AndHidesForObstructionAndRange()
        {
            yield return SceneManager.LoadSceneAsync("Ecs", LoadSceneMode.Single);
            var player = Object.FindFirstObjectByType<FirstPersonPlayerController>();
            for (int frame = 0; frame < 240 && !player.IsSpawnFoundationReady; frame++) yield return null;
            Assert.That(player.IsSpawnFoundationReady, Is.True);
            player.enabled = false;
            Cursor.lockState = CursorLockMode.Locked;
            var interaction = Object.FindFirstObjectByType<EcsGridInteractionController>();
            var camera = interaction.GetComponent<Camera>();
            World world = World.DefaultGameObjectInjectionWorld;
            var manager = world.EntityManager;
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<WorldGridConfig>());
            var grid = query.GetSingleton<WorldGridConfig>();
            GridCell anchor = EcsGridUtility.WorldToCell(camera.transform.position, grid);
            anchor.X -= 6;
            anchor.Z -= 1;
            anchor.Level = EcsGridUtility.WorldYToLevel(player.transform.position.y, grid);
            var placement = new GridPlacement { AnchorCell = anchor, FootprintSize = new int2(3, 3), Kind = BuildingKind.Processor };
            var port = new BuildingPort { Type = BuildingPortType.Output, Number = 17,
                OutputSlot = 1, CellOffset = new int2(3, 1), Direction = new int2(1, 0) };
            Entity owner = manager.CreateEntity();
            manager.AddComponentData(owner, placement);
            manager.AddBuffer<BuildingPort>(owner).Add(port);
            var occupied = manager.AddBuffer<OccupiedCellOffset>(owner);
            for (int z = 0; z < 3; z++)
            for (int x = 0; x < 3; x++) occupied.Add(new OccupiedCellOffset { Value = new int2(x, z) });
            Vector3 center = EcsGridUtility.CellToWorldCenter(EcsGridUtility.GetBuildingCell(placement, port.CellOffset), grid);
            center += new Vector3(-0.5f, 0.375f, 0f) * grid.CellSize;
            Vector3 eye = camera.transform.position;
            camera.transform.rotation = Quaternion.LookRotation(center - eye);
            MethodInfo updateLabel = typeof(EcsGridInteractionController).GetMethod("UpdateOutputPortLabel", BindingFlags.Instance | BindingFlags.NonPublic);
            GameObject blocker = null;
            try
            {
                updateLabel.Invoke(interaction, null);
                var text = interaction.GetComponentInChildren<TextMesh>(true);
                Assert.That(text, Is.Not.Null);
                Assert.That(text.gameObject.activeSelf, Is.True);
                Assert.That(text.text, Is.EqualTo("17"));
                Assert.That(Vector3.Distance(text.transform.position, center), Is.LessThan(0.4f * grid.CellSize));
                Assert.That(Quaternion.Angle(text.transform.rotation, camera.transform.rotation), Is.LessThan(0.01f));
                Assert.That(text.GetComponent<MeshRenderer>().sharedMaterial.shader.isSupported, Is.True);
                CaptureWorldLabel(camera);

                blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blocker.transform.position = (eye + center) * 0.5f;
                Physics.SyncTransforms();
                updateLabel.Invoke(interaction, null);
                Assert.That(text.gameObject.activeSelf, Is.False, "A solid wall must hide the outlet label.");
                blocker.SetActive(false);
                camera.transform.position = center + Vector3.right * 12f;
                camera.transform.rotation = Quaternion.LookRotation(center - camera.transform.position);
                updateLabel.Invoke(interaction, null);
                Assert.That(text.gameObject.activeSelf, Is.False, "The label must disappear beyond 10m.");
                camera.transform.position = eye;
                camera.transform.rotation = Quaternion.LookRotation(center - eye);
                updateLabel.Invoke(interaction, null);
                Assert.That(interaction.GetComponentInChildren<TextMesh>(true), Is.SameAs(text), "Reuse the world label.");
                Assert.That(text.gameObject.activeSelf, Is.True);
                interaction.enabled = false;
                Assert.That(text.gameObject.activeSelf, Is.False);
            }
            finally
            {
                manager.DestroyEntity(owner);
                if (blocker != null) Object.Destroy(blocker);
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private static void CaptureWorldLabel(Camera camera)
        {
            RenderTexture target = RenderTexture.GetTemporary(960, 540, 24);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            Texture2D capture = null;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                capture = new Texture2D(960, 540, TextureFormat.RGB24, false);
                capture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
                capture.Apply();
                Directory.CreateDirectory("Logs");
                File.WriteAllBytes("Logs/output-port-world-label.png", capture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                if (capture != null) Object.Destroy(capture);
            }
        }
    }
}
