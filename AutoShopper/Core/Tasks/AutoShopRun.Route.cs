using System.Collections.Immutable;
using System.Threading.Tasks;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Ipc;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Market.Model;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Preview.Model;
using AutoShopper.Core.Pricing;
using AutoShopper.Core.Pricing.Model;
using AutoShopper.Core.Travel;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;

namespace AutoShopper.Core.Tasks;

// Between stops: the trip to the next stop's world, what a failed trip leaves out, and the plan for what is left.
internal sealed partial class AutoShopRun
{
    // A world change settles well within this once the character stands on the new world.
    private const int ArrivalConfirmBudgetMs = 60_000;
    // Lifestream goes busy again briefly after it sets a character down, so arrival waits for this much quiet.
    private const int ArrivalSettleMs = 3_000;
    private const int ArrivalPollMs = 250;
    // Long enough for a busy aetheryte or a passing hitch to clear before the same trip is asked for again.
    private const int TravelRetryBackoffMs = 15_000;
    // A route refreshes stale estimates at most this often, on top of the price service's own cooldown.
    private const long PriceRefreshIntervalMs = 10 * TimeUnits.MillisecondsPerMinute;
    private const int PriceRefreshBudgetMs = 60_000;
    // Long enough for a window or a trade to be closed, or a duty queue left, before the run pauses for the player.
    private const int BlockerClearMs = 30_000;

    private TravelFailure lastTravelFailure;
    private string resumeReason = string.Empty;

    protected override object TravelOwner => run;

    // Armed right before the transfer request, so the logout it brings is known to be this run's.
    private protected override TransferContinuation? ContinuationFor(in JourneyPlan plan, in WorldInfo target)
    {
        if (run.Authorization is not { } authorization)
        {
            return null;
        }

        run.ArmTransfer();
        var stopIndex = run.Route?.NextIndex() ?? -1;
        RunLog.Info($"Run: run {run.RunId} armed its continuation for the data center transfer to {target.Name} (stop {stopIndex + 1}).");
        return new TransferContinuation(run.RunId, authorization.CharacterContentId, authorization.CreatedAtUtc, stopIndex, target.Id, target.Name, plan.StartedAtUtc, DateTime.UtcNow);
    }

    private async Task<StopReach> TravelToStop(PlanWorld world)
    {
        lastTravelFailure = TravelFailure.None;
        if (!Worlds.TryFindById(world.WorldId, out var target) || !Worlds.TryCurrent(out var current))
        {
            lastTravelFailure = TravelFailure.Refused;
            return StopReach.TravelFailed;
        }

        var crossDataCenter = !Worlds.SameDataCenter(current, target);
        run.SetState(RunState.Traveling);
        run.SetTravel(crossDataCenter ? TravelPhase.TransferringDataCenter : TravelPhase.ChangingWorld, target.Name);
        await CloseMarketWindows(Scope);
        if (!await WaitOutBlocker(target.Name, partyBlocks: false))
        {
            return StopReach.Held;
        }

        var outcome = await TravelToWorld(JourneyPlan.ToWorldThrough(target.Name, GatewayFor()));
        if (CancelToken.IsCancellationRequested)
        {
            return StopReach.Reached;
        }

        if (!AfterTransferLogin(target))
        {
            return StopReach.LeftForResume;
        }

        if (Owner.Controller.StopDeferred)
        {
            session.CrossedDataCenter |= crossDataCenter && outcome == JourneyOutcome.Arrived;
            Owner.Controller.ApplyDeferredStop();
            return StopReach.Reached;
        }

        // A pause asked for while the transfer was under way waited for it, and takes effect now; a Resume travels on
        // from wherever the transfer left the character.
        if (Owner.Controller.ApplyDeferredPause())
        {
            session.CrossedDataCenter |= crossDataCenter && outcome == JourneyOutcome.Arrived;
            return StopReach.Reached;
        }

        if (outcome == JourneyOutcome.Blocked)
        {
            lastTravelFailure = TravelFailure.Blocked;
            return await WaitOutBlocker(target.Name, partyBlocks: true) ? StopReach.TravelFailed : StopReach.Held;
        }

        if (outcome != JourneyOutcome.Arrived)
        {
            lastTravelFailure = FailureOf(outcome, crossDataCenter);
            RunLog.Info($"Run: the trip to {target.Name} ended {outcome} ({lastTravelFailure}).");
            return StopReach.TravelFailed;
        }

        if (!await ConfirmArrival(target))
        {
            lastTravelFailure = TravelFailure.ArrivalUnconfirmed;
            return CancelToken.IsCancellationRequested ? StopReach.Reached : StopReach.TravelFailed;
        }

        run.RecordArrival();
        session.CrossedDataCenter |= crossDataCenter;
        NoteWorld();
        RunLog.Info($"Run: arrived on {target.Name} ({target.DataCenterName}).");
        return StopReach.Reached;
    }

