using NUnit.Framework;
using UnityEngine;

namespace Factory.Tests
{
    public sealed class VerticalBeltUsabilityTests
    {
        [TestCase(9.2f,9)]
        [TestCase(2.8f,3)]
        [TestCase(5f,5)]
        [TestCase(-4f,0)]
        [TestCase(100000f,261)]
        public void RayPlaneHeight_SnapsToLevelsAndLocksEntryColumn(float level,int expected)
        {
            var entry=new GridCell(-2,5,7);
            var grid=new WorldGridConfig { CellSize=1.5f,LayerHeight=2f,Origin=new Unity.Mathematics.float3(10,20,-5) };
            Vector3 center=(Vector3)EcsGridUtility.CellToWorldCenter(entry,grid);
            Vector3 point=new Vector3(center.x+20f,20f+level*2f,center.z);
            Ray ray=new Ray(point+new Vector3(0,10,-10),new Vector3(0,-1,1).normalized);
            Assert.That(VerticalBeltPlacementUtility.TryResolveExit(ray,new Vector3(0,-1,1),entry,grid,out var exit),Is.True);
            Assert.That(exit.X,Is.EqualTo(entry.X));
            Assert.That(exit.Z,Is.EqualTo(entry.Z));
            Assert.That(exit.Level,Is.EqualTo(expected));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FixedScreenPointer_TracksCameraTranslationAndRotation(bool orthographic)
        {
            var owner=new GameObject("Vertical Placement Camera");
            try
            {
                var camera=owner.AddComponent<Camera>();
                camera.enabled=false;
                camera.pixelRect=new Rect(0,0,800,600);
                camera.orthographic=orthographic;
                camera.orthographicSize=10f;
                var grid=new WorldGridConfig { CellSize=1,LayerHeight=1 };
                var entry=new GridCell(0,2,0);
                Vector3 target=new Vector3(.5f,5f,.5f);
                camera.transform.position=new Vector3(.5f,12f,-15f);
                camera.transform.LookAt(target);
                Vector3 pointer=camera.WorldToScreenPoint(target);
                Assert.That(VerticalBeltPlacementUtility.TryResolveExit(camera.ScreenPointToRay(pointer),
                    camera.transform.forward,entry,grid,out var first),Is.True);
                Assert.That(first.Level,Is.EqualTo(5));
                camera.transform.position+=Vector3.up*3f;
                Assert.That(VerticalBeltPlacementUtility.TryResolveExit(camera.ScreenPointToRay(pointer),
                    camera.transform.forward,entry,grid,out var translated),Is.True);
                Assert.That(translated,Is.EqualTo(new GridCell(0,8,0)));
                camera.transform.Rotate(Vector3.right,10f,Space.Self);
                Assert.That(VerticalBeltPlacementUtility.TryResolveExit(camera.ScreenPointToRay(pointer),
                    camera.transform.forward,entry,grid,out var rotated),Is.True);
                Assert.That(rotated.Level,Is.Not.EqualTo(translated.Level));
                Assert.That(rotated.Horizontal,Is.EqualTo(entry.Horizontal));
            }
            finally { Object.DestroyImmediate(owner); }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void InvalidRayOrVerticalView_DoesNotProduceAnExit(int scenario)
        {
            var grid=new WorldGridConfig { CellSize=1,LayerHeight=1 };
            Vector3 direction=scenario==0 ? Vector3.up : Vector3.back;
            Vector3 view=scenario==2 ? Vector3.down : Vector3.forward;
            Assert.That(VerticalBeltPlacementUtility.TryResolveExit(new Ray(new Vector3(.5f,5f,-5f),direction),
                view,new GridCell(0,0,0),grid,out _),Is.False);
        }

        [Test]
        public void IconSource_DefaultModelCustomModelAndSuppliedSprite()
        {
            var settings=ScriptableObject.CreateInstance<FactoryIconBakeSettings>();
            var building=new GameObject("Building");
            var custom=new GameObject("Icon Model");
            var texture=new Texture2D(2,2);
            var sprite=Sprite.Create(texture,new Rect(0,0,2,2),Vector2.one*.5f);
            try
            {
                settings.GetIconSource(building,out var model,out var image);
                Assert.That(model,Is.SameAs(building));
                Assert.That(image,Is.Null);
                settings.overrides=new[] { new FactoryIconBakeOverride { prefab=building,iconPrefab=custom } };
                settings.GetIconSource(building,out model,out image);
                Assert.That(model,Is.SameAs(custom));
                Assert.That(image,Is.Null);
                settings.overrides[0].customIcon=sprite;
                settings.GetIconSource(building,out _,out image);
                Assert.That(image,Is.SameAs(sprite));
            }
            finally
            {
                Object.DestroyImmediate(sprite); Object.DestroyImmediate(texture);
                Object.DestroyImmediate(building); Object.DestroyImmediate(custom); Object.DestroyImmediate(settings);
            }
        }
    }
}
