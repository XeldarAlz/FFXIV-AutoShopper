using System.Collections.Immutable;
using System.Threading.Tasks;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Market;
using AutoShopper.Core.Market.Model;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Travel;
using ECommons.DalamudServices;

namespace AutoShopper.Core.Tasks;

internal enum StopReach : byte
{
    Reached = 0,
    TravelFailed = 1,
    BoardUnreachable = 2,
    // Logged out, or back from a transfer in a way only the player's Resume may carry on from.
    LeftForResume = 3,
    // The trip could take the purse below the reserve, so it was not started.
    OutOfGil = 4,
    // A party, a duty, a trade or an open window kept the trip from starting, and the run paused for the player.
    Held = 5,
}

internal enum ItemEnd : byte
{
    Done = 0,
    StopBuying = 1,
    NeedsReconciliation = 2,
    EndRun = 3,
}

internal enum ChoicesEnd : byte
{
    AllBought = 0,
    Research = 1,
    SkipItem = 2,
    StopBuying = 3,
    NeedsReconciliation = 4,
    EndRun = 5,
}

internal sealed partial class AutoShopRun
{
    // Four attempts with their pacing, backoff and timeouts fit well inside this.
    private const int SearchBudgetMs = 90_000;
    // Long enough for the board's "please wait" to pass before the item is searched again.
    private const int BackOffMs = 5_000;

    // A Pause or a Stop unwinds the task; the controller decides what the run becomes, so this end is never concluded.
    private static readonly RunEnd Cancelled = new(RunState.Stopped, "cancelled");

    private ItemRetryBudget itemBudget;

    // Null when the run was left for the player's Resume; nothing is concluded then.
    private async Task<RunEnd?> ShopStops()
    {
        while (run.Route is { } route && route.NextIndex() is var stopIndex and >= 0)
        {
            var stop = route[stopIndex].Stop;
            run.BeginStop(stopIndex);
            var reach = await ReachStop(stop);
            if (CancelToken.IsCancellationRequested)
            {
                return Cancelled;
            }

            switch (reach)
            {
                case StopReach.LeftForResume:
                    return LeaveForResume();
                case StopReach.OutOfGil:
                    run.AddIssue(RunIssueReason.InsufficientGil, 0, stop.World.WorldId, 0);
                    run.SkipAhead(RouteSkip.TravelHalted, TravelFailure.None);
                    return new RunEnd(RunState.CompletedPartial, "the next trip could take the gil below the reserve");
                case StopReach.Held:
                    run.AddIssue(RunIssueReason.TravelFailed, 0, stop.World.WorldId, 0);
                    return new RunEnd(RunState.CompletedPartial, "a party, a duty, a trade or an open window held the trip up");
                case StopReach.BoardUnreachable:
                    run.AddIssue(RunIssueReason.BoardUnreachable, 0, stop.World.WorldId, 0);
                    return new RunEnd(RunState.Failed, "no market board could be reached");
                case StopReach.TravelFailed:
                    await AfterTravelFailure(stopIndex, stop.World);
                    if (CancelToken.IsCancellationRequested)
                    {
                        return Cancelled;
                    }

                    continue;
            }

            if (await ShopAt(stop) is { } end)
            {
                return end;
            }

            run.CompleteStop(stopIndex);
            run.Checkpoint(DateTime.UtcNow);
            await ReplanRemaining();
            if (CancelToken.IsCancellationRequested)
            {
                return Cancelled;
            }
        }

        return run.Tally.AllSatisfied
            ? new RunEnd(RunState.Completed, "every line is bought")
            : new RunEnd(RunState.CompletedPartial, "some lines could not be bought");
    }

    private async Task<StopReach> ReachStop(PlanStop stop)
    {
        if (stop.World.WorldId != Svc.PlayerState.CurrentWorld.RowId)
        {
            if (!TripAffordable())
            {
                Warn($"Run: the trip to {stop.World.Name} was not started: it could take the gil below the reserve of {run.Authorization!.GilReserve}.");
                return StopReach.OutOfGil;
            }

            var travel = await TravelToStop(stop.World);
            if (travel != StopReach.Reached || CancelToken.IsCancellationRequested)
            {
                return travel;
            }
        }

        run.SetState(RunState.GoingToBoard);
        run.SetTravel(TravelPhase.GoingToBoard, stop.World.Name);
        var opened = await EnsureBoardOpen();
        run.ClearTravel();
        return opened ? StopReach.Reached : StopReach.BoardUnreachable;
    }

