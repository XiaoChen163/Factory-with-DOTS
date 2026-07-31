using Unity.Entities;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class FurnaceAuthoring : MonoBehaviour
{
    public Vector2Int direction = Vector2Int.right;
    public GameObject inputItemType;
    public GameObject outputItemType;
    [Min(1)] public int requiredInputCount = 2;
    [Min(1)] public int outputCount = 1;
    [Min(0.01f)] public float craftTime = 2f;

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

            AddComponent(entity, new FurnaceState
            {
                InputItemType = GetOptionalEntity(
                    authoring.inputItemType),
                OutputItemType = GetOptionalEntity(
                    authoring.outputItemType),
                PendingOutput = Entity.Null,
                RequiredInputCount =
                    Mathf.Max(1, authoring.requiredInputCount),
                OutputCount = Mathf.Max(1, authoring.outputCount),
                BufferedInputs = 0,
                PendingOutputCount = 0,
                CraftTime = Mathf.Max(0.01f, authoring.craftTime),
                CraftProgress = 0f,
                IsCrafting = 0
            });
        }

        private Entity GetOptionalEntity(GameObject itemType)
        {
            return itemType == null
                ? Entity.Null
                : GetEntity(itemType, TransformUsageFlags.None);
        }
    }
}
