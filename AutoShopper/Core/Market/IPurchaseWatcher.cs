namespace AutoShopper.Core.Market;

internal enum PurchaseChannel : byte
{
    Market = 0,
    Vendor = 1,
}

internal interface IPurchaseWatcher
{
    PurchaseOutcome Poll();

    bool Acknowledge(Guid attemptId);

    void Abandon(Guid attemptId);
}
