using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BeltAuthoring : MonoBehaviour
{
    [Min(0f)] public float speed = 1f;
    public Vector2Int direction = Vector2Int.right;
    public GameObject initialItem;

    private sealed class BeltBaker : Baker<BeltAuthoring>
    {
        public override void Bake(BeltAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            Vector3 position = authoring.transform.position;
            int2 cell = new int2(
                Mathf.RoundToInt(position.x),
                Mathf.RoundToInt(position.z));
            int2 direction = SanitizeDirection(authoring.direction);

            AddComponent(entity, new Belt
            {
                Speed = Mathf.Max(0f, authoring.speed),
                Cell = cell,
                Direction = direction,
                NextCell = cell + direction,
                CurrentItem = authoring.initialItem == null
                    ? Entity.Null
                    : GetEntity(authoring.initialItem, TransformUsageFlags.Dynamic),
                Progress = 0f,
                IsLoop = false
            });
        }

        private static int2 SanitizeDirection(Vector2Int direction)
        {
            if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y) &&
                direction.x != 0)
            {
                return new int2(direction.x > 0 ? 1 : -1, 0);
            }

            if (direction.y != 0)
            {
                return new int2(0, direction.y > 0 ? 1 : -1);
            }

            return new int2(1, 0);
        }
    }
}
