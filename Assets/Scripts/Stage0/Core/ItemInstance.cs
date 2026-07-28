using FactoryWithDots.Stage0.Data;
using UnityEngine;

namespace FactoryWithDots.Stage0.Core
{
    public sealed class ItemInstance
    {
        public ItemInstance(ItemData data, GameObject visual)
        {
            Data = data;
            Visual = visual;
        }

        public ItemData Data { get; }
        public GameObject Visual { get; }
        public float Progress { get; set; }

        public void DestroyVisual()
        {
            if (Visual != null)
            {
                Object.Destroy(Visual);
            }
        }
    }
}
