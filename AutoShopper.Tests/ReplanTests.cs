using System.Collections.Immutable;
using AutoShopper.Core.Planning;
using static AutoShopper.Tests.Fixtures;

namespace AutoShopper.Tests;

public sealed class ReplanTests
{
    private const uint Ore = 5_100;
    private const uint Cloth = 5_200;

    private static readonly PlanDemand OreDemand = PlanDemand.ForPurchase(Ore, DemandQuality.HighQuality, 10, 2, 150);
    private static readonly PlanDemand ClothDemand = PlanDemand.ForPurchase(Cloth, DemandQuality.Any, 6);

    [Fact]
    public void ReplanCoversOnlyOutstandingDemand()
    {
        var planner = NewPlanner();
        var first = planner.Plan(InitialRequest());
        var confirmed = ImmutableArray.Create(new ConfirmedPurchase(OreDemand.Key, "bought-1", 4, 420));

        var replanned = planner.Replan(first, confirmed, [], Alpha, FreshOffers());

        Assert.Equal(first.Version + 1, replanned.Version);
        Assert.InRange(PlannedQuantity(replanned, OreDemand.Key), 6, 8);
        Assert.Equal(6, PlannedQuantity(replanned, ClothDemand.Key));
        Assert.True(replanned.IsComplete);
        PlanInvariants.Check(replanned);
    }

    [Fact]
    public void ReplanNeverExpandsQuantitiesCapsQualitiesOrScope()
    {
        var planner = NewPlanner();
        var first = planner.Plan(InitialRequest());
        var confirmed = ImmutableArray.Create(new ConfirmedPurchase(OreDemand.Key, "bought-1", 4, 420));
        var tempting = FreshOffers()
            .Add(Offer(Alpha, Ore, 6, 1, isHq: false, key: "nq-bargain"))
            .Add(Offer(Alpha, Ore, 6, 200, isHq: true, key: "over-cap"))
            .Add(new PlanOffer(99, 99, Ore, true, 6, 1, 0, "outside-scope", ObservedAt, false));

        var replanned = planner.Replan(first, confirmed, [], Alpha, tempting);

        Assert.Equal(first.Request.Worlds, replanned.Request.Worlds);
        Assert.Equal(first.AuthorizedDemands, replanned.AuthorizedDemands);
        for (var index = 0; index < replanned.Request.Demands.Length; index++)
        {
            var outstanding = replanned.Request.Demands[index];
            var original = Original(first, outstanding.Key);
            Assert.True(outstanding.RequiredQuantity <= original.RequiredQuantity);
            Assert.True(outstanding.MaximumTotal <= original.MaximumTotal);
            Assert.Equal(original.UnitCapIncludingFee, outstanding.UnitCapIncludingFee);
        }

        foreach (var line in Lines(replanned))
        {
            Assert.NotEqual("nq-bargain", line.Offer.ListingKey);
            Assert.NotEqual("over-cap", line.Offer.ListingKey);
            Assert.NotEqual("outside-scope", line.Offer.ListingKey);
        }

        PlanInvariants.Check(replanned);
    }

    [Fact]
    public void ExcludedListingsAreNotResurrectedByAStaleSnapshot()
    {
        var planner = NewPlanner();
        var first = planner.Plan(InitialRequest());
        var excludedKey = CheapestOreKey(first);

        var second = planner.Replan(first, [], [excludedKey], Alpha, FreshOffers());
        var third = planner.Replan(second, [], [], Alpha, FreshOffers());

        Assert.Contains(excludedKey, second.Request.ExcludedListingKeys);
        Assert.Contains(excludedKey, third.Request.ExcludedListingKeys);
        foreach (var line in Lines(third))
        {
            Assert.NotEqual(excludedKey, line.Offer.ListingKey);
        }
    }

    [Fact]
    public void ConfirmedListingsAreExcludedAndCountedOnce()
    {
        var planner = NewPlanner();
        var first = planner.Plan(InitialRequest());
        var boughtKey = CheapestOreKey(first);
        var confirmed = ImmutableArray.Create(
            new ConfirmedPurchase(OreDemand.Key, boughtKey, 5, 525),
            new ConfirmedPurchase(OreDemand.Key, boughtKey, 5, 525));

        var second = planner.Replan(first, confirmed, [], Alpha, FreshOffers());
        var third = planner.Replan(second, confirmed, [], Alpha, FreshOffers());

        Assert.Contains(boughtKey, second.Request.ExcludedListingKeys);
        Assert.Equal(5, Outstanding(second, OreDemand.Key).RequiredQuantity);
        Assert.Equal(5, Outstanding(third, OreDemand.Key).RequiredQuantity);
        Assert.Equal(PlannedQuantity(second, OreDemand.Key), PlannedQuantity(third, OreDemand.Key));
    }

