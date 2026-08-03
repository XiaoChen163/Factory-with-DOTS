using Unity.Entities;

public static class FactorySimulationTime
{
    public const int TicksPerSecond = 60;
    public const float FixedDeltaTime = 1f / TicksPerSecond;

    public static int SecondsToTicks(float seconds)
    {
        return Unity.Mathematics.math.max(
            1,
            (int)Unity.Mathematics.math.ceil(
                Unity.Mathematics.math.max(0f, seconds) *
                TicksPerSecond));
    }
}

[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial class Stage3FixedRateSystem : SystemBase
{
    protected override void OnCreate()
    {
        FixedStepSimulationSystemGroup fixedStepGroup =
            World.GetExistingSystemManaged<
                FixedStepSimulationSystemGroup>();
        if (fixedStepGroup != null)
        {
            fixedStepGroup.Timestep =
                FactorySimulationTime.FixedDeltaTime;
        }

        Enabled = false;
    }

    protected override void OnUpdate()
    {
    }
}
