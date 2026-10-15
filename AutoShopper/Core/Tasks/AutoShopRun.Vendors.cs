using System.Collections.Immutable;
using System.Threading.Tasks;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Inventory;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Market;
using AutoShopper.Core.Market.Model;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Travel;
using AutoShopper.Core.Vendors;
using AutoShopper.Core.Vendors.Model;
using ECommons.DalamudServices;

namespace AutoShopper.Core.Tasks;

internal sealed partial class AutoShopRun
{
    private const string VendorScope = "vendor";
    private const int MaximumUnitsPerVendorPurchase = 99;
    private const int ShopIdleTimeoutMs = 5_000;

    private async Task<RunEnd?> BuyFromVendors()
    {
        if (run.FromApi || Owner.Configuration.BuySource == BuySource.MarketBoard)
        {
            return null;
        }

        if (VendorsInPlay() && await VisitVendors() is { } end)
        {
            return end;
        }

        if (CancelToken.IsCancellationRequested)
        {
            return Cancelled;
        }

        run.Checkpoint(DateTime.UtcNow);
        if (VendorsOnly())
        {
            return null;
        }

        await ReplanRemaining();
        return CancelToken.IsCancellationRequested ? Cancelled : null;
    }

    private bool VendorsOnly() => !run.FromApi && Owner.Configuration.BuysOnlyFromVendors;

    private async Task<RunEnd?> VisitVendors()
    {
        var route = VendorRouteFor();
        for (var index = 0; index < route.Unreachable.Length; index++)
        {
            run.NoteVendorFailure(route.Unreachable[index].Demand.Key, RunIssueReason.VendorUnreachable);
        }

        if (route.Stops.IsEmpty)
        {
            return null;
        }

        RunLog.Info($"Run: {route.Stops.Length} NPC vendor stop(s) before the market board.");
        for (var index = 0; index < route.Stops.Length; index++)
        {
            var end = await BuyAtVendor(route.Stops[index]);
            await CloseVendorShop(VendorScope);
            if (CancelToken.IsCancellationRequested)
            {
                return Cancelled;
            }

            if (end is not null)
            {
                return end;
            }
        }

        return null;
    }

    private bool VendorsInPlay()
    {
        if (run.Authorization is not { } authorization)
        {
            return false;
        }

        if (!Owner.Vendors.IsReady)
        {
            RunLog.Info("Run: the vendor catalog is not ready; every item goes to the market board.");
            return false;
        }

        if (!authorization.AllowsWorld(Svc.PlayerState.CurrentWorld.RowId))
        {
            RunLog.Info("Run: this world is outside the run's worlds; NPC vendors are skipped and the market board covers every item.");
            return false;
        }

        return true;
    }

    private VendorRoutePlan VendorRouteFor()
    {
        var demands = run.Authorization!.Demands;
        var outstanding = ImmutableArray.CreateBuilder<PlanDemand>(demands.Length);
        for (var index = 0; index < demands.Length; index++)
        {
            var left = run.Tally.Outstanding(demands[index].Key);
            if (left.RequiredQuantity > 0 && !run.VendorFailed(left.Key))
            {
                outstanding.Add(left);
            }
        }

        var vendors = Owner.Vendors;
        var offers = VendorsOnly() ? ImmutableArray<PlanOffer>.Empty : run.Prices?.Offers ?? ImmutableArray<PlanOffer>.Empty;
        var split = VendorSplit.Split(outstanding.ToImmutable(), offers, vendors.Prices);
        return VendorRoute.Plan(split.Vendor, vendors, Svc.ClientState.TerritoryType, VendorReachability.CanReach);
    }