    // A party, a duty queue, a trade or a window in the way holds up every trip alike, so it never counts against the
    // world: it gets BlockerClearMs to clear, then the run pauses for the player. False once the run paused or stopped.
    private async Task<bool> WaitOutBlocker(string world, bool partyBlocks)
    {
        if (TransferGuard.Blocker(partyBlocks) is not { } blocker)
        {
            return true;
        }

        Warn($"Run: the trip to {world} waits up to {BlockerClearMs / TimeUnits.MillisecondsPerSecond}s: {blocker}");
        var phase = run.TravelPhase;
        var target = run.TravelTarget;
        run.SetTravel(TravelPhase.WaitingToRetry, world);
        var cleared = await WaitUntilTimed(() => TransferGuard.Blocker(partyBlocks) is null, BlockerClearMs, "trip-blocker");
        run.SetTravel(phase, target);
        if (cleared || CancelToken.IsCancellationRequested)
        {
            return cleared;
        }

        Warn($"Run: the trip to {world} is still held up ({TransferGuard.Blocker(partyBlocks) ?? "cleared"}); the run pauses for the player");
        PrintError(Loc.T(L.Route.ChatHeldPaused, world));
        Owner.Controller.Pause(PauseReason.Manual);
        return false;
    }

    // False leaves the run for the player's Resume: the character is logged out, or the login after the logout was not
    // the one that completes the transfer this run planned.
    private bool AfterTransferLogin(in WorldInfo target)
    {
        var loggedIn = Svc.ClientState.IsLoggedIn;
        var transferred = run.TransferArmed && run.TransferLoggedOut;
        var verdict = transferred && loggedIn ? EvaluateContinuation() : ContinuationVerdict.Continue;
        run.EndTransfer();
        Owner.Configuration.ClearPendingContinuation();
        if (!loggedIn)
        {
            resumeReason = Loc.T(L.Route.ReasonLoggedOut);
            return false;
        }

        if (!transferred)
        {
            return true;
        }

        RunLog.Info($"Run: the login after the transfer to {target.Name} reads {verdict}.");
        if (verdict == ContinuationVerdict.Continue)
        {
            Print(Loc.T(L.Route.ChatTransferContinue, target.Name));
            return true;
        }

        resumeReason = ReasonFor(verdict, target.Name);
        return false;
    }

    private ContinuationVerdict EvaluateContinuation()
    {
        var pending = run.PendingAtLogout;
        var facts = new ContinuationFacts
        {
            Continuation = Owner.Configuration.PendingContinuation,
            RunId = run.RunId,
            AuthorizedAtUtc = run.Authorization!.CreatedAtUtc,
            LoginSeen = run.TransferLoggedOut && Svc.ClientState.IsLoggedIn,
            PendingJourneyWorld = pending?.World,
            PendingJourneyStartedAtUtc = pending?.StartedAtUtc ?? default,
            ContentId = Svc.PlayerState.ContentId,
            CurrentWorldId = Svc.PlayerState.CurrentWorld.RowId,
            PurchaseUnresolved = run.InFlight is not null || run.HasUnknownOutcome,
            NowUtc = DateTime.UtcNow,
        };
        return ContinuationDecision.Evaluate(facts);
    }

