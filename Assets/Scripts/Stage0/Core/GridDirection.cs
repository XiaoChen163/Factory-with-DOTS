using UnityEngine;

namespace FactoryWithDots.Stage0.Core
{
    public static class GridDirection
    {
        public static Vector2Int FromQuarterTurns(int quarterTurns)
        {
            switch (Normalize(quarterTurns))
            {
                case 0: return Vector2Int.right;
                case 1: return Vector2Int.up;
                case 2: return Vector2Int.left;
                default: return Vector2Int.down;
            }
        }

        public static Vector2Int Rotate(Vector2Int value, int quarterTurns)
        {
            switch (Normalize(quarterTurns))
            {
                case 0: return value;
                case 1: return new Vector2Int(-value.y, value.x);
                case 2: return new Vector2Int(-value.x, -value.y);
                default: return new Vector2Int(value.y, -value.x);
            }
        }

        public static int Normalize(int quarterTurns)
        {
            int normalized = quarterTurns % 4;
            return normalized < 0 ? normalized + 4 : normalized;
        }
    }
}
