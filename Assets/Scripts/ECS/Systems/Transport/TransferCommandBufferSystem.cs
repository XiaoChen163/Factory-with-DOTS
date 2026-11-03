using Unity.Entities;

/// <summary>
/// Plays back the entity command buffer produced by BeltTransferSystem.
/// Keeping the playback inside the fixed-tick group lets items created or
/// destroyed by the transfer job exist before the item visual capture runs,
/// while the main thread only waits in this system instead of inside
/// BeltTransferSystem.OnUpdate.
/// </summary>
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(BeltTransferSystem))]
[UpdateBefore(typeof(ItemPortBufferSwapSystem))]
public partial class TransferCommandBufferSystem : EntityCommandBufferSystem
{
}