    private static string ReasonFor(ContinuationVerdict verdict, string world) => verdict switch
    {
        ContinuationVerdict.OtherCharacter => Loc.T(L.Route.ReasonOtherCharacter),
        ContinuationVerdict.NotArrived => Loc.T(L.Route.ReasonNotArrived, world),
        ContinuationVerdict.PurchaseUnresolved => Loc.T(L.Route.ReasonUnresolved),
        ContinuationVerdict.Expired => Loc.T(L.Route.ReasonExpired),
        _ => Loc.T(L.Route.ReasonNotOurs),
    };

    // The run keeps its journal for the Resume card.
    private RunEnd? LeaveForResume()
    {
        var reason = resumeReason.Length > 0 ? resumeReason : Loc.T(L.Route.ReasonLoggedOut);
        RunLog.Info($"Run: run {run.RunId} stops where it stands and waits for the player: {reason}.");
        run.ClearTravel();
        run.Checkpoint(DateTime.UtcNow);
        session.Interrupted = true;
        Owner.Ledger.Invalidate();
        PrintError(Loc.T(L.Route.ChatLeftForResume, reason));
        return null;
    }

    private async Task<bool> ConfirmArrival(WorldInfo target)
    {
        run.SetTravel(TravelPhase.Settling, target.Name);
        var lifestream = LifestreamIPC.Instance;
        var watch = new ArrivalWatch();
        var deadline = Environment.TickCount64 + ArrivalConfirmBudgetMs;
        while (!CancelToken.IsCancellationRequested)
        {
            var now = Environment.TickCount64;
            var onTarget = Svc.Objects.LocalPlayer is not null && Svc.PlayerState.CurrentWorld.RowId == target.Id;
            var betweenAreas = Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51];
            if (watch.Observe(now, onTarget, betweenAreas, lifestream.IsBusy(), ArrivalSettleMs))
            {
                return true;
            }

            if (now >= deadline)
            {
                Warn($"Run: the arrival on {target.Name} did not settle within {ArrivalConfirmBudgetMs / TimeUnits.MillisecondsPerSecond}s (on the world {onTarget}, Lifestream busy {lifestream.IsBusy()}, {ConditionTag()})");
                return false;
            }

            await DelayMs(ArrivalPollMs);
        }

