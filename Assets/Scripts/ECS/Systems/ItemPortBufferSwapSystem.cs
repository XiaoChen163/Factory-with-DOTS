using Unity.Burst;
using Unity.Entities;

[BurstCompile]
[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(BeltTransferSystem))]
[UpdateBefore(typeof(ItemVisualStateCaptureSystem))]
public partial struct ItemPortBufferSwapSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        state.Dependency = new SwapPortBuffersJob()
            .ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct SwapPortBuffersJob : IJobEntity
    {
        private void Execute(
            DynamicBuffer<ItemInputPortCurrent> inputCurrent,
            DynamicBuffer<ItemInputPortNext> inputNext,
            DynamicBuffer<ItemOutputPortCurrent> outputCurrent,
            DynamicBuffer<ItemOutputPortNext> outputNext,
            DynamicBuffer<ItemTransferReceiptCurrent> receiptCurrent,
            DynamicBuffer<ItemTransferReceiptNext> receiptNext)
        {
            inputCurrent.Clear();
            for (int i = 0; i < inputNext.Length; i++)
            {
                inputCurrent.Add(new ItemInputPortCurrent
                {
                    Value = inputNext[i].Value
                });
            }
            inputNext.Clear();

            outputCurrent.Clear();
            for (int i = 0; i < outputNext.Length; i++)
            {
                outputCurrent.Add(new ItemOutputPortCurrent
                {
                    Value = outputNext[i].Value
                });
            }
            outputNext.Clear();

            receiptCurrent.Clear();
            for (int i = 0; i < receiptNext.Length; i++)
            {
                receiptCurrent.Add(new ItemTransferReceiptCurrent
                {
                    Value = receiptNext[i].Value
                });
            }
            receiptNext.Clear();
        }
    }
}
