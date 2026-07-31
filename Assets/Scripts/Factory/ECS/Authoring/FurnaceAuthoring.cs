using System;
using Unity.Entities;
using UnityEngine;

[Serializable]
public struct FurnaceRecipeAuthoring
{
    public GameObject inputItemType;
    public GameObject outputItemType;
    [Min(1)] public int requiredInputCount;
    [Min(1)] public int outputCount;
    [Min(0.01f)] public float craftTime;
}

[DisallowMultipleComponent]
public sealed class FurnaceAuthoring : MonoBehaviour
{
    public Vector2Int direction = Vector2Int.right;
    public GameObject inputItemType;
    public GameObject outputItemType;
    [Min(1)] public int requiredInputCount = 2;
    [Min(1)] public int outputCount = 1;
    [Min(0.01f)] public float craftTime = 2f;
    public FurnaceRecipeAuthoring[] additionalRecipes;
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

            DynamicBuffer<ItemProcessRecipe> recipes =
                AddBuffer<ItemProcessRecipe>(entity);
            AddRecipe(
                recipes,
                authoring.inputItemType,
                authoring.outputItemType,
                authoring.requiredInputCount,
                authoring.outputCount,
                authoring.craftTime);
            if (authoring.additionalRecipes != null)
            {
                for (int i = 0;
                     i < authoring.additionalRecipes.Length;
                     i++)
                {
                    FurnaceRecipeAuthoring recipe =
                        authoring.additionalRecipes[i];
                    AddRecipe(
                        recipes,
                        recipe.inputItemType,
                        recipe.outputItemType,
                        recipe.requiredInputCount,
                        recipe.outputCount,
                        recipe.craftTime);
                }
            }

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
            GameObject inputItemType,
            GameObject outputItemType,
            int requiredInputCount,
            int outputCount,
            float craftTime)
        {
            recipes.Add(new ItemProcessRecipe
            {
                InputItemType = GetOptionalEntity(inputItemType),
                OutputItemType = GetOptionalEntity(outputItemType),
                RequiredInputCount = Mathf.Max(1, requiredInputCount),
                OutputCount = Mathf.Max(1, outputCount),
                DurationTicks = FactorySimulationTime.SecondsToTicks(
                    craftTime)
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
