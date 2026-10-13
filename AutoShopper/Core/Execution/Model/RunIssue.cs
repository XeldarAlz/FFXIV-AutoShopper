using AutoShopper.Core.Planning;

namespace AutoShopper.Core.Execution.Model;

internal enum RunIssueReason : byte
{
    PriceCapExceeded = 0,
    NoStock = 1,
    InventoryFull = 2,
    InsufficientGil = 3,
    TravelFailed = 4,
    DependencyBusy = 5,
    UncertainPurchase = 6,
    UnsupportedGameVersion = 7,
    BudgetReached = 8,
    SearchFailed = 9,
    RetriesExhausted = 10,
    ListingRejected = 11,
    UniqueOwned = 12,
    BoardUnreachable = 13,
    JournalWriteFailed = 14,
    CharacterChanged = 15,
    NotMarketable = 16,
}

// One thing that kept part of the run from going as planned. ItemId and WorldId are zero when the issue is not about
// one item or one world; Quantity is the units it left unbought, zero when unknown.
internal readonly record struct RunIssue(RunIssueReason Reason, uint ItemId, uint WorldId, int Quantity, DateTime AtUtc)
{
    public static RunIssueReason ForUnresolved(UnresolvedReason reason) => reason switch
    {
        UnresolvedReason.AboveUnitCap => RunIssueReason.PriceCapExceeded,
        _ => RunIssueReason.NoStock,
    };
}
