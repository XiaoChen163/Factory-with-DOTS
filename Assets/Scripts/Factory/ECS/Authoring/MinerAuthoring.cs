using System;
using Unity.Entities;
using UnityEngine;

[Serializable]
public struct MinerRecipeAuthoring
{
    public GameObject outputItemType;
    [Min(0.01f)] public float productionInterval;
}

[DisallowMultipleComponent]
public sealed class MinerAuthoring : MonoBehaviour
{
    public Vector2Int direction = Vector2Int.right;
    public GameObject outputItemType;
    [Min(0.01f)] public float productionInterval = 1f;
    public MinerRecipeAuthoring[] additionalRecipes;
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

            DynamicBuffer<ItemProcessRecipe> recipes =
                AddBuffer<ItemProcessRecipe>(entity);
            AddRecipe(
                recipes,
                authoring.outputItemType,
                authoring.productionInterval);
            if (authoring.additionalRecipes != null)
            {
                for (int i = 0;
                     i < authoring.additionalRecipes.Length;
                     i++)
                {
                    MinerRecipeAuthoring recipe =
                        authoring.additionalRecipes[i];
                    AddRecipe(
                        recipes,
                        recipe.outputItemType,
                        recipe.productionInterval);
                }
            }

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
                SelectedRecipeIndex = Mathf.Clamp(
                    authoring.initialRecipeIndex,
                    0,
                    recipes.Length - 1),
                ActiveRecipeIndex = -1,
                Status = ItemProcessStatus.Idle
            });
        }

        private void AddRecipe(
            DynamicBuffer<ItemProcessRecipe> recipes,
            GameObject itemType,
            float productionInterval)
        {
            recipes.Add(new ItemProcessRecipe
            {
                InputItemType = Entity.Null,
                OutputItemType = itemType == null
                    ? Entity.Null
                    : GetEntity(
                        itemType,
                        TransformUsageFlags.None),
                RequiredInputCount = 0,
                OutputCount = 1,
                DurationTicks = FactorySimulationTime.SecondsToTicks(
                    productionInterval)
            });
        }
    }
}
