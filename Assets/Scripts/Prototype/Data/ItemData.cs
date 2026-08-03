using UnityEngine;

[CreateAssetMenu(fileName = "Item", menuName = "Factory With DOTS/Stage 0/Item")]
public sealed class ItemData : ScriptableObject
{
    [SerializeField] private string displayName = "Item";
    [SerializeField] private Sprite icon;
    [SerializeField] private Color displayColor = Color.white;

    public string DisplayName => displayName;
    public Sprite Icon => icon;
    public Color DisplayColor => displayColor;

    public void ConfigureForPrototype(string newDisplayName, Color newDisplayColor)
    {
        displayName = newDisplayName;
        displayColor = newDisplayColor;
    }
}