        return false;
    }

    private async Task AfterTravelFailure(int stopIndex, PlanWorld world)
    {
        var failure = lastTravelFailure == TravelFailure.None ? TravelFailure.Abandoned : lastTravelFailure;
        if (failure == TravelFailure.Blocked)
        {
            RunLog.Info($"Run: the trip to {world.Name} was held up and is cleared now; trying it again.");
            return;
        }

        var currentDataCenterId = Worlds.TryCurrent(out var current) ? current.DataCenterId : 0;
        var step = run.RecordTravelFailure(world, failure, currentDataCenterId);
        run.AddIssue(RunIssueReason.TravelFailed, 0, world.WorldId, 0);
        RunLog.Info($"Run: the trip to {world.Name} failed ({failure}, {run.Exclusions.Failures(world.WorldId)} time(s)); {step}.");
        switch (step)
        {
            case ExclusionStep.Retry:
                run.SetTravel(TravelPhase.WaitingToRetry, world.Name);
                await DelayMs(TravelRetryBackoffMs);
                run.ClearTravel();
                return;
            case ExclusionStep.TravelHalted:
                run.AddIssue(RunIssueReason.DependencyBusy, 0, 0, 0);
                run.SkipAhead(RouteSkip.TravelHalted, failure);
                break;
            default:
                run.SkipStop(stopIndex, RouteSkip.TravelFailed, failure);
                break;
        }

        run.ClearTravel();
        run.Checkpoint(DateTime.UtcNow);
        await ReplanRemaining();
    }

    // Plans what is left from the world the character stands on. Nothing left to buy, or no stop left in the most stops
    // setting, ends the route; a replan that fails keeps the route as it was.
    private async Task ReplanRemaining()
    {
        if (run.Authorization is not { } authorization || run.Plan is not { } plan || run.Route is not { } route)
        {
            return;
        }

        if (run.Tally.AllSatisfied)
        {
            run.SkipAhead(RouteSkip.NotNeeded, TravelFailure.None);
            return;
        }

        if (!RouteReplanning.TryStopsLeft(run.MaxStops, StopsMade(), out _))
        {
            RunLog.Info("Run: the most stops setting is used up; no further stop is planned.");
            run.SkipAhead(RouteSkip.StopLimit, TravelFailure.None);
            return;
        }

        if (!TryCurrentPlanWorld(out var currentWorld))
        {
            return;
        }

        var revisitsLeft = Math.Max(0, (authorization.WorldRevisits ?? Owner.Configuration.WorldRevisits) - run.Revisits);
        var eligible = RouteReplanning.EligibleWorlds(run.RunWorlds, run.CompletedWorldIds, run.Exclusions, revisitsLeft, authorization);
        if (run.Exclusions.TravelHalted)
        {
            eligible = OnlyWorld(eligible, currentWorld.WorldId);
        }

        await RefreshPricesIfStale(eligible, OutstandingItemIds(authorization));
        var input = new RouteReplanInput
        {
            RunPlan = plan,
            Authorization = authorization,
            Acquisitions = run.Tally.Acquisitions(),
            ExcludedListingKeys = run.ExcludedListingKeys,
            Offers = run.Prices?.Offers ?? ImmutableArray<PlanOffer>.Empty,
            RunWorlds = eligible,
            Route = route,
            CompletedWorldIds = run.CompletedWorldIds,
            StopsMade = StopsMade(),
            Exclusions = run.Exclusions,
            CurrentWorld = currentWorld,
            RevisitsLeft = revisitsLeft,
            MaxStops = run.MaxStops,
        };
        if (RouteReplanning.Prepare(input) is not { } prepared)
        {
            RunLog.Info("Run: no world is left to plan what remains on.");
            run.SkipAhead(RouteSkip.NotNeeded, TravelFailure.None);
            return;
        }

        var next = await OffThread(token => RouteReplanning.Plan(new Planner(), prepared, token), "planning what is left of the route");
        if (CancelToken.IsCancellationRequested || next is null)
        {
            return;
        }

        run.Replanned(next);
        RunLog.Info($"Run: replanned from {currentWorld.Name}: {next.Stops.Length} stop(s) ahead, {next.Totals.MissingQuantity} unit(s) not covered (plan {next.Version}).");
    }

    // Prices are fetched again only for the worlds still in play, only when the run's own are older than the stale
    // setting, and at most once per PriceRefreshIntervalMs; otherwise the run keeps the estimates it has. The fetch
    // is the run's own and merges into its copy; the snapshot the windows show is left as it was.
    private async Task RefreshPricesIfStale(ImmutableArray<PlanWorld> worlds, ImmutableArray<uint> itemIds)
    {
        if (worlds.IsEmpty || itemIds.IsEmpty)
        {
            return;
        }

        var staleAfter = TimeSpan.FromHours(Owner.Configuration.StaleAfterHours);
        if (run.Prices is { } snapshot && !PreviewReview.IsOlderThan(snapshot.FetchedAtUtc, DateTime.UtcNow, staleAfter))
        {
            return;
        }

        var prices = Owner.Prices;
        var now = Environment.TickCount64;
        if ((run.LastPriceRefreshAtMs != 0 && now - run.LastPriceRefreshAtMs < PriceRefreshIntervalMs) || prices.Fetching || prices.CooldownRemaining > TimeSpan.Zero)
        {
            return;
        }

        run.LastPriceRefreshAtMs = now;
        var started = prices.TryStartDetached(ScopeWorldsOf(worlds), itemIds, CancelToken, out var fetch);
        if (started != PriceStartResult.Started)
        {
            RunLog.Info($"Run: the stale prices were not refreshed ({started}).");
            return;
        }

        Status = Loc.T(L.Buyer.StatusPrices);
        var deadline = now + PriceRefreshBudgetMs;
        while (!fetch.IsCompleted && Environment.TickCount64 < deadline && !CancelToken.IsCancellationRequested)
        {
            await NextFrame(SlowPollFrames);
        }

        if (!fetch.IsCompletedSuccessfully || fetch.Result is not { } refreshed || refreshed.FailedRequestCount >= refreshed.RequestCount)
        {
            RunLog.Info("Run: the price refresh brought nothing usable back; the run keeps its older estimates.");
            return;
        }

        run.UsePrices(refreshed);
        RunLog.Info($"Run: refreshed the estimates for {worlds.Length} world(s) still in play.");
    }

    private int StopsMade() => run.CompletedWorldIds.Count + run.Revisits;

    private ImmutableArray<uint> OutstandingItemIds(RunAuthorization authorization)
    {
        var itemIds = new List<uint>(authorization.Demands.Length);
        for (var index = 0; index < authorization.Demands.Length; index++)
        {
            var demand = authorization.Demands[index];
            if (!run.Tally.IsSatisfied(demand.Key) && !itemIds.Contains(demand.ItemId))
            {
                itemIds.Add(demand.ItemId);
            }
        }

        return [.. itemIds];
    }

    private static bool TryCurrentPlanWorld(out PlanWorld world)
    {
        if (!Worlds.TryCurrent(out var current))
        {
            world = default;
            return false;
        }

        world = ScopeWorlds.From(current).ToPlanWorld();
        return true;
    }

    private static ImmutableArray<PlanWorld> OnlyWorld(ImmutableArray<PlanWorld> worlds, uint worldId)
    {
        for (var index = 0; index < worlds.Length; index++)
        {
            if (worlds[index].WorldId == worldId)
            {
                return [worlds[index]];
            }
        }

        return ImmutableArray<PlanWorld>.Empty;
    }

    private static ImmutableArray<ScopeWorld> ScopeWorldsOf(ImmutableArray<PlanWorld> worlds)
    {
        var scopeWorlds = ImmutableArray.CreateBuilder<ScopeWorld>(worlds.Length);
        for (var index = 0; index < worlds.Length; index++)
        {
            if (Worlds.TryFindById(worlds[index].WorldId, out var world))
            {
                scopeWorlds.Add(ScopeWorlds.From(world));
            }
        }

        return scopeWorlds.ToImmutable();
    }

    // The preferred city's own aetheryte when Lifestream changes worlds at it and the character has it attuned.
    private static int GatewayFor()
    {
        var city = Owner.Configuration.PreferredMarketCity;
        var gateway = MarketGateway.For(city);
        return gateway != MarketGateway.None && ZoneAetherytes.IsAttuned(MarketBoardCatalog.Of(city).TeleportAetheryteId) ? gateway : MarketGateway.None;
    }

    private static TravelFailure FailureOf(JourneyOutcome outcome, bool crossDataCenter) => outcome switch
    {
        JourneyOutcome.Refused => TravelFailure.Refused,
        JourneyOutcome.LifestreamBusy => TravelFailure.LifestreamBusy,
        JourneyOutcome.NotStarted => crossDataCenter ? TravelFailure.DataCenterTravelOff : TravelFailure.Abandoned,
        JourneyOutcome.Congested => TravelFailure.Congested,
        JourneyOutcome.TimedOut or JourneyOutcome.Overdue => TravelFailure.TimedOut,
        JourneyOutcome.Blocked => TravelFailure.Blocked,
        _ => TravelFailure.Abandoned,
    };
}
