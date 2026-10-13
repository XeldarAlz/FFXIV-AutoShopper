namespace AutoShopper.Core.Market.Model;

internal enum PurchaseRejection : byte
{
    None = 0,
    Unclassified = 1,
    Generic = 2,
    Throttled = 3,
    InventoryFull = 4,
    UniqueItem = 5,
    InsufficientGil = 6,
    OwnListing = 7,
    NotMarketable = 8,
    ListingGone = 9,
    SetListing = 10,
    OwnMannequin = 11,
    SearchFailed = 12,
}

// LogMessage rows of the market blocks in game data 2026.09.15. Which of the two parallel failure blocks the board
// uses today, and whether a purchase response code is a row of this sheet at all, is still to be pinned in game.
internal static class PurchaseMessages
{
    public const uint SuccessCandidate = 734;

    public static bool IsSuccessCandidate(uint logMessageId) => logMessageId == SuccessCandidate;

    public static bool IsMarketMessage(uint logMessageId)
        => IsSuccessCandidate(logMessageId) || Classify(logMessageId) != PurchaseRejection.None;

    public static bool IsPurchaseFailure(uint logMessageId)
        => Classify(logMessageId) is not (PurchaseRejection.None or PurchaseRejection.SearchFailed);

    public static PurchaseRejection Classify(uint logMessageId) => logMessageId switch
    {
        385 => PurchaseRejection.SearchFailed,
        386 or 390 or 6084 => PurchaseRejection.Generic,
        387 or 391 => PurchaseRejection.Throttled,
        388 or 4521 => PurchaseRejection.InventoryFull,
        389 => PurchaseRejection.UniqueItem,
        392 or 4520 => PurchaseRejection.InsufficientGil,
        468 => PurchaseRejection.OwnListing,
        4249 => PurchaseRejection.NotMarketable,
        4523 => PurchaseRejection.ListingGone,
        4525 => PurchaseRejection.SetListing,
        6085 => PurchaseRejection.OwnMannequin,
        4522 or 4524 or 4526 => PurchaseRejection.Unclassified,
        _ => PurchaseRejection.None,
    };

    // A non-zero response code is a refusal whatever it means; one this table does not know stays Unclassified.
    public static PurchaseRejection ClassifyResponse(uint errorMessageId)
    {
        var rejection = Classify(errorMessageId);
        return rejection is PurchaseRejection.None or PurchaseRejection.SearchFailed ? PurchaseRejection.Unclassified : rejection;
    }
}
