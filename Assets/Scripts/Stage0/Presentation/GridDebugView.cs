using FactoryWithDots.Stage0.Core;
using UnityEngine;

namespace FactoryWithDots.Stage0.Presentation
{
    public sealed class GridDebugView : MonoBehaviour
    {
        public void Build(GridMap grid)
        {
            CreateGround(grid);

            for (int x = 0; x <= grid.Width; x++)
            {
                CreateLine(
                    grid.Origin + new Vector3(x, 0.012f, 0f),
                    grid.Origin + new Vector3(x, 0.012f, grid.Height));
            }

            for (int y = 0; y <= grid.Height; y++)
            {
                CreateLine(
                    grid.Origin + new Vector3(0f, 0.012f, y),
                    grid.Origin + new Vector3(grid.Width, 0.012f, y));
            }

            for (int x = 0; x < grid.Width; x++)
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    Vector2Int cell = new Vector2Int(x, y);
                    TextMesh label = PrototypeVisuals.CreateWorldLabel(
                        x + "," + y,
                        transform,
                        grid.CellToWorld(cell) + Vector3.up * 0.018f,
                        20,
                        new Color(0.68f, 0.74f, 0.78f, 0.78f),
                        0.055f);
                    label.gameObject.name = "Cell " + x + "," + y;
                }
            }
        }

        private void CreateGround(GridMap grid)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Build Plane";
            ground.transform.SetParent(transform, false);
            ground.transform.position = grid.Origin + new Vector3(grid.Width * 0.5f, -0.06f, grid.Height * 0.5f);
            ground.transform.localScale = new Vector3(grid.Width, 0.1f, grid.Height);
            ground.GetComponent<Renderer>().material = PrototypeVisuals.CreateMaterial(new Color(0.08f, 0.11f, 0.13f));
        }

        private void CreateLine(Vector3 start, Vector3 end)
        {
            GameObject lineObject = new GameObject("Grid Line");
            lineObject.transform.SetParent(transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.startWidth = 0.018f;
            line.endWidth = 0.018f;
            line.material = PrototypeVisuals.GridMaterial;
            line.startColor = new Color(0.35f, 0.42f, 0.46f);
            line.endColor = line.startColor;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
