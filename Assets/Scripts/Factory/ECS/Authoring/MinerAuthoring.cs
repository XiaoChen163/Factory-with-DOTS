using Unity.Entities;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MinerAuthoring : MonoBehaviour
{
    public Vector2Int direction = Vector2Int.right;
    [Min(0)] public int initialRecipeIndex;

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

            AddComponent(entity, new ItemProcessor
            {
                MachineType = BuildingKind.Miner
            });
            AddBuffer<ItemProcessInput>(entity);
            AddComponent(entity, new ItemProcessCapacity
            {
                InputCapacity = 0
            });
            AddBuffer<ItemInputPortCurrent>(entity);
            AddBuffer<ItemInputPortNext>(entity);
            AddBuffer<ItemOutputPortCurrent>(entity);
            AddBuffer<ItemOutputPortNext>(entity);
            AddBuffer<ItemTransferReceiptCurrent>(entity);
            AddBuffer<ItemTransferReceiptNext>(entity);
            AddComponent(entity, new ItemProcessState
            {
                PendingOutputCount = 0,
                ElapsedTicks = 0,
                DurationTicks = 0,
                SelectedRecipeIndex = Mathf.Max(
                    0,
                    authoring.initialRecipeIndex),
                ActiveRecipeIndex = -1,
                Status = ItemProcessStatus.Idle
            });
        }

    }
}
