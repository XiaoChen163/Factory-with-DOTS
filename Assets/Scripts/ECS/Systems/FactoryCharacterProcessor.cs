// Adapted from Unity Character Controller 1.4.5 Standard Characters.
// Copyright Unity Technologies. Unity Companion License; see Docs/CharacterController-LICENSE.md.
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.CharacterController;

public struct FactoryCharacterUpdateContext { }

public struct FactoryCharacterProcessor : IKinematicCharacterProcessor<FactoryCharacterUpdateContext>
{
    public KinematicCharacterDataAccess CharacterDataAccess;
    public RefRW<FactoryCharacterSettings> CharacterComponent;
    public RefRW<FactoryCharacterControl> CharacterControl;
    public RefRW<FactoryCharacterState> CharacterState;

    public void PhysicsUpdate(ref FactoryCharacterUpdateContext context, ref KinematicCharacterUpdateContext baseContext)
    {
        ref FactoryCharacterSettings characterComponent = ref CharacterComponent.ValueRW;
        ref KinematicCharacterBody characterBody = ref CharacterDataAccess.CharacterBody.ValueRW;
        ref float3 characterPosition = ref CharacterDataAccess.LocalTransform.ValueRW.Position;

        ref var control = ref CharacterControl.ValueRW;
        ref var state = ref CharacterState.ValueRW;
        ref var properties = ref CharacterDataAccess.CharacterProperties.ValueRW;
        if (!control.Active) return;
        CharacterDataAccess.LocalTransform.ValueRW.Rotation = control.Rotation;
        state.HitCeiling = false;
        if (control.ToggleFlightRequested)
        {
            state.IsFlying = !state.IsFlying;
            characterBody.RelativeVelocity.y = 0f;
            characterBody.IsGrounded = false;
            control.JumpRequested = false;
            control.ToggleFlightRequested = false;
        }
        bool descending = control.Controlling && control.Descend && !control.Ascend;
        properties.EvaluateGrounding = !state.IsFlying || descending;
        properties.SnapToGround = !state.IsFlying;

        // First phase of default character update
        KinematicCharacterUtilities.Update_Initialize(
            in this,
            ref context,
            ref baseContext,
            ref characterBody,
            CharacterDataAccess.CharacterHitsBuffer,
            CharacterDataAccess.DeferredImpulsesBuffer,
            CharacterDataAccess.VelocityProjectionHits,
            baseContext.Time.DeltaTime);

        KinematicCharacterUtilities.Update_ParentMovement(
            in this,
            ref context,
            ref baseContext,
            CharacterDataAccess.CharacterEntity,
            ref characterBody,
            CharacterDataAccess.CharacterProperties.ValueRO,
            CharacterDataAccess.PhysicsCollider.ValueRO,
            CharacterDataAccess.LocalTransform.ValueRO,
            ref characterPosition,
            characterBody.WasGroundedBeforeCharacterUpdate);

        KinematicCharacterUtilities.Update_Grounding(
            in this,
            ref context,
            ref baseContext,
            ref characterBody,
            CharacterDataAccess.CharacterEntity,
            CharacterDataAccess.CharacterProperties.ValueRO,
            CharacterDataAccess.PhysicsCollider.ValueRO,
            CharacterDataAccess.LocalTransform.ValueRO,
            CharacterDataAccess.VelocityProjectionHits,
            CharacterDataAccess.CharacterHitsBuffer,
            ref characterPosition);

        // Update desired character velocity after grounding was detected, but before doing additional processing that depends on velocity
        HandleVelocityControl(ref context, ref baseContext);

        // Second phase of default character update
        KinematicCharacterUtilities.Update_PreventGroundingFromFutureSlopeChange(
            in this,
            ref context,
            ref baseContext,
            CharacterDataAccess.CharacterEntity,
            ref characterBody,
            CharacterDataAccess.CharacterProperties.ValueRO,
            CharacterDataAccess.PhysicsCollider.ValueRO,
            in characterComponent.StepAndSlopeHandling);

        KinematicCharacterUtilities.Update_GroundPushing(
            in this,
            ref context,
            ref baseContext,
            ref characterBody,
            CharacterDataAccess.CharacterProperties.ValueRO,
            CharacterDataAccess.LocalTransform.ValueRO,
            CharacterDataAccess.DeferredImpulsesBuffer,
            new float3(0f, characterComponent.Gravity, 0f));

        KinematicCharacterUtilities.Update_MovementAndDecollisions(
            in this,
            ref context,
            ref baseContext,
            CharacterDataAccess.CharacterEntity,
            ref characterBody,
            CharacterDataAccess.CharacterProperties.ValueRO,
            CharacterDataAccess.PhysicsCollider.ValueRO,
            CharacterDataAccess.LocalTransform.ValueRO,
            CharacterDataAccess.VelocityProjectionHits,
            CharacterDataAccess.CharacterHitsBuffer,
            CharacterDataAccess.DeferredImpulsesBuffer,
            ref characterPosition);

        KinematicCharacterUtilities.Update_MovingPlatformDetection(
            ref baseContext,
            ref characterBody);

        KinematicCharacterUtilities.Update_ParentMomentum(
            ref baseContext,
            ref characterBody,
            CharacterDataAccess.LocalTransform.ValueRO.Position);

        KinematicCharacterUtilities.Update_ProcessStatefulCharacterHits(
            CharacterDataAccess.CharacterHitsBuffer,
            CharacterDataAccess.StatefulHitsBuffer);
        if (state.IsFlying && descending && characterBody.IsGrounded)
        {
            state.IsFlying = false;
            state.LandingVersion++;
            characterBody.RelativeVelocity.y = 0f;
        }
        for (int i = 0; i < CharacterDataAccess.CharacterHitsBuffer.Length; i++)
            state.HitCeiling |= CharacterDataAccess.CharacterHitsBuffer[i].Normal.y < -0.5f;

    }

