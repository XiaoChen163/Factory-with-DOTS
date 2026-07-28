using UnityEngine;

    public sealed class Miner : PortBuilding
    {
        private float productionTimer;

        public override BuildingKind Kind => BuildingKind.Miner;
        public ItemData OutputItem { get; set; }
        public float ProductionInterval { get; set; } = 1.25f;

        protected override void OnInitialized()
        {
            PrototypeVisuals.CreateMachineVisual(transform, Footprint, new Color(0.24f, 0.62f, 0.95f), "M", false, true);
        }

        private void Update()
        {
            if (OutputItem == null)
            {
                return;
            }

            productionTimer += Time.deltaTime;
            if (productionTimer < ProductionInterval)
            {
                return;
            }

            ItemInstance item = PrototypeVisuals.CreateItem(OutputItem);
            if (TrySend(item))
            {
                productionTimer -= ProductionInterval;
            }
            else
            {
                item.DestroyVisual();
            }
        }
}
