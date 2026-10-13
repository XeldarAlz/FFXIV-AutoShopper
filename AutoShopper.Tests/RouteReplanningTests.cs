using System.Collections.Immutable;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Planning;
using static AutoShopper.Tests.Fixtures;

namespace AutoShopper.Tests;

public sealed class RouteReplanningTests
{
    private const uint Ore = 5_100;
    private const uint Cloth = 5_200;
    private const ulong Character = 1_001;

    private static readonly PlanDemand OreDemand = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 10);
    private static readonly PlanDemand ClothDemand = PlanDemand.ForPurchase(Cloth, DemandQuality.Any, 6);
    private static readonly ImmutableArray<PlanDemand> Demands = [OreDemand, ClothDemand];
    private static readonly ConfirmedPurchase OreBought = new(OreDemand.Key, "alpha-ore", 10, 1_000);

    [Fact]
    public void AReplanFillsTheStopsAheadBeforeAddingACheaperWorld()
    {
        var (runPlan, route) = StartedOn(Alpha);
        route.Complete(0);

        var plan = Replan(runPlan, route, completed: [Alpha.WorldId], offers: [BetaCloth(), Offer(Delta, Cloth, 6, 10, key: "delta-cloth")]);

        Assert.NotNull(plan);
        Assert.Equal([Beta.WorldId], StopWorldIds(plan!));
        Assert.True(plan!.IsComplete);
    }

    [Fact]
    public void AReplanAddsAWorldOnlyWhenTheStopsAheadFallShort()
    {
        var (runPlan, route) = StartedOn(Alpha);
        route.Complete(0);

        var plan = Replan(runPlan, route, completed: [Alpha.WorldId], offers: [BetaCloth(), Offer(Delta, Cloth, 6, 10, key: "delta-cloth")], excludedKeys: ["beta-cloth"]);

        Assert.NotNull(plan);
        Assert.Equal([Delta.WorldId], StopWorldIds(plan!));
    }

    [Fact]
    public void AShoppedWorldComesBackOnlyWhileRevisitsRemain()
    {
        var (runPlan, route) = StartedOn(Alpha);
        route.Complete(0);
        ImmutableArray<PlanOffer> offers = [BetaCloth(), Offer(Alpha, Cloth, 6, 10, key: "alpha-cloth"), Offer(Gamma, Cloth, 6, 80, key: "gamma-cloth")];

        var withoutRevisits = Replan(runPlan, route, completed: [Alpha.WorldId], offers: offers, excludedKeys: ["beta-cloth"]);
        var withRevisit = Replan(runPlan, route, completed: [Alpha.WorldId], offers: offers, excludedKeys: ["beta-cloth"], revisitsLeft: 1);

        Assert.Equal([Gamma.WorldId], StopWorldIds(withoutRevisits!));
        Assert.Equal([Alpha.WorldId], StopWorldIds(withRevisit!));
    }

    [Fact]
    public void ExcludedWorldsAndWorldsOutsideTheRunAreNeverPlanned()
    {
        var (runPlan, route) = StartedOn(Alpha);
        route.Complete(0);
        var exclusions = new TravelExclusions();
        exclusions.Record(Beta, TravelFailure.Congested, Alpha.DataCenterId);
        var outsider = new PlanWorld(99, PrimaryDataCenter, Region, "Outsider");
        ImmutableArray<PlanOffer> offers = [BetaCloth(), Offer(outsider, Cloth, 6, 1, key: "outsider-cloth"), Offer(Gamma, Cloth, 6, 90, key: "gamma-cloth")];

        var plan = Replan(runPlan, route, completed: [Alpha.WorldId], offers: offers, exclusions: exclusions);

        Assert.Equal([Gamma.WorldId], StopWorldIds(plan!));
        Assert.DoesNotContain(plan!.Request.Worlds, world => world.WorldId == Beta.WorldId || world.WorldId == outsider.WorldId);
    }

    [Fact]
    public void AReplanKeepsTheAuthorizedQuantitiesAndOnlyPlansWhatIsOutstanding()
    {
        var (runPlan, route) = StartedOn(Alpha);
        route.Complete(0);

        var plan = Replan(runPlan, route, completed: [Alpha.WorldId], offers: [BetaCloth(), Offer(Beta, Ore, 10, 1, key: "beta-ore")]);

        Assert.Equal(Demands.Length, plan!.AuthorizedDemands.Length);
        Assert.Equal(0, PlannedQuantity(plan, OreDemand.Key));
        Assert.Equal(6, PlannedQuantity(plan, ClothDemand.Key));
        PlanInvariants.Check(plan);
    }

    [Fact]
    public void NoEligibleWorldLeavesNothingToPlan()
    {
        var (runPlan, route) = StartedOn(Alpha);
        route.Complete(0);
        var exclusions = new TravelExclusions();
        exclusions.Record(Beta, TravelFailure.DataCenterTravelOff, SecondaryDataCenter);

        var plan = Replan(runPlan, route, completed: [Alpha.WorldId], offers: [BetaCloth()], exclusions: exclusions, runWorlds: [Alpha, Beta]);

        Assert.Null(plan);
    }

    [Fact]
    public void TheMostStopsSettingCountsTheStopsAlreadyMade()
    {
        Assert.True(RouteReplanning.TryStopsLeft(0, 5, out var unlimited));
        Assert.Equal(0, unlimited);
        Assert.True(RouteReplanning.TryStopsLeft(3, 1, out var two));
        Assert.Equal(2, two);
        Assert.False(RouteReplanning.TryStopsLeft(2, 2, out _));
    }

    [Fact]
    public void AReplanWithTheStopLimitUsedUpPlansNothing()
    {
        var (runPlan, route) = StartedOn(Alpha);
        route.Complete(0);

        var plan = Replan(runPlan, route, completed: [Alpha.WorldId], offers: [BetaCloth()], maxStops: 1, stopsMade: 1);

        Assert.Null(plan);
    }

    [Fact]
    public void TheRouteKeepsWhatIsBehindAndTakesTheNewOrderAhead()
    {
        var route = RunRoute.Start([Stop(Alpha), Stop(Beta), Stop(Gamma)]);
        route.Begin(0);
        route.Complete(0);

        route.ReplaceAhead([Stop(Gamma), Stop(Delta)]);

        Assert.Equal([Alpha.WorldId, Beta.WorldId, Gamma.WorldId, Delta.WorldId], WorldIds(route));
        Assert.Equal(RouteStatus.Done, route[0].Status);
        Assert.Equal(RouteStatus.Skipped, route[1].Status);
        Assert.Equal(RouteSkip.NotNeeded, route[1].Skip);
        Assert.Equal(2, route.NextIndex());
        Assert.Equal(3, route.FollowingIndex());
    }

    [Fact]
    public void AWorldALaterPlanBringsBackLosesItsNotNeededMark()
    {
        var route = RunRoute.Start([Stop(Alpha), Stop(Beta)]);
        route.Complete(0);
        route.ReplaceAhead([Stop(Gamma)]);

        route.ReplaceAhead([Stop(Beta)]);

        Assert.Equal([Alpha.WorldId, Gamma.WorldId, Beta.WorldId], WorldIds(route));
        Assert.Equal(RouteSkip.NotNeeded, route[1].Skip);
        Assert.Equal(RouteStatus.Ahead, route[2].Status);
        Assert.Equal(2, route.NextIndex());
    }

    [Fact]
    public void AFailedTripStaysCurrentUntilItIsSkipped()
    {
        var route = RunRoute.Start([Stop(Beta), Stop(Gamma)]);
        route.Begin(0);

        Assert.Equal(0, route.NextIndex());
        Assert.Equal(1, route.FollowingIndex());

        route.Skip(0, RouteSkip.TravelFailed, TravelFailure.Congested);

        Assert.Equal(1, route.NextIndex());
        Assert.Equal(TravelFailure.Congested, route[0].Failure);
    }

    [Fact]
    public void SkippingWhatIsAheadEndsTheRoute()
    {
        var route = RunRoute.Start([Stop(Alpha), Stop(Beta), Stop(Gamma)]);
        route.Complete(0);
        route.Begin(1);

        route.SkipAhead(RouteSkip.TravelHalted, TravelFailure.LifestreamBusy);

        Assert.Equal(-1, route.NextIndex());
        Assert.Equal(1, route.CountWithStatus(RouteStatus.Done));
        Assert.Equal(2, route.CountWithStatus(RouteStatus.Skipped));
    }

    private static (PurchasePlan RunPlan, RunRoute Route) StartedOn(PlanWorld current)
    {
        var request = Request(Settings(current: current), Demands, Offer(Alpha, Ore, 10, 100, key: "alpha-ore"), BetaCloth());
        var plan = NewPlanner().Plan(request);
        Assert.Equal([Alpha.WorldId, Beta.WorldId], StopWorldIds(plan));
        return (plan, RunRoute.Start(plan.Stops));
    }

    private static PurchasePlan? Replan(
        PurchasePlan runPlan,
        RunRoute route,
        uint[] completed,
        ImmutableArray<PlanOffer> offers,
        ImmutableArray<string> excludedKeys = default,
        TravelExclusions? exclusions = null,
        int revisitsLeft = 0,
        int maxStops = 0,
        int stopsMade = 1,
        ImmutableArray<PlanWorld> runWorlds = default)
    {
        var input = new RouteReplanInput
        {
            RunPlan = runPlan,
            Authorization = Authorization(),
            Acquisitions = [OreBought],
            ExcludedListingKeys = excludedKeys.IsDefault ? [] : excludedKeys,
            Offers = offers,
            RunWorlds = runWorlds.IsDefault ? AllWorlds : runWorlds,
            Route = route,
            CompletedWorldIds = completed,
            StopsMade = stopsMade,
            Exclusions = exclusions ?? new TravelExclusions(),
            CurrentWorld = Alpha,
            RevisitsLeft = revisitsLeft,
            MaxStops = maxStops,
        };
        return RouteReplanning.Replan(NewPlanner(), input);
    }

    private static RunAuthorization Authorization()
    {
        var worldIds = new uint[AllWorlds.Length];
        for (var index = 0; index < worldIds.Length; index++)
        {
            worldIds[index] = AllWorlds[index].WorldId;
        }

        var problem = RunAuthorization.TryCreate(Guid.NewGuid(), Character, Alpha.WorldId, Demands, [.. worldIds], 1_000_000, 0, 1, ObservedAt, out var authorization);
        Assert.Equal(AuthorizationProblem.None, problem);
        return authorization!;
    }

    private static PlanOffer BetaCloth() => Offer(Beta, Cloth, 6, 100, key: "beta-cloth");

    private static PlanStop Stop(PlanWorld world) => new(world, [], 0, 0, 0);

    private static uint[] WorldIds(RunRoute route)
    {
        var ids = new uint[route.Count];
        for (var index = 0; index < ids.Length; index++)
        {
            ids[index] = route[index].World.WorldId;
        }

        return ids;
    }
}