    [Fact]
    public void CompletedDemandDropsOutIncludingExcessBought()
    {
        var planner = NewPlanner();
        var first = planner.Plan(InitialRequest());
        var confirmed = ImmutableArray.Create(
            new ConfirmedPurchase(OreDemand.Key, "a", 11, 1_100),
            new ConfirmedPurchase(ClothDemand.Key, "b", 6, 600));

        var replanned = planner.Replan(first, confirmed, [], Delta, FreshOffers());

        Assert.Empty(replanned.Request.Demands);
        Assert.Empty(replanned.Stops);
        Assert.True(replanned.IsComplete);
    }

    [Fact]
    public void ReplanStartsFromTheCurrentWorld()
    {
        var planner = NewPlanner();
        var first = planner.Plan(InitialRequest());

        var replanned = planner.Replan(first, [], [], Delta, FreshOffers());

        Assert.Equal(Delta, replanned.Request.Settings.CurrentWorld);
        Assert.Equal(first.Request.Settings.HomeWorld, replanned.Request.Settings.HomeWorld);
        if (Array.IndexOf(StopWorldIds(replanned), Delta.WorldId) >= 0)
        {
            Assert.Equal(Delta.WorldId, replanned.Stops[0].World.WorldId);
        }
    }

    [Fact]
    public void ObservedListingsFeedReplanningWithLiveFees()
    {
        var planner = NewPlanner();
        var first = planner.Plan(InitialRequest());
        var live = new AutoShopper.Core.Execution.Model.ObservedListing(
            900, 1, 2, Ore, true, 6, 80, 7, false, false, Delta.WorldId, 1, ObservedAt);

        var replanned = planner.Replan(first, [], [], Delta, [live.ToPlanOffer(Delta.DataCenterId)]);

        var line = Assert.Single(Lines(replanned), candidate => candidate.Demand == OreDemand.Key);
        Assert.Equal(6 * 80 + 7, line.Payable);
        Assert.False(line.Offer.IsEstimate);
        Assert.Equal(live.ToListingKey(), line.Offer.ListingKey);
    }

    private static PlanRequest InitialRequest()
    {
        return Request(Settings(PlanMode.Balanced, Alpha) with { StopPenaltyGil = 50 }, [OreDemand, ClothDemand], [.. FreshOffers()]);
    }

    private static ImmutableArray<PlanOffer> FreshOffers()
    {
        return
        [
            Offer(Alpha, Ore, 5, 100, isHq: true, key: "ore-a5"),
            Offer(Alpha, Ore, 3, 110, isHq: true, key: "ore-a3"),
            Offer(Beta, Ore, 5, 104, isHq: true, key: "ore-b5"),
            Offer(Delta, Ore, 4, 99, isHq: true, key: "ore-d4"),
            Offer(Delta, Ore, 2, 90, isHq: true, key: "ore-d2"),
            Offer(Alpha, Cloth, 6, 100, key: "cloth-a6"),
            Offer(Delta, Cloth, 6, 95, key: "cloth-d6"),
        ];
    }

    private static string CheapestOreKey(PurchasePlan plan)
    {
        foreach (var line in Lines(plan))
        {
            if (line.Demand == OreDemand.Key)
            {
                return line.Offer.ListingKey;
            }
        }

        throw new Xunit.Sdk.XunitException("no ore line in the plan");
    }

    private static PlanDemand Original(PurchasePlan plan, DemandKey key)
    {
        for (var index = 0; index < plan.AuthorizedDemands.Length; index++)
        {
            if (plan.AuthorizedDemands[index].Key == key)
            {
                return plan.AuthorizedDemands[index];
            }
        }

        throw new Xunit.Sdk.XunitException($"{key} was not authorized");
    }

    private static PlanDemand Outstanding(PurchasePlan plan, DemandKey key)
    {
        for (var index = 0; index < plan.Request.Demands.Length; index++)
        {
            if (plan.Request.Demands[index].Key == key)
            {
                return plan.Request.Demands[index];
            }
        }

        throw new Xunit.Sdk.XunitException($"{key} is not outstanding");
    }
}
