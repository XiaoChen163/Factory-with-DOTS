using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateBefore(typeof(BeltTransferSystem))]
public partial struct BeltProgressSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        float deltaTime = SystemAPI.Time.DeltaTime;
        state.Dependency = new BeltProgressJob
        {
            DeltaTime = deltaTime
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct BeltProgressJob : IJobEntity
    {
        public float DeltaTime;

        private void Execute(ref Belt belt)
        {
            if (belt.CurrentItem == Entity.Null)
            {
                belt.Progress = 0f;
                return;
            }

            belt.Progress = math.min(
                belt.Progress + belt.CellsPerSecond * DeltaTime,
                1f);
        }
    }

}
