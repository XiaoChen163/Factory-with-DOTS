using System;
using UnityEngine;

[Serializable]
public struct FactoryIconBakeManifestEntry
{
    public string prefabPath;
    public string outputPath;
    public string hash;
}

public sealed class FactoryIconBakeManifest : ScriptableObject
{
    public FactoryIconBakeManifestEntry[] entries =
        Array.Empty<FactoryIconBakeManifestEntry>();
}
