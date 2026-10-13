using System.Runtime.CompilerServices;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Market.Model;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace AutoShopper.Core.Market;

// The guarded listing and the payable the guard approved, under the attempt id the journal holds its intent by.
internal readonly record struct PurchaseAttempt(Guid AttemptId, ObservedListing Listing, long Payable);

internal readonly record struct PurchaseSendResult(PurchaseSendStatus Status, PurchaseRefusal Refusal, ListingField Difference, long Payable)
{
    public static PurchaseSendResult Sent(long payable) => new(PurchaseSendStatus.Sent, PurchaseRefusal.None, ListingField.None, payable);

    public static PurchaseSendResult DryRunWouldBuy(long payable) => new(PurchaseSendStatus.DryRunWouldBuy, PurchaseRefusal.None, ListingField.None, payable);

    public static PurchaseSendResult NotSent(PurchaseRefusal refusal, ListingField difference = ListingField.None)
        => new(PurchaseSendStatus.NotSent, refusal, difference, 0);
}

// Sends one whole listing exactly as guarded. The live listing is found by id, must equal the guarded one field
// for field, and the pending purchase the game copies from it must equal it again before the packet goes out. The
// quantity is never touched. Framework thread only.
internal sealed unsafe class PurchaseSender(ListingRequestTracker tracker, PurchaseResultWatcher watcher, MarketCompatibilityReport compatibility, Configuration configuration)
{
    // Public tools space purchases 1.3 to 2 s apart; a setting may ask for longer pauses, never for shorter ones.
    public const int MinimumIntervalMs = 1_500;
    public const int DefaultIntervalMs = 2_000;

    public long NextAllowedAtMs { get; private set; }

    public PurchaseSendResult Send(in PurchaseAttempt attempt, bool dryRun)
    {
        var result = TrySend(attempt, dryRun);
        if (result.Status == PurchaseSendStatus.NotSent)
        {
            var difference = result.Difference == ListingField.None ? string.Empty : $", first difference {result.Difference}";
            RunLog.Info($"Market: purchase {attempt.AttemptId} not sent: {result.Refusal}{difference} (listing {attempt.Listing.ListingId}).");
        }

        return result;
    }

    private PurchaseSendResult TrySend(in PurchaseAttempt attempt, bool dryRun)
    {
        var listing = attempt.Listing;
        if (attempt.AttemptId == Guid.Empty
            || !ListingFields.TryFrom(listing, out var guarded)
            || !listing.TryGetPayable(out var payable)
            || payable != attempt.Payable)
        {
            return PurchaseSendResult.NotSent(PurchaseRefusal.InvalidAttempt);
        }

        var precondition = CheckPreconditions(listing, dryRun);
        if (precondition != PurchaseRefusal.None)
        {
            return PurchaseSendResult.NotSent(precondition);
        }

        var proxy = InfoProxyItemSearch.Instance();
        if (proxy == null)
        {
            return PurchaseSendResult.NotSent(PurchaseRefusal.ProxyUnavailable);
        }

        if (proxy->SearchItemId != MarketItemId.Normalize(listing.ItemId))
        {
            return PurchaseSendResult.NotSent(PurchaseRefusal.SearchItemChanged);
        }

        var index = ListingRequestTracker.IndexOfListing(proxy, listing.ListingId);
        if (index < 0)
        {
            return PurchaseSendResult.NotSent(PurchaseRefusal.ListingGone);
        }

        var live = (MarketBoardListing*)Unsafe.AsPointer(ref proxy->Listings[index]);
        var difference = guarded.FirstDifference(FieldsOf(*live));
        if (difference != ListingField.None)
        {
            return PurchaseSendResult.NotSent(PurchaseRefusal.ListingChanged, difference);
        }

        if (live->IsMannequin || live->IsSellingAsSet || (live->ContentId != 0 && live->ContentId == Svc.PlayerState.ContentId))
        {
            return PurchaseSendResult.NotSent(PurchaseRefusal.ListingKindRefused);
        }

        // A dry run on a game version whose purchase addresses did not resolve stops short of staging the purchase.
        if (dryRun && !compatibility.ExecutionAvailable)
        {
            NextAllowedAtMs = PacedFrom(Environment.TickCount64);
            RunLog.Info($"Market: dry run, would buy {guarded} for {payable} gil (purchase {attempt.AttemptId}); buying is off, so the purchase was not staged.");
            return PurchaseSendResult.DryRunWouldBuy(payable);
        }

        if (!proxy->SetLastPurchasedItem(live))
        {
            return PurchaseSendResult.NotSent(PurchaseRefusal.SetLastPurchasedFailed);
        }

        difference = guarded.FirstDifference(FieldsOf(proxy->LastPurchasedMarketboardItem));
        if (difference != ListingField.None)
        {
            return PurchaseSendResult.NotSent(PurchaseRefusal.LastPurchasedMismatch, difference);
        }

        var now = Environment.TickCount64;
        NextAllowedAtMs = PacedFrom(now);
        if (dryRun)
        {
            RunLog.Info($"Market: dry run, would buy {guarded} for {payable} gil (purchase {attempt.AttemptId}).");
            return PurchaseSendResult.DryRunWouldBuy(payable);
        }

        watcher.Arm(new PurchaseExpectation(attempt.AttemptId, guarded, payable), now, configuration.ResultTimeoutMs);
        var sent = proxy->SendPurchaseRequestPacket();
        RunLog.Info($"Market: purchase {attempt.AttemptId} sent: returned {sent}, request event seen {watcher.RequestObserved}.");

        // A false return is "not sent" only when the request event did not fire; otherwise the packet may have left.
        if (!sent && !watcher.RequestObserved)
        {
            watcher.Disarm(attempt.AttemptId);
            return PurchaseSendResult.NotSent(PurchaseRefusal.SendReturnedFalse);
        }

        return PurchaseSendResult.Sent(payable);
    }

    // A dry run needs only the listings to be readable; a real purchase needs every market address and hook.
    private PurchaseRefusal CheckPreconditions(in ObservedListing listing, bool dryRun)
    {
        if (!compatibility.ReadingAvailable || (!dryRun && !compatibility.ExecutionAvailable))
        {
            return PurchaseRefusal.ExecutionUnavailable;
        }

        if (watcher.HasAttempt)
        {
            return PurchaseRefusal.AttemptPending;
        }

        if (Environment.TickCount64 < NextAllowedAtMs)
        {
            return PurchaseRefusal.TooSoon;
        }

        if (tracker.IsBusy)
        {
            return PurchaseRefusal.SearchInProgress;
        }

        if (tracker.Snapshot is null || tracker.Generation != listing.SearchGeneration)
        {
            return PurchaseRefusal.StaleGeneration;
        }

        return Svc.PlayerState.CurrentWorld.RowId == listing.WorldId ? PurchaseRefusal.None : PurchaseRefusal.WrongWorld;
    }

    private long PacedFrom(long nowMs) => nowMs + Math.Max(MinimumIntervalMs, configuration.PurchaseIntervalMs);

    private static ListingFields FieldsOf(in MarketBoardListing listing)
        => new(listing.ListingId, listing.RetainerId, listing.ItemId, listing.Quantity, listing.UnitPrice, listing.TotalTax, listing.IsHqItem);

    private static ListingFields FieldsOf(in LastPurchasedMarketboardItem pending)
        => new(pending.ListingId, pending.RetainerId, pending.ItemId, pending.Quantity, pending.UnitPrice, pending.TotalTax, pending.IsHqItem);
}
