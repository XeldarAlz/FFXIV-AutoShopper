namespace AutoShopper.Core.Market.Model;

internal enum PurchaseSendStatus : byte
{
    Sent = 0,
    NotSent = 1,
}

internal enum PurchaseRefusal : byte
{
    None = 0,
    InvalidAttempt = 1,
    ExecutionUnavailable = 2,
    AttemptPending = 3,
    TooSoon = 4,
    SearchInProgress = 5,
    StaleGeneration = 6,
    WrongWorld = 7,
    ProxyUnavailable = 8,
    SearchItemChanged = 9,
    ListingGone = 10,
    ListingChanged = 11,
    ListingKindRefused = 12,
    SetLastPurchasedFailed = 13,
    LastPurchasedMismatch = 14,
    SendReturnedFalse = 15,
}
