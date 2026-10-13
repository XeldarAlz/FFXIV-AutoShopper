using System.Threading.Tasks;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Inventory;
using AutoShopper.Core.Items;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Market;
using AutoShopper.Core.Market.Model;
using ECommons.DalamudServices;

namespace AutoShopper.Core.Tasks;

internal sealed partial class AutoShopRun
{
    // The answer can take the whole result timeout, and the watcher then needs a moment to settle; past this the run
    // stops waiting and treats the purchase as uncertain, while the run state keeps listening for it.
    private const int SettlementGraceMs = 30_000;

    // Nothing after the last wait yields a frame until the packet is sent, so a Pause or a Stop that lands during the
    // pacing wait is caught right after it.
    private async Task<RunResponse> BuyListing(ListingChoice choice, uint itemId, string name)
    {
        await WaitForPurchasePacing();
        if (CancelToken.IsCancellationRequested)
        {
            return RunResponse.Of(RunReaction.EndRun);
        }

        var market = Owner.Market;
        var read = market.Tracker.ReadLive(choice.Listing.ListingId, out var live);
        if (read != LiveListingRead.Found)
        {
            RunLog.Info($"Run: listing {choice.Listing.ListingId} of item {itemId} is {(read == LiveListingRead.Stale ? "in a stale copy" : "gone")}.");
            return RunReactions.ForLiveRead(read);
        }

        run.SetState(RunState.Guarding);
        Status = Loc.T(L.Buyer.StatusGuarding, name);
        var inventory = InventoryReader.ReadLive(itemId);
        run.NoteGil(inventory.Gil);
        var verdict = PurchaseGuard.Evaluate(GuardInputFor(choice, live, inventory, itemId));
        if (!verdict.IsAllowed)
        {
            RunLog.Info($"Run: listing {live.ListingId} of item {itemId} refused by the guard: {verdict.Rejection}.");
            return RunReactions.ForGuard(verdict.Rejection);
        }

        run.SetState(RunState.PersistingIntent);
        var attemptId = Guid.NewGuid();
        var payable = verdict.Payable;
        run.CountAttempt();
        if (!run.Journal.TryAppend(IntentFor(attemptId, choice, live, payable, inventory)))
        {
            RunLog.Warning($"Run: the intent of purchase {attemptId} could not be written ({run.Journal.LastError}); nothing was sent.");
            return RunResponse.Of(RunReaction.EndRun, RunIssueReason.JournalWriteFailed);
        }

        var reservation = run.Spend.TryReserve(attemptId, payable);
        if (reservation != SpendBookResult.Applied)
        {
            RunLog.Warning($"Run: purchase {attemptId} could not reserve {payable} gil ({reservation}); nothing was sent.");
            RecordSubmission(attemptId, sent: false);
            return RunResponse.Of(RunReaction.EndRun);
        }

        run.SetState(RunState.Submitting);
        var quantityText = live.Quantity.ToString("N0", Loc.Culture);
        Status = run.DryRun ? Loc.T(L.Buyer.StatusWouldBuy, quantityText, name) : Loc.T(L.Buyer.StatusBuying, quantityText, name);
        var attempt = new PurchaseAttempt(attemptId, live, payable);
        var result = market.Sender.Send(attempt, run.DryRun);
        switch (result.Status)
        {
            case PurchaseSendStatus.NotSent:
                RecordSubmission(attemptId, sent: false);
                run.Spend.Release(attemptId);
                run.CountFailedAttempt();
                return RunReactions.ForRefusal(result.Refusal);
            case PurchaseSendStatus.DryRunWouldBuy:
                RecordSubmission(attemptId, sent: false);
                run.Spend.Confirm(attemptId, payable);
                run.Tally.RecordWouldBuy(choice.Demand, live.ToListingKey(), live.Quantity, payable);
                RunLog.Info($"Run: dry run, would buy {live.Quantity} x {name} ({itemId}{(live.IsHq ? " HQ" : string.Empty)}) from listing {live.ListingId} for {payable} gil ({live.UnitPrice} each + {live.TotalTax} fee).");
                return RunResponse.Of(RunReaction.Continue);
        }

        RecordSubmission(attemptId, sent: true);
        run.BeginInFlight(new InFlightPurchase(attempt, choice.Demand, live.ToListingKey(), live.Quantity, payable));
        run.SetState(RunState.AwaitingResult);
        Status = Loc.T(L.Buyer.StatusAwaiting, quantityText, name);
        if (!await WaitForSettlement())
        {
            return RunResponse.Of(RunReaction.NeedsReconciliation, RunIssueReason.UncertainPurchase);
        }

        return ResponseFor(run.LastSettlement);
    }

    // An outcome the journal could not keep is as good as unknown after a restart, so the run stops on it.
    private static RunResponse ResponseFor(Settlement settlement)
    {
        if (settlement.Kind is not (SettledKind.Confirmed or SettledKind.Rejected))
        {
            return RunResponse.Of(RunReaction.NeedsReconciliation);
        }

        if (!settlement.Durable)
        {
            return RunResponse.Of(RunReaction.NeedsReconciliation, RunIssueReason.JournalWriteFailed);
        }

        return settlement.Kind == SettledKind.Confirmed ? RunResponse.Of(RunReaction.Continue) : RunReactions.ForRejection(settlement.Rejection);
    }

