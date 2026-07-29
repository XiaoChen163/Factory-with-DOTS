public interface IItemTransferSource
{
    bool TryCreateTransferRequest(out ItemTransferRequest request);
    void StageTransferOut(ItemState item);
}