    void HandleVelocityControl(ref FactoryCharacterUpdateContext context, ref KinematicCharacterUpdateContext baseContext)
    {
        ref var body = ref CharacterDataAccess.CharacterBody.ValueRW;
        ref var control = ref CharacterControl.ValueRW;
        var settings = CharacterComponent.ValueRO;
        bool flying = CharacterState.ValueRO.IsFlying;
        float speed = flying ? settings.FlySpeed * (control.Sprint ? settings.FlySprintMultiplier : 1f)
            : control.Sprint ? settings.RunSpeed : settings.WalkSpeed;
        float3 target = control.Controlling ? control.MoveVector * speed : float3.zero;
        float dt = baseContext.Time.DeltaTime;
        if (!flying && body.IsGrounded)
        {
            if (!control.Controlling) body.RelativeVelocity = float3.zero;
            CharacterControlUtilities.StandardGroundMove_Interpolated(ref body.RelativeVelocity, target,
                settings.Acceleration, dt, body.GroundingUp, body.GroundHit.Normal);
            if (control.JumpRequested && control.Controlling)
                CharacterControlUtilities.StandardJump(ref body, math.up() * settings.JumpSpeed, true, math.up());
        }
        else
        {
            float3 planar = new float3(body.RelativeVelocity.x, 0f, body.RelativeVelocity.z);
            float3 delta = target - planar;
            planar = control.Controlling
                ? planar + math.normalizesafe(delta) * math.min(math.length(delta), settings.Acceleration * dt)
                : float3.zero;
            float vertical = flying
                ? (control.Controlling ? (control.Ascend ? 1f : 0f) - (control.Descend ? 1f : 0f) : 0f) * speed
                : math.max(body.RelativeVelocity.y + settings.Gravity * dt, -53f);
            body.RelativeVelocity = planar + math.up() * vertical;
        }
        control.JumpRequested = false;
    }



    #region Character Processor Callbacks

