using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MergerAuthoring : MonoBehaviour
{
    public Vector2Int direction = Vector2Int.right;
    public GameObject initialItem;

    private sealed class MergerBaker : Baker<MergerAuthoring>
    {
        public override void Bake(MergerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            Vector3 position = authoring.transform.position;

            AddComponent(entity, new Merger
            {
                Cell = new int2(
                    Mathf.RoundToInt(position.x),
                    Mathf.RoundToInt(position.z)),
                Direction = SanitizeDirection(authoring.direction),
                CurrentItem = authoring.initialItem == null
                    ? Entity.Null
                    : GetEntity(
                        authoring.initialItem,
                        TransformUsageFlags.Dynamic),
                TransferElapsed = 0f,
                InputInterval = 0f,
                NextInputIndex = 0
            });
        }
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
