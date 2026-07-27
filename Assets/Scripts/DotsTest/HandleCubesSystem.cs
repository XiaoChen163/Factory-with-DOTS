using Unity.Burst;
using Unity.Entities;
using Unity.Transforms;

public partial struct HandleCubesSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        InitCubeJob InitCubeJob = new InitCubeJob();
        InitCubeJob.ScheduleParallel();
    }

    public void OnUpdate(ref SystemState state)
    {
        // foreach (var (localTransfrom, rotateSpeed, movement) in 
        //          SystemAPI.Query<RefRW<LocalTransform>,RefRO<RotateSpeed>,RefRO<Movement>>())
        // {
        //     
        // }
        RotatingCubeJob rotatingCubeJob = new RotatingCubeJob
        {
            deltaTime = SystemAPI.Time.DeltaTime
        };
        rotatingCubeJob.ScheduleParallel();
    }
    
    [BurstCompile]
    [WithAll(typeof(RotatingCube))]
    public partial struct RotatingCubeJob : IJobEntity
    {
        public float deltaTime;
        public void Execute(ref LocalTransform localTransform, in RotateSpeed rotateSpeed, in Movement movement)
        {
            localTransform = localTransform.Translate(movement.movementVector * deltaTime);
            localTransform = localTransform.RotateY(rotateSpeed.Value * deltaTime);
        }
    }
    
    [BurstCompile]
    [WithAll(typeof(RotatingCube))]
    public partial struct InitCubeJob : IJobEntity
    {
        public void Execute(ref LocalTransform localTransform)
        {
            localTransform = localTransform.RotateY(UnityEngine.Random.Range(-90,90));
        }
    }
}
