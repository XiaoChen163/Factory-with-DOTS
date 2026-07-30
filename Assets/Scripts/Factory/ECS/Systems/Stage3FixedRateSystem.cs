using Unity.Entities;

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
            fixedStepGroup.Timestep = 1f / 60f;
        }

        Enabled = false;
    }

    protected override void OnUpdate()
    {
    }
}
