public interface ITransportNode : IItemReceiver, IItemTransferSource, IGridOutputProvider
{
    ItemState CurrentItem { get; }
    float Progress { get; }

    void PrepareNextState();
    void Advance(float deltaTime);
    void StageTransferIn(ItemState item);
    void CommitNextState();
}