    private async Task<RunEnd?> BuyAtVendor(VendorStop stop)
    {
        var vendors = Owner.Vendors;
        var vendorName = vendors.NpcName(stop.SpotIndex);
        var placeName = vendors.PlaceName(stop.SpotIndex);
        if (stop.Spot.TerritoryId != Svc.ClientState.TerritoryType && !TripAffordable())
        {
            Warn($"Run: the trip to {vendorName} in {placeName} was not started: it could take the gil below the reserve.");
            NoteVendorFailures(stop.Purchases, RunIssueReason.InsufficientGil);
            return null;
        }

        run.SetState(RunState.Traveling);
        run.SetTravel(TravelPhase.GoingToVendor, Loc.T(L.Route.VendorTarget, vendorName, placeName));
        Status = Loc.T(L.Buyer.StatusGoingToVendor, vendorName, placeName);
        var reach = await GoToVendor(stop.Spot, vendors.PositionsOf(stop.Spot.NpcId, stop.Spot.TerritoryId), VendorScope);
        run.ClearTravel();
        if (CancelToken.IsCancellationRequested)
        {
            return Cancelled;
        }

        if (reach != VendorReach.Reached)
        {
            NoteVendorFailures(stop.Purchases, RunIssueReason.VendorUnreachable);
            return null;
        }

        var purchases = stop.Purchases;
        for (var index = 0; index < purchases.Length; index++)
        {
            var purchase = purchases[index];
            if (run.Tally.IsSatisfied(purchase.Line.Demand.Key))
            {
                continue;
            }

            switch (await BuyVendorLine(stop.Spot.NpcId, purchase, vendorName))
            {
                case ItemEnd.StopBuying:
                    return new RunEnd(RunState.CompletedPartial, "buying stopped: the bags or the purse ran out");
                case ItemEnd.NeedsReconciliation:
                    return new RunEnd(RunState.NeedsReconciliation, "a purchase has no clear answer");
                case ItemEnd.EndRun:
                    return CancelToken.IsCancellationRequested ? Cancelled : new RunEnd(RunState.Failed, "the run could not go on safely");
            }
        }

        return null;
    }

    private async Task<ItemEnd> BuyVendorLine(uint npcId, VendorPurchase purchase, string vendorName)
    {
        var demand = purchase.Line.Demand.Key;
        var itemId = demand.ItemId;
        var name = ItemName(itemId);
        var retries = 0;
        while (!CancelToken.IsCancellationRequested)
        {
            var outstanding = run.Tally.Outstanding(demand);
            if (outstanding.RequiredQuantity <= 0)
            {
                return ItemEnd.Done;
            }

            if (!await OpenVendorShop(npcId, purchase.ShopId, VendorScope))
            {
                run.NoteVendorFailure(demand, RunIssueReason.VendorUnreachable);
                return ItemEnd.Done;
            }

            if (!VendorShop.TryGetPrice(purchase.ShopId, itemId, out var unitPrice))
            {
                RunLog.Info($"Run: shop {purchase.ShopId} of {vendorName} does not show item {itemId} to this character.");
                run.NoteVendorFailure(demand, RunIssueReason.VendorUnreachable);
                return ItemEnd.Done;
            }

            if (unitPrice > purchase.Line.UnitPrice)
            {
                RunLog.Info($"Run: {vendorName} asks {unitPrice} gil for item {itemId}, more than the {purchase.Line.UnitPrice} gil it was planned at.");
                run.NoteVendorFailure(demand, RunIssueReason.VendorRefused);
                return ItemEnd.Done;
            }

            if (!await WaitUntilTimed(() => !VendorShop.TransactionBusy(purchase.ShopId), ShopIdleTimeoutMs, $"{VendorScope}-idle"))
            {
                run.NoteVendorFailure(demand, RunIssueReason.VendorRefused);
                return ItemEnd.Done;
            }

            var quantity = Math.Min(outstanding.RequiredQuantity, UnitsPerVendorPurchase(itemId));
            var response = await BuyFromVendor(purchase.ShopId, demand, quantity, unitPrice, name, vendorName);
            if (response.Reaction == RunReaction.Continue)
            {
                retries = 0;
                continue;
            }

            if (response.HasIssue && response.Issue is RunIssueReason.VendorRefused or RunIssueReason.VendorUnreachable)
            {
                run.NoteVendorFailure(demand, response.Issue);
            }
            else if (response.HasIssue)
            {
                run.AddIssue(response.Issue, itemId, 0, run.Tally.Outstanding(demand).RequiredQuantity);
            }

            switch (response.Reaction)
            {
                case RunReaction.StopBuying:
                    return ItemEnd.StopBuying;
                case RunReaction.NeedsReconciliation:
                    return ItemEnd.NeedsReconciliation;
                case RunReaction.EndRun:
                    return ItemEnd.EndRun;
                case RunReaction.Research or RunReaction.BackOffAndResearch when ++retries < ItemRetryBudget.MaximumRejections:
                    await DelayMs(BackOffMs);
                    continue;
                default:
                    return ItemEnd.Done;
            }
        }

        return ItemEnd.EndRun;
    }

