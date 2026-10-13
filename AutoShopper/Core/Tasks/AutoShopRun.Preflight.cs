using System.Collections.Immutable;
using System.Threading.Tasks;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Preview;
using AutoShopper.Core.Preview.Model;
using AutoShopper.Core.Pricing;
using AutoShopper.Core.Pricing.Model;
using AutoShopper.Core.Shopping.Model;
using ECommons.DalamudServices;

namespace AutoShopper.Core.Tasks;

internal sealed partial class AutoShopRun
{
    // A fetch of a long list over a whole region takes well under this; a cooldown adds at most 30 seconds.
    private const int PriceFetchBudgetMs = 180_000;
    // The preview plans within a quarter second of fresh prices, and the planner stops itself after two seconds.
    private const int PlanWaitMs = 30_000;

    private async Task<string?> PrepareFresh()
    {
        run.SetState(RunState.Preflight);
        Status = Loc.T(L.Buyer.StatusPreflight);
        var plugin = Owner;
        var readiness = ShopReadiness.Evaluate(plugin, forRun: true);
        if (readiness != Readiness.Ready)
        {
            return ShopReadiness.Reason(readiness);
        }

        var list = plugin.Lists.Selected!;
        if (plugin.Preview.Scope is not { } scope)
        {
            return Loc.T(L.Shop.ReasonLoggedOut);
        }

        run.SetState(RunState.FetchingPrices);
        Status = Loc.T(L.Buyer.StatusPrices);
        if (!await EnsureFreshPrices(scope, ItemIdsOf(plugin.Lists.Analyze(list))))
        {
            return Loc.T(L.Buyer.RefusePrices);
        }

        run.SetState(RunState.Planning);
        Status = Loc.T(L.Buyer.StatusPlanning);
        var preview = await WaitForPreview(list);
        if (preview is null || !ReferenceEquals(preview.Input.Prices, plugin.Prices.Latest))
        {
            return Loc.T(L.Buyer.RefusePlan);
        }

        if (preview.Input.Demands.TargetsUnknown > 0)
        {
            return Loc.T(L.Buyer.RefuseOwned);
        }

        var demands = RunPlanning.AuthorizedDemands(preview.Input.Demands.Demands);
        if (demands.IsEmpty)
        {
            return Loc.T(L.Buyer.RefuseNothing);
        }

        return Authorize(demands, preview.Selected, scope, preview.Input.Prices);
    }

    // A Resume, a fault restart or a run read back from the journal: the authorization stands, so only who and where
    // is checked again, after the purchase in flight has its answer. A run read back from the journal plans what is
    // left from fresh prices.
    private async Task<string?> PrepareAuthorized()
    {
        run.SetState(RunState.Preflight);
        Status = Loc.T(run.InFlight is null ? L.Buyer.StatusPreflight : L.Buyer.StatusSettling);
        await WaitForSettlement();
        var authorization = run.Authorization!;
        if (!Svc.ClientState.IsLoggedIn || Svc.Objects.LocalPlayer is null)
        {
            return Loc.T(L.Shop.ReasonLoggedOut);
        }

        if (Svc.PlayerState.ContentId != authorization.CharacterContentId)
        {
            return Loc.T(L.Buyer.RefuseCharacter);
        }

        var compatibility = Owner.Market.Compatibility;
        if (!compatibility.ReadingAvailable)
        {
            return Loc.T(L.Shop.ReasonReadingOff);
        }

        if (!run.DryRun && !compatibility.ExecutionAvailable)
        {
            return Loc.T(L.Shop.ReasonBuyingOff);
        }

        session.DidWork = true;
        NoteWorld();
        if (run.Plan is not null)
        {
            return null;
        }

        if (Owner.Preview.Scope is not { } scope)
        {
            return Loc.T(L.Shop.ReasonLoggedOut);
        }

        run.SetState(RunState.FetchingPrices);
        Status = Loc.T(L.Buyer.StatusPrices);
        if (!await EnsureFreshPrices(scope, ItemIdsOf(authorization)) || Owner.Prices.Latest is not { } prices)
        {
            return Loc.T(L.Buyer.RefusePrices);
        }

        run.SetState(RunState.Planning);
        Status = Loc.T(L.Buyer.StatusPlanning);
        var configuration = Owner.Configuration;
        var settings = PlanPreview.SettingsFor(configuration, scope);
        var runWorlds = RunPlanning.AuthorizedWorlds(authorization, scope.PlanWorlds);
        // A run whose most stops are used up plans no world, so its route is empty and it ends with what it bought.
        var worlds = RouteReplanning.TryStopsLeft(settings.MaxStops, run.CompletedWorldIds.Count, out var stopsLeft)
            ? RouteReplanning.EligibleWorlds(runWorlds, run.CompletedWorldIds, run.Exclusions, configuration.WorldRevisits, authorization)
            : ImmutableArray<PlanWorld>.Empty;
        var request = RunPlanning.RemainingRequest(authorization, run.Tally, prices.Offers, worlds, settings with { MaxStops = stopsLeft }, run.ExcludedListingKeys);
        var plan = await OffThread(token => new Planner().Plan(request, token), "planning what is left of the run");
        if (plan is null)
        {
            return Loc.T(L.Buyer.RefusePlan);
        }

        run.StartRoute(plan, runWorlds, prices, settings.MaxStops);
        RunLog.Info($"Run: resumed run {run.RunId}; {plan.Stops.Length} stop(s) planned for what is left.");
        Print(Loc.T(L.Buyer.ChatResumed));
        return null;
    }

