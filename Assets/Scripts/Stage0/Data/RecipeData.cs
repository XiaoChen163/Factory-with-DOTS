using UnityEngine;

[CreateAssetMenu(fileName = "Recipe", menuName = "Factory With DOTS/Stage 0/Recipe")]
public sealed class RecipeData : ScriptableObject
{
    [SerializeField] private ItemData inputItem;
    [SerializeField, Min(1)] private int inputCount = 1;
    [SerializeField] private ItemData outputItem;
    [SerializeField, Min(1)] private int outputCount = 1;
    [SerializeField, Min(0.01f)] private float craftTime = 2f;

    public ItemData InputItem => inputItem;
    public int InputCount => inputCount;
    public ItemData OutputItem => outputItem;
    public int OutputCount => outputCount;
    public float CraftTime => craftTime;

    public void ConfigureForPrototype(
        ItemData newInputItem,
        int newInputCount,
        ItemData newOutputItem,
        int newOutputCount,
        float newCraftTime)
    {
        inputItem = newInputItem;
        inputCount = Mathf.Max(1, newInputCount);
        outputItem = newOutputItem;
        outputCount = Mathf.Max(1, newOutputCount);
        craftTime = Mathf.Max(0.01f, newCraftTime);
    }
}
