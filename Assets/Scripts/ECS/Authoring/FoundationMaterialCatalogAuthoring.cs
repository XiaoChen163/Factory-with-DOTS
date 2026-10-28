using Unity.Entities;
using UnityEngine;

public struct FoundationVisualMaterial : IBufferElementData
{
    public UnityObjectRef<Material> Value;
}

public struct FoundationLevelMaterial : IBufferElementData
{
    public BuildingLevelId BuildingLevel;
    public ushort VisualMaterialId;
}

[DisallowMultipleComponent]
public sealed class FoundationMaterialCatalogAuthoring : MonoBehaviour
{
    [SerializeField] private Material[] materials;

    private sealed class Baker : Baker<FoundationMaterialCatalogAuthoring>
    {
        public override void Bake(FoundationMaterialCatalogAuthoring authoring)
        {
            // Legacy scene component retained for serialized-scene compatibility.
            // Foundation materials are now derived from database level prefabs by
            // BuildingPrefabCatalogAuthoring so level and material IDs cannot drift.
        }
    }
}
