using Unity.Entities;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MinerAuthoring : MonoBehaviour
{
    public Vector2Int direction = Vector2Int.right;
    public GameObject outputItemType;
    [Min(0.01f)] public float productionInterval = 1f;

    private sealed class MinerBaker : Baker<MinerAuthoring>
    {
        public override void Bake(MinerAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            TransportGridBakingUtility.AddGridData(
                this,
                entity,
                BuildingKind.Miner,
                authoring.transform.position,
                authoring.direction);

            AddComponent(entity, new MinerState
            {
                OutputItemType = authoring.outputItemType == null
                    ? Entity.Null
                    : GetEntity(
                        authoring.outputItemType,
                        TransformUsageFlags.None),
                PendingOutput = Entity.Null,
                ProductionInterval =
                    Mathf.Max(0.01f, authoring.productionInterval),
                ProductionElapsed = 0f
            });
        }
    }
}