    public void UpdateGroundingUp(
        ref FactoryCharacterUpdateContext context,
        ref KinematicCharacterUpdateContext baseContext)
    {
        ref KinematicCharacterBody characterBody = ref CharacterDataAccess.CharacterBody.ValueRW;

        KinematicCharacterUtilities.Default_UpdateGroundingUp(
            ref characterBody,
            CharacterDataAccess.LocalTransform.ValueRO.Rotation);
    }

    public bool CanCollideWithHit(
        ref FactoryCharacterUpdateContext context,
        ref KinematicCharacterUpdateContext baseContext,
        in BasicHit hit)
    {
        return PhysicsUtilities.IsCollidable(hit.Material);
    }

    public bool IsGroundedOnHit(
        ref FactoryCharacterUpdateContext context,
        ref KinematicCharacterUpdateContext baseContext,
        in BasicHit hit,
        int groundingEvaluationType)
    {
        FactoryCharacterSettings characterComponent = CharacterComponent.ValueRO;

        return KinematicCharacterUtilities.Default_IsGroundedOnHit(
            in this,
            ref context,
            ref baseContext,
            CharacterDataAccess.CharacterEntity,
            CharacterDataAccess.PhysicsCollider.ValueRO,
            CharacterDataAccess.CharacterBody.ValueRO,
            CharacterDataAccess.CharacterProperties.ValueRO,
            in hit,
            in characterComponent.StepAndSlopeHandling,
            groundingEvaluationType);
    }

    public void OnMovementHit(
            ref FactoryCharacterUpdateContext context,
            ref KinematicCharacterUpdateContext baseContext,
            ref KinematicCharacterHit hit,
            ref float3 remainingMovementDirection,
            ref float remainingMovementLength,
            float3 originalVelocityDirection,
            float hitDistance)
    {
        ref KinematicCharacterBody characterBody = ref CharacterDataAccess.CharacterBody.ValueRW;
        ref float3 characterPosition = ref CharacterDataAccess.LocalTransform.ValueRW.Position;
        FactoryCharacterSettings characterComponent = CharacterComponent.ValueRO;

        KinematicCharacterUtilities.Default_OnMovementHit(
            in this,
            ref context,
            ref baseContext,
            ref characterBody,
            CharacterDataAccess.CharacterEntity,
            CharacterDataAccess.CharacterProperties.ValueRO,
            CharacterDataAccess.PhysicsCollider.ValueRO,
            CharacterDataAccess.LocalTransform.ValueRO,
            ref characterPosition,
            CharacterDataAccess.VelocityProjectionHits,
            ref hit,
            ref remainingMovementDirection,
            ref remainingMovementLength,
            originalVelocityDirection,
            hitDistance,
            characterComponent.StepAndSlopeHandling.StepHandling && !CharacterState.ValueRO.IsFlying,
            characterComponent.StepAndSlopeHandling.MaxStepHeight,
            characterComponent.StepAndSlopeHandling.CharacterWidthForStepGroundingCheck);
    }

    public void OverrideDynamicHitMasses(
        ref FactoryCharacterUpdateContext context,
        ref KinematicCharacterUpdateContext baseContext,
        ref PhysicsMass characterMass,
        ref PhysicsMass otherMass,
        BasicHit hit)
    {
        // Custom mass overrides
    }

    public void ProjectVelocityOnHits(
        ref FactoryCharacterUpdateContext context,
        ref KinematicCharacterUpdateContext baseContext,
        ref float3 velocity,
        ref bool characterIsGrounded,
        ref BasicHit characterGroundHit,
        in DynamicBuffer<KinematicVelocityProjectionHit> velocityProjectionHits,
        float3 originalVelocityDirection)
    {
        FactoryCharacterSettings characterComponent = CharacterComponent.ValueRO;

        KinematicCharacterUtilities.Default_ProjectVelocityOnHits(
            ref velocity,
            ref characterIsGrounded,
            ref characterGroundHit,
            in velocityProjectionHits,
            originalVelocityDirection,
            characterComponent.StepAndSlopeHandling.ConstrainVelocityToGroundPlane && !CharacterState.ValueRO.IsFlying,
            in CharacterDataAccess.CharacterBody.ValueRO);
    }
    #endregion
}
