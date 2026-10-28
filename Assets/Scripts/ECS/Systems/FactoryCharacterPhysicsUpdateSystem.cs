// Adapted from Unity Character Controller 1.4.5 Standard Characters.
// Copyright Unity Technologies. Unity Companion License; see Docs/CharacterController-LICENSE.md.
using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Physics;
using Unity.Transforms;
using Unity.CharacterController;
using Unity.Burst.Intrinsics;

[UpdateInGroup(typeof(KinematicCharacterPhysicsUpdateGroup))]
[BurstCompile]
public partial struct FactoryCharacterPhysicsUpdateSystem : ISystem
{
    EntityQuery m_CharacterQuery;
    FactoryCharacterUpdateContext m_Context;
    KinematicCharacterUpdateContext m_BaseContext;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        m_CharacterQuery = KinematicCharacterUtilities.GetBaseCharacterQueryBuilder()
            .WithAll<FactoryCharacterSettings, FactoryCharacterControl, FactoryCharacterState>()
            .Build(ref state);

        m_Context = new FactoryCharacterUpdateContext();
        m_BaseContext = new KinematicCharacterUpdateContext();
        m_BaseContext.OnSystemCreate(ref state);

        state.RequireForUpdate(m_CharacterQuery);
        state.RequireForUpdate<PhysicsWorldSingleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        m_BaseContext.OnSystemUpdate(ref state, SystemAPI.Time, SystemAPI.GetSingleton<PhysicsWorldSingleton>());

        FactoryCharacterPhysicsUpdateJob job = new FactoryCharacterPhysicsUpdateJob
        {
            Context = m_Context,
            BaseContext = m_BaseContext,
        };
        job.ScheduleParallel();
    }

    [BurstCompile]
    [WithAll(typeof(Simulate))]
    public partial struct FactoryCharacterPhysicsUpdateJob : IJobEntity, IJobEntityChunkBeginEnd
    {
        public FactoryCharacterUpdateContext Context;
        public KinematicCharacterUpdateContext BaseContext;

        public void Execute(
            Entity entity,
            RefRW<LocalTransform> localTransform,
            RefRW<KinematicCharacterProperties> characterProperties,
            RefRW<KinematicCharacterBody> characterBody,
            RefRW<PhysicsCollider> physicsCollider,
            RefRW<FactoryCharacterSettings> characterComponent,
            RefRW<FactoryCharacterControl> characterControl,
            RefRW<FactoryCharacterState> characterState,
            DynamicBuffer<KinematicCharacterHit> characterHitsBuffer,
            DynamicBuffer<StatefulKinematicCharacterHit> statefulHitsBuffer,
            DynamicBuffer<KinematicCharacterDeferredImpulse> deferredImpulsesBuffer,
            DynamicBuffer<KinematicVelocityProjectionHit> velocityProjectionHits)
        {
            var characterProcessor = new FactoryCharacterProcessor()
            {
                CharacterDataAccess = new KinematicCharacterDataAccess(

                    entity,
                    localTransform,
                    characterProperties,
                    characterBody,
                    physicsCollider,
                    characterHitsBuffer,
                    statefulHitsBuffer,
                    deferredImpulsesBuffer,
                    velocityProjectionHits
                ),
                CharacterComponent = characterComponent,
                CharacterControl = characterControl,
                CharacterState = characterState
            };

            characterProcessor.PhysicsUpdate(ref Context, ref BaseContext);
        }

        public bool OnChunkBegin(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            BaseContext.EnsureCreationOfTmpCollections();
            return true;
        }

        public void OnChunkEnd(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask, bool chunkWasExecuted)
        {
        }
    }
}

