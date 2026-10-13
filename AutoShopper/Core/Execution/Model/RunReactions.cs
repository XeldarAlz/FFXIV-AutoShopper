using AutoShopper.Core.Market.Model;

namespace AutoShopper.Core.Execution.Model;

internal enum RunReaction : byte
{
    Continue = 0,
    SkipListing = 1,
    Research = 2,
    ExcludeAndResearch = 3,
    BackOffAndResearch = 4,
    SkipItem = 5,
    StopBuying = 6,
    NeedsReconciliation = 7,
    EndRun = 8,
}

internal readonly record struct RunResponse(RunReaction Reaction, bool HasIssue, RunIssueReason Issue)
{
    public static RunResponse Of(RunReaction reaction) => new(reaction, false, default);

    public static RunResponse Of(RunReaction reaction, RunIssueReason issue) => new(reaction, true, issue);
}

// What the run does after each way a purchase can fail to happen. A refused listing never comes back for the run, a
// stale copy is searched again, and anything that no other listing can fix stops buying or ends the run.
internal static class RunReactions
{
    public static RunResponse ForGuard(GuardRejection rejection) => rejection switch
    {
        GuardRejection.None => RunResponse.Of(RunReaction.Continue),
        GuardRejection.ExecutionUnavailable => RunResponse.Of(RunReaction.EndRun, RunIssueReason.UnsupportedGameVersion),
        GuardRejection.WrongRun => RunResponse.Of(RunReaction.EndRun, RunIssueReason.CharacterChanged),
        GuardRejection.WrongCharacter => RunResponse.Of(RunReaction.EndRun, RunIssueReason.CharacterChanged),
        GuardRejection.WorldNotAllowed => RunResponse.Of(RunReaction.EndRun, RunIssueReason.CharacterChanged),
        GuardRejection.WrongWorld => RunResponse.Of(RunReaction.EndRun, RunIssueReason.CharacterChanged),
        GuardRejection.StaleObservation => RunResponse.Of(RunReaction.Research),
        GuardRejection.PurchasePending => RunResponse.Of(RunReaction.NeedsReconciliation, RunIssueReason.UncertainPurchase),
        GuardRejection.InvalidListing => RunResponse.Of(RunReaction.SkipListing),
        GuardRejection.Mannequin => RunResponse.Of(RunReaction.SkipListing),
        GuardRejection.SetListing => RunResponse.Of(RunReaction.SkipListing),
        GuardRejection.OwnRetainer => RunResponse.Of(RunReaction.SkipListing),
        GuardRejection.ItemMismatch => RunResponse.Of(RunReaction.Research),
        GuardRejection.QualityMismatch => RunResponse.Of(RunReaction.Research),
        GuardRejection.QuantityMismatch => RunResponse.Of(RunReaction.Research),
        GuardRejection.ListingChanged => RunResponse.Of(RunReaction.Research),
        GuardRejection.DemandNotAuthorized => RunResponse.Of(RunReaction.SkipItem),
        GuardRejection.DemandExhausted => RunResponse.Of(RunReaction.Continue),
        GuardRejection.ExcessExceeded => RunResponse.Of(RunReaction.SkipListing),
        GuardRejection.ArithmeticOverflow => RunResponse.Of(RunReaction.SkipListing),
        GuardRejection.UnitCapExceeded => RunResponse.Of(RunReaction.SkipListing, RunIssueReason.PriceCapExceeded),
        GuardRejection.BudgetExceeded => RunResponse.Of(RunReaction.SkipListing, RunIssueReason.BudgetReached),
        GuardRejection.ReserveBreached => RunResponse.Of(RunReaction.SkipListing, RunIssueReason.InsufficientGil),
        GuardRejection.UniqueAlreadyOwned => RunResponse.Of(RunReaction.SkipItem, RunIssueReason.UniqueOwned),
        GuardRejection.InventoryFull => RunResponse.Of(RunReaction.StopBuying, RunIssueReason.InventoryFull),
        GuardRejection.CrystalLimitExceeded => RunResponse.Of(RunReaction.SkipListing, RunIssueReason.CrystalLimit),
        _ => RunResponse.Of(RunReaction.SkipListing),
    };