    private async Task<bool> EnsureBoardOpen()
    {
        if (MarketBoardDriver.IsSearchWindowReady())
        {
            return true;
        }

        if (!MarketCityChoice.TryChoose(Svc.ClientState.TerritoryType, Owner.Configuration.PreferredMarketCity, ZoneAetherytes.IsAttuned, out var city))
        {
            Warn("Run: no market city's aetheryte is attuned on this character.");
            return false;
        }

        var arrival = await GoToMarketBoard(city, Scope);
        return arrival.Opened;
    }

    private async Task<RunEnd?> ShopAt(PlanStop stop)
    {
        var items = RunPlanning.ItemsOf(stop);
        for (var index = 0; index < items.Length; index++)
        {
            var demands = RunPlanning.DemandsOfItem(run.Authorization!, items[index]);
            if (Satisfied(demands))
            {
                continue;
            }

            switch (await BuyItem(items[index], demands, stop.World))
            {
                case ItemEnd.StopBuying:
                    return new RunEnd(RunState.CompletedPartial, "buying stopped: the bags or the purse ran out");
                case ItemEnd.NeedsReconciliation:
                    return new RunEnd(RunState.NeedsReconciliation, "a purchase has no clear answer");
                case ItemEnd.EndRun:
                    return new RunEnd(RunState.Failed, "the run could not go on safely");
            }
        }

        return null;
    }

    private async Task<ItemEnd> BuyItem(uint itemId, ImmutableArray<PlanDemand> demands, PlanWorld world)
    {
        itemBudget = default;
        var name = ItemName(itemId);
        while (true)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return ItemEnd.EndRun;
            }

            if (Satisfied(demands))
            {
                return ItemEnd.Done;
            }

            if (!itemBudget.TryBeginSearch())
            {
                run.AddIssue(RunIssueReason.RetriesExhausted, itemId, world.WorldId, Missing(demands));
                return ItemEnd.Done;
            }

            if (!await EnsureBoardOpen())
            {
                if (!CancelToken.IsCancellationRequested)
                {
                    run.AddIssue(RunIssueReason.BoardUnreachable, itemId, world.WorldId, Missing(demands));
                }

                return ItemEnd.EndRun;
            }

            run.SetState(RunState.Searching);
            Status = Loc.T(L.Buyer.StatusSearching, name);
            var snapshot = await SearchListings(itemId);
            if (CancelToken.IsCancellationRequested)
            {
                return ItemEnd.EndRun;
            }

            if (snapshot is null)
            {
                run.AddIssue(RunIssueReason.SearchFailed, itemId, world.WorldId, Missing(demands));
                return ItemEnd.Done;
            }

            run.SetState(RunState.Inspecting);
            Status = Loc.T(L.Buyer.StatusChoosing, name);
            var choice = await ChooseListings(demands, world, snapshot);
            if (choice is null || choice.Choices.IsEmpty)
            {
                NoteUnresolved(choice, demands, world);
                return ItemEnd.Done;
            }

