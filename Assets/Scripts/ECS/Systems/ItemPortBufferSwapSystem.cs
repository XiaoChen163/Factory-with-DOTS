using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(BeltTransferSystem))]
[UpdateBefore(typeof(ItemVisualStateCaptureSystem))]
public partial struct ItemPortBufferSwapSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<ItemPortBufferGeneration>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new TogglePortBufferGenerationJob()
            .ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct TogglePortBufferGenerationJob : IJobEntity
    {
        private void Execute(ref ItemPortBufferGeneration generation)
        {
            generation.Value ^= 1;
        }
    }
}
