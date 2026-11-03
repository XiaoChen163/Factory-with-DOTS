using Unity.CharacterController;
using Unity.Entities;
using Unity.Mathematics;

public struct FactoryCharacterSettings : IComponentData
{
    public float WalkSpeed, RunSpeed, FlySpeed, FlySprintMultiplier, Acceleration, Gravity, JumpSpeed;
    public BasicStepAndSlopeHandlingParameters StepAndSlopeHandling;
}

public struct FactoryCharacterControl : IComponentData
{
    public float3 MoveVector;
    public quaternion Rotation;
    public bool Active, Controlling, Sprint, Ascend, Descend;
    // Latched until consumed by a fixed physics tick.
    public bool JumpRequested, ToggleFlightRequested;
}

public struct FactoryCharacterState : IComponentData
{
    public bool IsFlying, HitCeiling;
    public uint LandingVersion;
}

public struct RetiredCharacterCollider : IComponentData
{
    public BlobAssetReference<Unity.Physics.Collider> Value;
}