    // A stale copy is searched again and counts as no refusal; only a listing the live search no longer holds is
    // left out and counts toward giving up on the item.
    public static RunResponse ForLiveRead(LiveListingRead read) => read switch
    {
        LiveListingRead.Found => RunResponse.Of(RunReaction.Continue),
        LiveListingRead.Stale => RunResponse.Of(RunReaction.Research),
        _ => RunResponse.Of(RunReaction.ExcludeAndResearch),
    };

    public static RunResponse ForRefusal(PurchaseRefusal refusal) => refusal switch
    {
        PurchaseRefusal.None => RunResponse.Of(RunReaction.Continue),
        PurchaseRefusal.InvalidAttempt => RunResponse.Of(RunReaction.SkipListing),
        PurchaseRefusal.ExecutionUnavailable => RunResponse.Of(RunReaction.EndRun, RunIssueReason.UnsupportedGameVersion),
        PurchaseRefusal.AttemptPending => RunResponse.Of(RunReaction.NeedsReconciliation, RunIssueReason.UncertainPurchase),
        PurchaseRefusal.TooSoon => RunResponse.Of(RunReaction.Research),
        PurchaseRefusal.SearchInProgress => RunResponse.Of(RunReaction.Research),
        PurchaseRefusal.StaleGeneration => RunResponse.Of(RunReaction.Research),
        PurchaseRefusal.WrongWorld => RunResponse.Of(RunReaction.EndRun, RunIssueReason.CharacterChanged),
        PurchaseRefusal.ProxyUnavailable => RunResponse.Of(RunReaction.Research),
        PurchaseRefusal.SearchItemChanged => RunResponse.Of(RunReaction.Research),
        PurchaseRefusal.ListingGone => RunResponse.Of(RunReaction.ExcludeAndResearch),
        PurchaseRefusal.ListingChanged => RunResponse.Of(RunReaction.Research),
        PurchaseRefusal.ListingKindRefused => RunResponse.Of(RunReaction.SkipListing),
        PurchaseRefusal.SetLastPurchasedFailed => RunResponse.Of(RunReaction.SkipListing),
        PurchaseRefusal.LastPurchasedMismatch => RunResponse.Of(RunReaction.Research),
        PurchaseRefusal.SendReturnedFalse => RunResponse.Of(RunReaction.Research),
        _ => RunResponse.Of(RunReaction.SkipListing),
    };

    public static RunResponse ForRejection(PurchaseRejection rejection) => rejection switch
    {
        PurchaseRejection.Throttled => RunResponse.Of(RunReaction.BackOffAndResearch),
        PurchaseRejection.InventoryFull => RunResponse.Of(RunReaction.StopBuying, RunIssueReason.InventoryFull),
        PurchaseRejection.InsufficientGil => RunResponse.Of(RunReaction.StopBuying, RunIssueReason.InsufficientGil),
        PurchaseRejection.UniqueItem => RunResponse.Of(RunReaction.SkipItem, RunIssueReason.UniqueOwned),
        PurchaseRejection.NotMarketable => RunResponse.Of(RunReaction.SkipItem, RunIssueReason.NotMarketable),
        PurchaseRejection.ListingGone => RunResponse.Of(RunReaction.ExcludeAndResearch),
        PurchaseRejection.SearchFailed => RunResponse.Of(RunReaction.Research),
        _ => RunResponse.Of(RunReaction.ExcludeAndResearch, RunIssueReason.ListingRejected),
    };
}

// How often one item may be searched without buying anything, and refused, before the run gives up on it, so a board
// that keeps refusing never turns into a loop. A search that led to a purchase starts the count again, because a
// purchase can make the copy stale and a long line may need many searches.
internal struct ItemRetryBudget
{
    // The first search and three more in a row that bought nothing.
    public const int MaximumSearches = 4;
    public const int MaximumRejections = 3;

    public int Searches { get; private set; }

    public int Rejections { get; private set; }

    public readonly bool Exhausted => Searches >= MaximumSearches || Rejections >= MaximumRejections;

    public bool TryBeginSearch()
    {
        if (Exhausted)
        {
            return false;
        }

        Searches++;
        return true;
    }

    public void RecordProgress() => Searches = 0;

    public bool RecordRejection()
    {
        Rejections++;
        return !Exhausted;
    }
}