    private string? Authorize(ImmutableArray<PlanDemand> demands, PurchasePlan plan, ResolvedScope scope, PriceSnapshot? prices)
    {
        var configuration = Owner.Configuration;
        var now = DateTime.UtcNow;
        var problem = RunAuthorization.TryCreate(
            run.RunId,
            Svc.PlayerState.ContentId,
            Svc.PlayerState.HomeWorld.RowId,
            demands,
            WorldIdsOf(scope.Allowed),
            configuration.MaxSpend,
            configuration.GilReserve,
            PolicyVersion,
            now,
            out var authorization);
        if (problem != AuthorizationProblem.None)
        {
            return problem switch
            {
                AuthorizationProblem.MissingSpendCap => Loc.T(L.Shop.ReasonNoCap),
                AuthorizationProblem.NoDemand => Loc.T(L.Buyer.RefuseNothing),
                AuthorizationProblem.MissingCharacter or AuthorizationProblem.MissingHomeWorld => Loc.T(L.Shop.ReasonLoggedOut),
                _ => Loc.T(L.Buyer.RefuseAuthorization, problem.ToString()),
            };
        }

        if (!run.DryRun)
        {
            var rotation = Owner.Ledger.Rotate(now);
            RunLog.Info($"Run: journal rotation archived {rotation.ArchivedRuns} finished run(s){(rotation.Succeeded ? string.Empty : $"; it failed: {rotation.Error}")}.");
        }

        if (!run.Authorize(authorization!, plan, prices, now))
        {
            RunLog.Warning($"Run: the authorization could not be written: {run.Journal.LastError}.");
            return Loc.T(L.Buyer.RefuseJournal);
        }

        if (!run.DryRun)
        {
            Owner.Ledger.Invalidate();
        }

        session.DidWork = true;
        NoteWorld();
        NoteUnplanned(plan);
        RunLog.Info($"Run: {(run.DryRun ? "dry run" : "run")} {run.RunId} authorized for '{run.ListName}': {authorization!.Demands.Length} line(s), cap {configuration.MaxSpend}, reserve {configuration.GilReserve}, {authorization.AllowedWorldIds.Length} world(s), {plan.Stops.Length} stop(s).");
        Print(run.DryRun
            ? Loc.T(L.Buyer.ChatStartedDry, run.ListName)
            : Loc.T(L.Buyer.ChatStarted, run.ListName, Loc.Gil(configuration.MaxSpend)));
        return null;
    }

