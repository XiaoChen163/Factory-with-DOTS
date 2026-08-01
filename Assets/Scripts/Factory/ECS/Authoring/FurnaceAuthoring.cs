using Unity.Entities;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class FurnaceAuthoring : MonoBehaviour
{
    public Vector2Int direction = Vector2Int.right;
    [Min(0)] public int initialRecipeIndex;
    [Min(1)] public int inputCapacity = 6;

    private sealed class FurnaceBaker : Baker<FurnaceAuthoring>
    {
        public override void Bake(FurnaceAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            TransportGridBakingUtility.AddGridData(
                this,
                entity,
                BuildingKind.Furnace,
                authoring.transform.position,
                authoring.direction);

            AddComponent(entity, new ItemProcessor
            {
                MachineType = BuildingKind.Furnace
            });
            AddBuffer<ItemProcessInput>(entity);
            AddComponent(entity, new ItemProcessCapacity
            {
                InputCapacity = Mathf.Max(1, authoring.inputCapacity)
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
