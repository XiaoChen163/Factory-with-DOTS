using Unity.Entities;
using UnityEngine;

public struct FoundationVisualMaterial : IBufferElementData
{
    public UnityObjectRef<Material> Value;
}

[DisallowMultipleComponent]
public sealed class FoundationMaterialCatalogAuthoring : MonoBehaviour
{
    [SerializeField] private Material[] materials;

    private sealed class Baker : Baker<FoundationMaterialCatalogAuthoring>
    {
        public override void Bake(FoundationMaterialCatalogAuthoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            DynamicBuffer<FoundationVisualMaterial> buffer =
                AddBuffer<FoundationVisualMaterial>(entity);
            if (authoring.materials == null) return;
            for (int i = 0; i < authoring.materials.Length; i++)
                buffer.Add(new FoundationVisualMaterial
                {
                    Value = (UnityObjectRef<Material>)authoring.materials[i]
                });
        }
    }
}
