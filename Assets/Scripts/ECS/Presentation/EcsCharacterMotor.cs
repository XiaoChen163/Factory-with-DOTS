using Unity.CharacterController;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>Owns the official ECS character entity and synchronizes its GameObject presentation.</summary>
[DisallowMultipleComponent]
public sealed class EcsCharacterMotor : MonoBehaviour
{
    public const float Radius = 0.28f;
    public const float Height = 1.8f;
    private World world;
    private Entity character;
    private BlobAssetReference<Unity.Physics.Collider> capsule;

    public Entity CharacterEntity => character;
    public bool IsCreated => world != null && world.IsCreated && world.EntityManager.Exists(character);
    public bool IsGrounded => IsCreated && world.EntityManager.GetComponentData<KinematicCharacterBody>(character).IsGrounded;
    public bool IsFlying => IsCreated && world.EntityManager.GetComponentData<FactoryCharacterState>(character).IsFlying;
    public bool HitCeiling => IsCreated && world.EntityManager.GetComponentData<FactoryCharacterState>(character).HitCeiling;
    public uint LandingVersion => IsCreated ? world.EntityManager.GetComponentData<FactoryCharacterState>(character).LandingVersion : 0;
    public Vector3 Velocity => IsCreated ? (Vector3)world.EntityManager.GetComponentData<KinematicCharacterBody>(character).RelativeVelocity : Vector3.zero;

    public bool EnsureCreated(FactoryCharacterSettings settings)
    {
        if (IsCreated) return true;
        Release();
        world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated) return false;
        EntityManager manager = world.EntityManager;
        character = manager.CreateEntity();
        manager.SetName(character, "First Person Robot Character");
        manager.AddComponentData(character, LocalTransform.FromPositionRotation(transform.position, transform.rotation));
        manager.AddComponentData(character, new LocalToWorld { Value = float4x4.TRS(transform.position, transform.rotation, new float3(1f)) });
        manager.AddSharedComponentManaged(character, new PhysicsWorldIndex { Value = 0 });
        manager.AddComponent<Simulate>(character);
        var properties = AuthoringKinematicCharacterProperties.GetDefault();
        properties.MaxGroundedSlopeAngle = 55f;
        properties.GroundSnappingDistance = 0.08f;
        properties.EnhancedGroundPrecision = true;
        properties.MaxOverlapDecollisionIterations = 8;
        KinematicCharacterUtilities.CreateCharacter(manager, character, properties);
        var material = Unity.Physics.Material.Default;
        material.CollisionResponse = CollisionResponsePolicy.RaiseTriggerEvents;
        capsule = Unity.Physics.CapsuleCollider.Create(new CapsuleGeometry
        {
            Vertex0 = new float3(0f, Radius, 0f),
            Vertex1 = new float3(0f, Height - Radius, 0f), Radius = Radius
        }, new CollisionFilter
        {
            BelongsTo = FoundationCollisionCategories.Character,
            CollidesWith = FoundationCollisionCategories.Foundation | FoundationCollisionCategories.Character
        }, material);
        manager.AddComponentData(character, new PhysicsCollider { Value = capsule });
        settings.StepAndSlopeHandling = BasicStepAndSlopeHandlingParameters.GetDefault();
        settings.StepAndSlopeHandling.StepHandling = true;
        // Predictive center-line rays can miss exactly on voxel chunk seams.
        // Let the actual capsule support determine when the character leaves the ground.
        settings.StepAndSlopeHandling.PreventGroundingWhenMovingTowardsNoGrounding = false;
        settings.StepAndSlopeHandling.MaxStepHeight = 0.3f;
        settings.StepAndSlopeHandling.CharacterWidthForStepGroundingCheck = Radius * 2f;
        manager.AddComponentData(character, settings);
        manager.AddComponentData(character, new FactoryCharacterControl { Rotation = transform.rotation });
        manager.AddComponentData(character, new FactoryCharacterState());
        Teleport(transform.position);
        return true;
    }

    public void SubmitControl(FactoryCharacterControl control)
    {
        if (!IsCreated) return;
        EntityManager manager = world.EntityManager;
        var previous = manager.GetComponentData<FactoryCharacterControl>(character);
        control.JumpRequested |= previous.JumpRequested;
        control.ToggleFlightRequested |= previous.ToggleFlightRequested;
        manager.SetComponentData(character, control);
    }

    public void Suspend()
    {
        if (!IsCreated) return;
        world.EntityManager.SetComponentData(character, new FactoryCharacterControl { Rotation = transform.rotation });
        var body = world.EntityManager.GetComponentData<KinematicCharacterBody>(character);
        body.RelativeVelocity = float3.zero;
        world.EntityManager.SetComponentData(character, body);
    }

    public void SynchronizePresentation()
    {
        if (IsCreated) transform.position = world.EntityManager.GetComponentData<LocalToWorld>(character).Position;
    }

    public void Teleport(Vector3 position)
    {
        transform.position = position;
        if (!IsCreated) return;
        EntityManager manager = world.EntityManager;
        LocalTransform pose = manager.GetComponentData<LocalTransform>(character);
        pose.Position = position;
        manager.SetComponentData(character, pose);
        manager.SetComponentData(character, new LocalToWorld { Value = pose.ToMatrix() });
        manager.SetComponentData(character, KinematicCharacterBody.GetDefault());
        var interpolation = manager.GetComponentData<CharacterInterpolation>(character);
        interpolation.InterpolationFromTransform = new RigidTransform(pose.Rotation, pose.Position);
        interpolation.SkipNextInterpolation();
        manager.SetComponentData(character, interpolation);
    }

    private void OnDestroy() => Release();

    private void Release()
    {
        if (world != null && world.IsCreated)
        {
            world.EntityManager.CompleteAllTrackedJobs();
            if (world.EntityManager.Exists(character)) world.EntityManager.DestroyEntity(character);
            // Keep the collider alive until the physics world's next rebuild drops this body.
            if (capsule.IsCreated)
            {
                Entity owner = world.EntityManager.CreateEntity();
                world.EntityManager.AddComponentData(owner, new RetiredCharacterCollider { Value = capsule });
            }
        }
        else if (capsule.IsCreated) capsule.Dispose();
        capsule = default;
        character = Entity.Null;
        world = null;
    }
}

