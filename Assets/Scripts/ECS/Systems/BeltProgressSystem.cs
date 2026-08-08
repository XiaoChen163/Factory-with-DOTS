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

        private void Execute(
            ref BeltState state,
            in BeltTopology topology)
        {
            if (state.CurrentItem == Entity.Null)
            {
                state.Progress = 0f;
                return;
            }

            state.Progress = math.min(
                state.Progress +
                topology.CellsPerSecond * DeltaTime,
                1f);
        }
    }

}