    private async Task<RunResponse> BuyFromVendor(uint shopId, DemandKey demand, int quantity, long unitPrice, string name, string vendorName)
    {
        await DelayMs(Owner.Configuration.PurchaseIntervalMs);
        if (CancelToken.IsCancellationRequested)
        {
            return RunResponse.Of(RunReaction.EndRun);
        }

        var itemId = demand.ItemId;
        run.SetState(RunState.Guarding);
        Status = Loc.T(L.Buyer.StatusGuarding, name);
        var inventory = InventoryReader.ReadLive(itemId);
        run.NoteGil(inventory.Gil);
        var listing = VendorListing(itemId, quantity, unitPrice);
        var verdict = PurchaseGuard.Evaluate(VendorGuardInput(demand, listing, inventory, itemId));
        if (!verdict.IsAllowed)
        {
            RunLog.Info($"Run: {quantity} x item {itemId} from {vendorName} refused by the guard: {verdict.Rejection}.");
            return RunReactions.ForGuard(verdict.Rejection);
        }

        run.SetState(RunState.PersistingIntent);
        var attemptId = Guid.NewGuid();
        var payable = verdict.Payable;
        run.CountAttempt();
        if (!run.Journal.TryAppend(VendorIntentFor(attemptId, demand, listing, payable, inventory, shopId)))
        {
            RunLog.Warning($"Run: the intent of vendor purchase {attemptId} could not be written ({run.Journal.LastError}); nothing was bought.");
            return RunResponse.Of(RunReaction.EndRun, RunIssueReason.JournalWriteFailed);
        }

        var reservation = run.Spend.TryReserve(attemptId, payable);
        if (reservation != SpendBookResult.Applied)
        {
            RunLog.Warning($"Run: vendor purchase {attemptId} could not reserve {payable} gil ({reservation}); nothing was bought.");
            RecordSubmission(attemptId, sent: false);
            return RunResponse.Of(RunReaction.EndRun);
        }

        run.SetState(RunState.Submitting);
        var quantityText = quantity.ToString("N0", Loc.Culture);
        Status = Loc.T(L.Buyer.StatusBuyingVendor, quantityText, name, vendorName);
        var watcher = Owner.Market.VendorWatcher;
        watcher.Arm(new VendorExpectation(attemptId, itemId, shopId, quantity, payable, new PurseReading(inventory.Gil, inventory.Owned), Environment.TickCount64));
        var sent = VendorShop.TryBuy(shopId, itemId, quantity);
        if (sent != VendorBuyResult.Sent)
        {
            watcher.Abandon(attemptId);
            RecordSubmission(attemptId, sent: false);
            run.Spend.Release(attemptId);
            run.CountFailedAttempt();
            RunLog.Warning($"Run: shop {shopId} of {vendorName} did not take the purchase of item {itemId} ({sent}); nothing was bought.");
            return sent == VendorBuyResult.NotListed
                ? RunResponse.Of(RunReaction.SkipItem, RunIssueReason.VendorUnreachable)
                : RunResponse.Of(RunReaction.Research);
        }

        watcher.NoteSent();
        RecordSubmission(attemptId, sent: true);
        RunLog.Info($"Run: asked {vendorName} for {quantity} x item {itemId} at {payable} gil; waiting for the purse and bags (attempt {attemptId}).");
        run.BeginInFlight(new InFlightPurchase(new PurchaseAttempt(attemptId, listing, payable), demand, ListingKeys.ForVendorPurchase(attemptId), quantity, payable, PurchaseChannel.Vendor));
        run.SetState(RunState.AwaitingResult);
        Status = Loc.T(L.Buyer.StatusAwaiting, quantityText, name);
        if (!await WaitForSettlement())
        {
            return RunResponse.Of(RunReaction.NeedsReconciliation, RunIssueReason.UncertainPurchase);
        }

        var settlement = run.LastSettlement;
        return settlement is { Kind: SettledKind.Rejected, Durable: true }
            ? RunResponse.Of(RunReaction.SkipItem, RunIssueReason.VendorRefused)
            : ResponseFor(settlement);
    }

