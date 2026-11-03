using System;
using Unity.Entities;

[Serializable]
public struct PlayerId : IEquatable<PlayerId>
{
    public ulong Value;
    public bool IsValid => Value != 0;
    public bool Equals(PlayerId other) => Value == other.Value;
    public override bool Equals(object obj) => obj is PlayerId other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(PlayerId left, PlayerId right) => left.Value == right.Value;
    public static bool operator !=(PlayerId left, PlayerId right) => left.Value != right.Value;
}

public struct PlayerIdentity : IComponentData
{
    public PlayerId Value;
}

public struct PlayerInventory : IComponentData
{
    public ushort SlotCount;
    public uint Revision;
}

public struct ItemContainerIdentity : IComponentData
{
    public ulong RuntimeId;
}