    private void NoteUnplanned(PurchasePlan plan)
    {
        for (var index = 0; index < plan.Unresolved.Length; index++)
        {
            var unresolved = plan.Unresolved[index];
            run.AddIssue(RunIssue.ForUnresolved(unresolved.Reason), unresolved.Demand.ItemId, 0, unresolved.MissingQuantity);
        }
    }

    // Prices count as fresh when they cover the allowed worlds and every item, and are younger than the stale setting.
    private async Task<bool> EnsureFreshPrices(ResolvedScope scope, ImmutableArray<uint> itemIds)
    {
        var prices = Owner.Prices;
        if (PricesFresh(prices.Latest, scope, itemIds))
        {
            return true;
        }

        var deadline = Environment.TickCount64 + PriceFetchBudgetMs;
        while (prices.Fetching || prices.CooldownRemaining > TimeSpan.Zero)
        {
            if (Environment.TickCount64 >= deadline)
            {
                return false;
            }

            await NextFrame(SlowPollFrames);
        }

        var before = prices.Latest?.Generation ?? 0;
        var started = prices.TryStart(scope.Allowed, itemIds);
        if (started != PriceStartResult.Started)
        {
            RunLog.Info($"Run: the price fetch did not start ({started}).");
            return false;
        }

        while (prices.Fetching)
        {
            if (Environment.TickCount64 >= deadline)
            {
                return false;
            }

            await NextFrame(SlowPollFrames);
        }

        var latest = prices.Latest;
        if (latest is null || latest.Generation <= before || latest.FailedRequestCount >= latest.RequestCount)
        {
            RunLog.Info("Run: the price fetch brought nothing usable back.");
            return false;
        }

        return true;
    }

    private static bool PricesFresh(PriceSnapshot? snapshot, ResolvedScope scope, ImmutableArray<uint> itemIds)
    {
        if (snapshot is null || !snapshot.HasSameWorlds(scope.Allowed))
        {
            return false;
        }

        var staleAfter = TimeSpan.FromHours(Owner.Configuration.StaleAfterHours);
        if (PreviewReview.IsOlderThan(snapshot.FetchedAtUtc, DateTime.UtcNow, staleAfter))
        {
            return false;
        }

        for (var index = 0; index < itemIds.Length; index++)
        {
            if (!snapshot.TryGetItem(itemIds[index], out _))
            {
                return false;
            }
        }

        return true;
    }

    // The preview plans the selected list on its own; the run takes its plan once it was made from the newest prices.
    private async Task<PreviewResult?> WaitForPreview(ShoppingList list)
    {
        var preview = Owner.Preview;
        var deadline = Environment.TickCount64 + PlanWaitMs;
        while (true)
        {
            var result = preview.LatestFor(list);
            var current = result is not null && !preview.Planning && ReferenceEquals(result.Input.Prices, Owner.Prices.Latest);
            if ((current && result!.Input.Demands.TargetsUnknown == 0) || Environment.TickCount64 >= deadline)
            {
                return result;
            }

            await NextFrame(SlowPollFrames);
        }
    }

    private static ImmutableArray<uint> ItemIdsOf(ListAnalysis analysis) => [.. analysis.ItemIds];

    private static ImmutableArray<uint> ItemIdsOf(RunAuthorization authorization)
    {
        var itemIds = new List<uint>(authorization.Demands.Length);
        for (var index = 0; index < authorization.Demands.Length; index++)
        {
            var itemId = authorization.Demands[index].ItemId;
            if (!itemIds.Contains(itemId))
            {
                itemIds.Add(itemId);
            }
        }

        return [.. itemIds];
    }

    private static ImmutableArray<uint> WorldIdsOf(ImmutableArray<ScopeWorld> worlds)
    {
        var worldIds = ImmutableArray.CreateBuilder<uint>(worlds.Length);
        for (var index = 0; index < worlds.Length; index++)
        {
            worldIds.Add(worlds[index].WorldId);
        }

        return worldIds.MoveToImmutable();
    }
}
