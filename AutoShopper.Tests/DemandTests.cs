using AutoShopper.Core.Planning;
using static AutoShopper.Tests.Fixtures;

namespace AutoShopper.Tests;

public sealed class DemandTests
{
    private const uint Ore = 5_100;

    [Fact]
    public void FixedPurchaseIgnoresWhatIsAlreadyOwned()
    {
        var demand = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 10);

        Assert.Equal(10, demand.RequiredQuantity);
        Assert.True(demand.IsValid);
    }

    [Theory]
    [InlineData(20, 15, 5)]
    [InlineData(20, 25, 0)]
    [InlineData(20, -3, 20)]
    [InlineData(0, 0, 0)]
    public void InventoryTargetAsksOnlyForTheShortfall(int target, int owned, int expected)
    {
        var demand = PlanDemand.ForInventoryTarget(Ore, DemandQuality.Any, target, owned);

        Assert.Equal(expected, demand.RequiredQuantity);
    }

    [Fact]
    public void NegativeTargetIsInvalid()
    {
        var demand = PlanDemand.ForInventoryTarget(Ore, DemandQuality.Any, -1, 0);

        Assert.False(demand.IsValid);
    }

    [Theory]
    [InlineData(4, 6, 2)]
    [InlineData(11, 0, 1)]
    [InlineData(13, 0, 0)]
    [InlineData(0, 10, 2)]
    public void ReducedByNeverExpandsTheDemand(int acquired, int expectedRequired, int expectedExcess)
    {
        var demand = PlanDemand.ForPurchase(Ore, DemandQuality.HighQuality, 10, 2, 900);

        var reduced = demand.ReducedBy(acquired);

        Assert.Equal(expectedRequired, reduced.RequiredQuantity);
        Assert.Equal(expectedExcess, reduced.ExcessAllowance);
        Assert.Equal(demand.Key, reduced.Key);
        Assert.Equal(demand.UnitCapIncludingFee, reduced.UnitCapIncludingFee);
        Assert.True(reduced.MaximumTotal <= demand.MaximumTotal);
    }

    [Fact]
    public void PlannerBuysOnlyTheInventoryShortfall()
    {
        var demand = PlanDemand.ForInventoryTarget(Ore, DemandQuality.Any, 20, 15);
        var request = Request(Settings(), [demand], Offer(Alpha, Ore, 5, 100), Offer(Alpha, Ore, 20, 90));

        var plan = NewPlanner().Plan(request);

        Assert.True(plan.IsComplete);
        Assert.Equal(5, PlannedQuantity(plan, demand.Key));
    }

    [Fact]
    public void DuplicateLinesMergeWithoutDoubleCountingAndKeepTheStricterCap()
    {
        var first = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 3, 0, 0);
        var second = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 3, 0, 120);
        var request = Request(
            Settings(),
            [first, second],
            Offer(Alpha, Ore, 6, 100),
            Offer(Alpha, Ore, 6, 130),
            Offer(Beta, Ore, 6, 101));

        var plan = NewPlanner().Plan(request);

        Assert.True(plan.IsComplete);
        Assert.Equal(6, PlannedQuantity(plan, first.Key));
        Assert.All(Lines(plan), line => Assert.True(line.Payable <= 120L * line.Offer.Quantity));
    }

    [Fact]
    public void QualitiesStayDistinctLines()
    {
        var highQuality = PlanDemand.ForPurchase(Ore, DemandQuality.HighQuality, 2);
        var normalQuality = PlanDemand.ForPurchase(Ore, DemandQuality.NormalQuality, 2);
        var request = Request(
            Settings(),
            [highQuality, normalQuality],
            Offer(Alpha, Ore, 2, 50, isHq: true),
            Offer(Alpha, Ore, 2, 10));

        var plan = NewPlanner().Plan(request);

        Assert.True(plan.IsComplete);
        Assert.All(Lines(plan), line => Assert.Equal(line.Demand.Quality == DemandQuality.HighQuality, line.Offer.IsHq));
    }

    [Fact]
    public void InvalidDemandIsReportedNotDropped()
    {
        var invalid = new PlanDemand(new DemandKey(0, DemandQuality.Any), 4, 0, 0);
        var negative = PlanDemand.ForPurchase(Ore, DemandQuality.Any, -2);
        var request = Request(Settings(), [invalid, negative], Offer(Alpha, Ore, 5, 10));

        var plan = NewPlanner().Plan(request);

        Assert.False(plan.IsComplete);
        Assert.Equal(UnresolvedReason.InvalidDemand, Unresolved(plan, invalid.Key).Reason);
        Assert.Equal(4, Unresolved(plan, invalid.Key).MissingQuantity);
        Assert.Equal(UnresolvedReason.InvalidDemand, Unresolved(plan, negative.Key).Reason);
        Assert.Empty(plan.Stops);
    }
}
