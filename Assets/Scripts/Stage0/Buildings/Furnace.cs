using FactoryWithDots.Stage0.Core;
using FactoryWithDots.Stage0.Data;
using FactoryWithDots.Stage0.Presentation;
using UnityEngine;

namespace FactoryWithDots.Stage0.Buildings
{
    public sealed class Furnace : PortBuilding, IItemReceiver
    {
        private int bufferedInputs;
        private int pendingOutputs;
        private float craftProgress;
        private bool crafting;

        public override BuildingKind Kind => BuildingKind.Furnace;
        public RecipeData Recipe { get; set; }
        public int BufferedInputs => bufferedInputs;
        public bool IsCrafting => crafting;
        public float CraftProgress => Recipe == null || !crafting ? 0f : Mathf.Clamp01(craftProgress / Recipe.CraftTime);

        public bool TryAccept(ItemInstance item, Vector2Int sourceCell)
        {
            if (Recipe == null ||
                item == null ||
                item.Data != Recipe.InputItem ||
                !IsValidInputSource(sourceCell) ||
                bufferedInputs >= Recipe.InputCount * 3)
            {
                return false;
            }

            bufferedInputs++;
            item.DestroyVisual();
            return true;
        }

        protected override void OnInitialized()
        {
            PrototypeVisuals.CreateMachineVisual(transform, Footprint, new Color(0.95f, 0.38f, 0.18f), "F", true, true);
        }

        private void Update()
        {
            if (Recipe == null)
            {
                return;
            }

            if (pendingOutputs > 0)
            {
                TryFlushOutput();
                return;
            }

            if (!crafting && bufferedInputs >= Recipe.InputCount)
            {
                bufferedInputs -= Recipe.InputCount;
                crafting = true;
                craftProgress = 0f;
            }

            if (!crafting)
            {
                return;
            }

            craftProgress += Time.deltaTime;
            if (craftProgress >= Recipe.CraftTime)
            {
                crafting = false;
                craftProgress = 0f;
                pendingOutputs = Recipe.OutputCount;
                TryFlushOutput();
            }
        }

        private void TryFlushOutput()
        {
            ItemInstance item = PrototypeVisuals.CreateItem(Recipe.OutputItem);
            if (TrySend(item))
            {
                pendingOutputs--;
            }
            else
            {
                item.DestroyVisual();
            }
        }
    }
}