    // False when the answer did not settle in time; the purchase then stays in flight and buying stops.
    private async Task<bool> WaitForSettlement()
    {
        if (run.InFlight is null)
        {
            return true;
        }

        var deadline = Environment.TickCount64 + Owner.Configuration.ResultTimeoutMs + SettlementGraceMs;
        while (run.InFlight is not null)
        {
            if (Environment.TickCount64 >= deadline)
            {
                RunLog.Warning("Run: the purchase in flight did not settle in time; buying stops.");
                return false;
            }

            await NextFrame(PollFrames);
        }

        return true;
    }

    private async Task WaitForPurchasePacing()
    {
        var sender = Owner.Market.Sender;
        while (Environment.TickCount64 < sender.NextAllowedAtMs)
        {
            await NextFrame(PollFrames);
        }
    }

    private GuardInput GuardInputFor(in ListingChoice choice, in ObservedListing live, in LiveInventory inventory, uint itemId)
    {
        var compatibility = Owner.Market.Compatibility;
        var found = Owner.Items.TryGet(itemId, out var item);
        var tally = run.Tally;
        var owned = inventory.Owned + (run.DryRun ? tally.AcquiredOfItem(itemId) : 0);
        var gil = run.DryRun ? Math.Max(0, inventory.Gil - tally.WouldBuySpend) : inventory.Gil;
        return new GuardInput
        {
            Authorization = run.Authorization!,
            TravelCommitment = TravelCommitment(),
            ActiveRunId = run.FinalState is null ? run.RunId : Guid.Empty,
            CharacterContentId = Svc.PlayerState.ContentId,
            CurrentWorldId = Svc.PlayerState.CurrentWorld.RowId,
            SearchGeneration = Owner.Market.Tracker.Generation,
            ExecutionAvailable = inventory.Available && (run.DryRun ? compatibility.ReadingAvailable : compatibility.ExecutionAvailable),
            Demand = choice.Demand,
            Selected = StopSelection.Normalized(choice.Listing),
            Current = StopSelection.Normalized(live),
            ConfirmedQuantity = tally.Acquired(choice.Demand),
            Spend = run.Spend.Snapshot(),
            CurrentGil = gil,
            FreeInventorySlots = inventory.FreeSlots,
            ItemStackSize = found ? item.StackSize : 1,
            IsCrystal = found && item.IsCrystal,
            OwnedQuantity = owned,
            UniqueItemAlreadyOwned = found && item.IsUnique && owned > 0,
            OwnRetainerIds = InventoryReader.OwnRetainerIds(),
        };
    }

    // The teleports the stops still ahead and the way home may take, kept out of what purchases may spend.
    private long TravelCommitment()
    {
        var stopsAhead = run.Route?.CountWithStatus(RouteStatus.Ahead) ?? 0;
        var awayFromHome = Svc.PlayerState.CurrentWorld.RowId != Svc.PlayerState.HomeWorld.RowId;
        return TravelBudget.Commitment(stopsAhead, run.ReturnsHome(Owner.Configuration) && (stopsAhead > 0 || awayFromHome));
    }

    // A dry run counts what it would have spent as gone, as its purchases do.
    private bool TripAffordable()
    {
        var inventory = InventoryReader.ReadLive(0);
        if (!inventory.Available)
        {
            return true;
        }

        var gil = run.DryRun ? Math.Max(0, inventory.Gil - run.Tally.WouldBuySpend) : inventory.Gil;
        return TravelBudget.Affords(gil, run.Authorization!.GilReserve, TravelBudget.TeleportEstimateGil);
    }

    private IntentRecorded IntentFor(Guid attemptId, in ListingChoice choice, in ObservedListing live, long payable, in LiveInventory inventory) => new()
    {
        Sequence = run.Journal.NextSequence,
        RunId = run.RunId,
        AtUtc = DateTime.UtcNow,
        AttemptId = attemptId,
        ItemId = MarketItemId.Normalize(live.ItemId),
        Quality = choice.Demand.Quality,
        ListingId = live.ListingId,
        RetainerId = live.RetainerId,
        WorldId = live.WorldId,
        IsHq = live.IsHq,
        Quantity = live.Quantity,
        UnitPrice = live.UnitPrice,
        TotalTax = live.TotalTax,
        Payable = payable,
        SearchGeneration = live.SearchGeneration,
        PlanVersion = run.Plan?.Version ?? 0,
        GilBefore = inventory.Available ? inventory.Gil : -1,
        OwnedBefore = inventory.Available ? inventory.Owned : -1,
    };

    private void RecordSubmission(Guid attemptId, bool sent)
    {
        var written = run.Journal.TryAppend(new PurchaseSubmitted
        {
            Sequence = run.Journal.NextSequence,
            RunId = run.RunId,
            AtUtc = DateTime.UtcNow,
            AttemptId = attemptId,
            Sent = sent,
        });
        if (!written)
        {
            RunLog.Warning($"Run: the submission of purchase {attemptId} could not be written ({run.Journal.LastError}).");
        }
    }
}