            switch (await BuyChoices(itemId, name, choice.Choices, world))
            {
                case ChoicesEnd.AllBought when !choice.Unresolved.IsEmpty:
                    NoteUnresolved(choice, demands, world);
                    return ItemEnd.Done;
                case ChoicesEnd.SkipItem:
                    return ItemEnd.Done;
                case ChoicesEnd.StopBuying:
                    return ItemEnd.StopBuying;
                case ChoicesEnd.NeedsReconciliation:
                    return ItemEnd.NeedsReconciliation;
                case ChoicesEnd.EndRun:
                    return ItemEnd.EndRun;
            }
        }
    }

    private async Task<ChoicesEnd> BuyChoices(uint itemId, string name, ImmutableArray<ListingChoice> choices, PlanWorld world)
    {
        for (var index = 0; index < choices.Length; index++)
        {
            var choice = choices[index];
            if (run.Tally.IsSatisfied(choice.Demand))
            {
                continue;
            }

            var acquiredBefore = run.Tally.Purchases;
            var response = await BuyListing(choice, itemId, name);
            if (run.Tally.Purchases > acquiredBefore)
            {
                itemBudget.RecordProgress();
            }

            if (response.HasIssue)
            {
                run.AddIssue(response.Issue, itemId, world.WorldId, MissingOfItem(itemId));
            }

            switch (response.Reaction)
            {
                case RunReaction.Continue:
                    continue;
                case RunReaction.SkipListing:
                    run.Exclude(choice.Listing.ToListingKey());
                    continue;
                case RunReaction.Research:
                    return ChoicesEnd.Research;
                case RunReaction.ExcludeAndResearch:
                    run.Exclude(choice.Listing.ToListingKey());
                    return AfterRejection(itemId, world);
                case RunReaction.BackOffAndResearch:
                    Status = Loc.T(L.Buyer.StatusBackOff);
                    await DelayMs(BackOffMs);
                    if (CancelToken.IsCancellationRequested)
                    {
                        return ChoicesEnd.EndRun;
                    }

                    return AfterRejection(itemId, world);
                case RunReaction.SkipItem:
                    return ChoicesEnd.SkipItem;
                case RunReaction.StopBuying:
                    return ChoicesEnd.StopBuying;
                case RunReaction.NeedsReconciliation:
                    return ChoicesEnd.NeedsReconciliation;
                default:
                    return ChoicesEnd.EndRun;
            }
        }

        return ChoicesEnd.AllBought;
    }

    private ChoicesEnd AfterRejection(uint itemId, PlanWorld world)
    {
        if (itemBudget.RecordRejection())
        {
            return ChoicesEnd.Research;
        }

        run.AddIssue(RunIssueReason.RetriesExhausted, itemId, world.WorldId, MissingOfItem(itemId));
        return ChoicesEnd.SkipItem;
    }

    // One search at a time through the tracker, polled once a frame. A search left behind by a Pause or a Stop is
    // cancelled, so the tracker never stays busy for the next run.
    private async Task<ListingSnapshot?> SearchListings(uint itemId)
    {
        var tracker = Owner.Market.Tracker;
        if (CancelToken.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            var state = tracker.Request(itemId);
            var deadline = Environment.TickCount64 + SearchBudgetMs;
            while (state.Status == ListingRequestStatus.Waiting)
            {
                if (CancelToken.IsCancellationRequested || Environment.TickCount64 >= deadline)
                {
                    return null;
                }

                await NextFrame(PollFrames);
                state = tracker.Poll();
            }

            if (state.Snapshot is null)
            {
                RunLog.Info($"Run: the search for item {itemId} failed ({state.Failure}).");
            }

            return state.Snapshot;
        }
        finally
        {
            tracker.Cancel();
        }
    }

    private Task<StopChoice?> ChooseListings(ImmutableArray<PlanDemand> demands, PlanWorld world, ListingSnapshot snapshot)
    {
        var plan = run.Plan!;
        var acquisitions = run.Tally.Acquisitions();
        var excluded = run.ExcludedListingKeys;
        return OffThread(token => StopSelection.Choose(new Planner(), plan, demands, acquisitions, excluded, world, snapshot.Listings, snapshot.Marks, token), "choosing listings");
    }

    private void NoteUnresolved(StopChoice? choice, ImmutableArray<PlanDemand> demands, PlanWorld world)
    {
        if (choice is null || choice.Unresolved.IsEmpty)
        {
            var missing = Missing(demands);
            if (missing > 0 && demands.Length > 0)
            {
                run.AddIssue(RunIssueReason.NoStock, demands[0].ItemId, world.WorldId, missing);
            }

            return;
        }

        for (var index = 0; index < choice.Unresolved.Length; index++)
        {
            var unresolved = choice.Unresolved[index];
            run.AddIssue(RunIssue.ForUnresolved(unresolved.Reason), unresolved.Demand.ItemId, world.WorldId, MissingOfItem(unresolved.Demand.ItemId));
        }
    }

    private bool Satisfied(ImmutableArray<PlanDemand> demands)
    {
        for (var index = 0; index < demands.Length; index++)
        {
            if (!run.Tally.IsSatisfied(demands[index].Key))
            {
                return false;
            }
        }

        return true;
    }

    private int Missing(ImmutableArray<PlanDemand> demands)
    {
        var missing = 0;
        for (var index = 0; index < demands.Length; index++)
        {
            missing += Math.Max(0, demands[index].RequiredQuantity - run.Tally.Acquired(demands[index].Key));
        }

        return missing;
    }

    private int MissingOfItem(uint itemId) => Missing(RunPlanning.DemandsOfItem(run.Authorization!, itemId));
}
