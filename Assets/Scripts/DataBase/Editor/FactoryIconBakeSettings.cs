using System;
using UnityEngine;

[Serializable]
public sealed class FactoryIconBakeOverride
{
    public GameObject prefab;
    public Vector3 viewDirection = new Vector3(-1f, -0.75f, -1f);
    [Min(1.01f)] public float padding = 1.12f;
    public Vector3 centerOffset;
}

public sealed class FactoryIconBakeSettings : ScriptableObject
{
    [Min(32)] public int resolution = 256;
    [Min(1.01f)] public float padding = 1.12f;
    public Vector3 viewDirection = new Vector3(-1f, -0.75f, -1f);
    public Vector3 centerOffset;
    public Color backgroundColor = new Color(0f, 0f, 0f, 0f);
    public Color ambientColor = new Color(0.34f, 0.34f, 0.34f, 1f);
    public Color keyLightColor = new Color(1f, 0.96f, 0.88f, 1f);
    [Min(0f)] public float keyLightIntensity = 1.15f;
    public Vector3 keyLightEuler = new Vector3(35f, 35f, 0f);
    public Color fillLightColor = new Color(0.70f, 0.80f, 1f, 1f);
    [Min(0f)] public float fillLightIntensity = 0.45f;
    public Vector3 fillLightEuler = new Vector3(340f, 215f, 0f);
    public FactoryIconBakeOverride[] overrides = Array.Empty<FactoryIconBakeOverride>();

    public void GetFraming(
        GameObject prefab,
        out Vector3 direction,
        out float framingPadding,
        out Vector3 framingOffset)
    {
        direction = viewDirection;
        framingPadding = padding;
        framingOffset = centerOffset;

        foreach (FactoryIconBakeOverride value in overrides)
        {
            if (value == null || value.prefab != prefab)
                continue;

            direction = value.viewDirection;
            framingPadding = value.padding;
            framingOffset = value.centerOffset;
            return;
        }
    }
}