    private static ObservedListing VendorListing(uint itemId, int quantity, long unitPrice) => new(
        0,
        0,
        0,
        itemId,
        false,
        quantity,
        unitPrice,
        0,
        false,
        Svc.PlayerState.CurrentWorld.RowId,
        Owner.Market.Tracker.Generation,
        DateTime.UtcNow);

    private GuardInput VendorGuardInput(DemandKey demand, in ObservedListing listing, in LiveInventory inventory, uint itemId)
    {
        var found = Owner.Items.TryGet(itemId, out var item);
        var owned = inventory.Owned;
        return new GuardInput
        {
            Authorization = run.Authorization!,
            TravelCommitment = TravelCommitment(),
            ActiveRunId = run.FinalState is null ? run.RunId : Guid.Empty,
            CharacterContentId = Svc.PlayerState.ContentId,
            CurrentWorldId = Svc.PlayerState.CurrentWorld.RowId,
            SearchGeneration = listing.SearchGeneration,
            ExecutionAvailable = inventory.Available,
            Demand = demand,
            Selected = listing,
            Current = listing,
            ConfirmedQuantity = run.Tally.Acquired(demand),
            Spend = run.Spend.Snapshot(),
            CurrentGil = inventory.Gil,
            FreeInventorySlots = inventory.FreeSlots,
            ItemStackSize = found ? item.StackSize : 1,
            IsCrystal = found && item.IsCrystal,
            OwnedQuantity = owned,
            UniqueItemAlreadyOwned = found && item.IsUnique && owned > 0,
        };
    }

    private IntentRecorded VendorIntentFor(Guid attemptId, DemandKey demand, in ObservedListing listing, long payable, in LiveInventory inventory, uint shopId) => new()
    {
        Sequence = run.Journal.NextSequence,
        RunId = run.RunId,
        AtUtc = DateTime.UtcNow,
        AttemptId = attemptId,
        ItemId = listing.ItemId,
        Quality = demand.Quality,
        ListingId = 0,
        RetainerId = 0,
        WorldId = listing.WorldId,
        IsHq = false,
        Quantity = listing.Quantity,
        UnitPrice = listing.UnitPrice,
        TotalTax = 0,
        Payable = payable,
        SearchGeneration = listing.SearchGeneration,
        PlanVersion = run.Plan?.Version ?? 0,
        GilBefore = inventory.Available ? inventory.Gil : -1,
        OwnedBefore = inventory.Available ? inventory.Owned : -1,
        VendorShopId = shopId,
    };

    private static int UnitsPerVendorPurchase(uint itemId)
        => Owner.Items.TryGet(itemId, out var item) && item.StackSize > 1 ? Math.Min((int)item.StackSize, MaximumUnitsPerVendorPurchase) : 1;

    private void NoteVendorFailures(ImmutableArray<VendorPurchase> purchases, RunIssueReason reason)
    {
        for (var index = 0; index < purchases.Length; index++)
        {
            run.NoteVendorFailure(purchases[index].Line.Demand.Key, reason);
        }
    }
}
