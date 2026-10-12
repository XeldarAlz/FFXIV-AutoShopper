using System.Collections.Immutable;
using System.Threading;
using AutoShopper.Core.Planning;
using static AutoShopper.Tests.Fixtures;

namespace AutoShopper.Tests;

public sealed class PlannerTests
{
    private const uint Ore = 5_100;
    private const uint Cloth = 5_200;
    private const uint Resin = 5_300;

    private static readonly PlanDemand OreDemand = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 10);
    private static readonly PlanDemand ClothDemand = PlanDemand.ForPurchase(Cloth, DemandQuality.Any, 10);

    [Fact]
    public void CheapestModeSplitsAcrossTheCheapestWorlds()
    {
        var plan = NewPlanner().Plan(ModeFixture(PlanMode.Cheapest));

        Assert.Equal([Beta.WorldId, Delta.WorldId], StopWorldIds(plan));
        Assert.Equal(2_100, plan.Totals.PurchaseEstimate);
        Assert.Equal(1, plan.Totals.DataCenterChanges);
        Assert.True(plan.IsProvenOptimal);
    }

    [Fact]
    public void BalancedModeTradesSomeGilForLessTravel()
    {
        var plan = NewPlanner().Plan(ModeFixture(PlanMode.Balanced));

        Assert.Equal([Alpha.WorldId, Beta.WorldId], StopWorldIds(plan));
        Assert.Equal(2_310, plan.Totals.PurchaseEstimate);
        Assert.Equal(0, plan.Totals.DataCenterChanges);
    }

    [Fact]
    public void FewestStopsModeConsolidatesWithinThePremium()
    {
        var plan = NewPlanner().Plan(ModeFixture(PlanMode.FewestStops));

        Assert.Equal([Beta.WorldId], StopWorldIds(plan));
        Assert.Equal(2_362, plan.Totals.PurchaseEstimate);
        Assert.Equal(2_100, plan.CheapestBaselineEstimate);
        Assert.False(plan.BaselineIsPartial);
    }

    [Fact]
    public void FewestStopsWithoutPremiumFallsBackToTheCheapestBasket()
    {
        var request = ModeFixture(PlanMode.FewestStops);
        var plan = NewPlanner().Plan(request with { Settings = request.Settings with { FewestStopsPremiumPercent = 0 } });

        Assert.Equal([Beta.WorldId, Delta.WorldId], StopWorldIds(plan));
        Assert.Equal(2_100, plan.Totals.PurchaseEstimate);
    }

    [Fact]
    public void FewestStopsLabelsAPartialBaseline()
    {
        var resin = PlanDemand.ForPurchase(Resin, DemandQuality.Any, 3);
        var request = ModeFixture(PlanMode.FewestStops);
        var plan = NewPlanner().Plan(request with { Demands = request.Demands.Add(resin) });

        Assert.True(plan.BaselineIsPartial);
        Assert.False(plan.IsComplete);
        Assert.Equal(UnresolvedReason.NoListings, Unresolved(plan, resin.Key).Reason);
    }

    [Fact]
    public void MaxStopsLeavesDemandUnresolvedWithAStopLimitReason()
    {
        var request = Request(
            Settings() with { MaxStops = 1 },
            [OreDemand, ClothDemand],
            Offer(Beta, Ore, 10, 100),
            Offer(Delta, Cloth, 10, 100));

        var plan = NewPlanner().Plan(request);

        Assert.Single(plan.Stops);
        Assert.Single(plan.Unresolved);
        Assert.Equal(UnresolvedReason.StopLimit, plan.Unresolved[0].Reason);
        Assert.Equal(10, plan.Unresolved[0].MissingQuantity);
        PlanInvariants.Check(plan);
    }

    [Fact]
    public void OverflowingOfferIsRejectedNotWrapped()
    {
        var request = Request(Settings(), [OreDemand], Overflowing(Alpha, Ore));

        var plan = NewPlanner().Plan(request);

        Assert.True(plan.Limits.HasFlag(PlanSearchLimits.ArithmeticOverflow));
        Assert.Empty(plan.Stops);
        Assert.Equal(UnresolvedReason.NoListings, Unresolved(plan, OreDemand.Key).Reason);
    }

    [Fact]
    public void OfferAboveTheUnitCapIsNeverUsed()
    {
        var capped = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 5, unitCapIncludingFee: 100);
        var request = Request(Settings(), [capped], Offer(Alpha, Ore, 5, 96), Offer(Beta, Ore, 2, 90));

        var plan = NewPlanner().Plan(request);

        Assert.Equal(2, PlannedQuantity(plan, capped.Key));
        var unresolved = Unresolved(plan, capped.Key);
        Assert.Equal(UnresolvedReason.AboveUnitCap, unresolved.Reason);
        Assert.Equal(2, unresolved.CoveredQuantity);
        Assert.Equal(3, unresolved.MissingQuantity);
    }

    [Fact]
    public void ReasonsSeparateShortStockFromStacksThatDoNotFit()
    {
        var short1 = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 10);
        var tooLarge = PlanDemand.ForPurchase(Cloth, DemandQuality.Any, 5);
        var request = Request(
            Settings(),
            [short1, tooLarge],
            Offer(Alpha, Ore, 3, 10),
            Offer(Alpha, Cloth, 4, 10),
            Offer(Alpha, Cloth, 4, 11));

        var plan = NewPlanner().Plan(request);

        Assert.Equal(UnresolvedReason.InsufficientStock, Unresolved(plan, short1.Key).Reason);
        var cloth = Unresolved(plan, tooLarge.Key);
        Assert.Equal(UnresolvedReason.NoFittingStacks, cloth.Reason);
        Assert.Equal(4, cloth.CoveredQuantity);
        Assert.Equal(1, cloth.MissingQuantity);
    }

    [Fact]
    public void AnyLineReportsStockTakenByStricterDemand()
    {
        var strict = PlanDemand.ForPurchase(Ore, DemandQuality.HighQuality, 2);
        var relaxed = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 2);
        var request = Request(Settings(), [relaxed, strict], Offer(Alpha, Ore, 2, 10, isHq: true), Offer(Alpha, Ore, 1, 12, isHq: true));

        var plan = NewPlanner().Plan(request);

        Assert.Equal(2, PlannedQuantity(plan, strict.Key));
        var unresolved = Unresolved(plan, relaxed.Key);
        Assert.Equal(UnresolvedReason.TakenByStricterDemand, unresolved.Reason);
        Assert.Equal(1, unresolved.MissingQuantity);
    }

    [Fact]
    public void ExcludedAndDuplicateListingsAreUsedAtMostOnce()
    {
        var demand = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 4, excessAllowance: 4);
        var request = Request(
            Settings(),
            [demand],
            Offer(Alpha, Ore, 4, 10, key: "shared"),
            Offer(Beta, Ore, 4, 10, key: "shared"),
            Offer(Alpha, Ore, 4, 5, key: "excluded"),
            Offer(Alpha, Ore, 4, 50, key: "fallback")) with
        {
            ExcludedListingKeys = ["excluded"],
        };

        var plan = NewPlanner().Plan(request);

        var keys = new List<string>();
        foreach (var line in Lines(plan))
        {
            keys.Add(line.Offer.ListingKey);
        }

        Assert.Equal(["shared"], keys);
        PlanInvariants.Check(plan);
    }

    [Fact]
    public void FreshestCopyOfAListingWinsOverAStaleCheaperOne()
    {
        var demand = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 5);
        var stale = Offer(Alpha, Ore, 5, 10, key: "listing");
        var live = new PlanOffer(Alpha.WorldId, Alpha.DataCenterId, Ore, false, 5, 40, 9, "listing", ObservedAt.AddMinutes(5), false);

        var plan = NewPlanner().Plan(Request(Settings(), [demand], stale, live));

        var line = Assert.Single(Lines(plan));
        Assert.Equal(5 * 40 + 9, line.Payable);
        Assert.False(line.Offer.IsEstimate);
    }

    [Fact]
    public void OffersOutsideTheWorldScopeAreIgnored()
    {
        var request = Request(Settings(), [OreDemand], Offer(Alpha, Ore, 10, 100), Offer(Beta, Ore, 10, 1)) with
        {
            Worlds = [Alpha, Gamma],
        };

        var plan = NewPlanner().Plan(request);

        Assert.Equal([Alpha.WorldId], StopWorldIds(plan));
    }

    [Fact]
    public void PlanIsDeterministicAcrossRunsAndInputOrder()
    {
        var random = new Random(77);
        for (var caseIndex = 0; caseIndex < 60; caseIndex++)
        {
            var request = OracleTests.RandomRequest(random, (PlanMode)(caseIndex % 3), caseIndex);
            var expected = Describe(NewPlanner().Plan(request));
            var shuffled = request with
            {
                Demands = Shuffled(request.Demands, random),
                Offers = Shuffled(request.Offers, random),
                Worlds = Shuffled(request.Worlds, random),
            };

            Assert.Equal(expected, Describe(NewPlanner().Plan(request)));
            Assert.Equal(expected, Describe(NewPlanner().Plan(shuffled)));
            Assert.Equal(expected, Describe(NewPlanner().Plan(shuffled with { Settings = shuffled.Settings with { ExactWorldLimit = 0 } })));
        }
    }

    [Fact]
    public void PlansNeverBreakCapsOrUnderreportOnRandomBaskets()
    {
        var random = new Random(31337);
        for (var caseIndex = 0; caseIndex < 200; caseIndex++)
        {
            var request = SharedStockRequest(random, caseIndex);
            PlanInvariants.Check(NewPlanner().Plan(request));
            PlanInvariants.Check(NewPlanner().Plan(request with { Settings = request.Settings with { ExactWorldLimit = 0 } }));
        }
    }

    [Fact]
    public void EvaluationLimitIsReportedAsASearchLimit()
    {
        var request = ModeFixture(PlanMode.Balanced);
        var plan = NewPlanner().Plan(request with { Settings = request.Settings with { SearchEvaluationLimit = 2 } });

        Assert.True(plan.SearchLimitReached);
        Assert.True(plan.Limits.HasFlag(PlanSearchLimits.EvaluationLimitReached));
        Assert.False(plan.IsProvenOptimal);
        PlanInvariants.Check(plan);
    }

    [Fact]
    public void TimeBudgetIsReportedAsASearchLimit()
    {
        var request = ModeFixture(PlanMode.Cheapest);
        var planner = new Planner(new SteppingTimeProvider(TimeSpan.FromSeconds(1)));

        var plan = planner.Plan(request with { Settings = request.Settings with { SearchTimeBudget = TimeSpan.FromSeconds(3) } });

        Assert.True(plan.Limits.HasFlag(PlanSearchLimits.TimeBudgetReached));
        Assert.True(plan.SearchLimitReached);
        PlanInvariants.Check(plan);
    }

    [Fact]
    public void OfferPruningIsReported()
    {
        var demand = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 2);
        var offers = new PlanOffer[6];
        for (var index = 0; index < offers.Length; index++)
        {
            offers[index] = Offer(Alpha, Ore, 1, 10 + index);
        }

        var request = Request(Settings() with { OffersPerWorldLimit = 2 }, [demand], offers);

        var plan = NewPlanner().Plan(request);

        Assert.True(plan.Limits.HasFlag(PlanSearchLimits.OffersPruned));
        Assert.Equal(2, plan.Provenance.UsableOfferCount);
    }

    [Fact]
    public void HeuristicSearchIsReportedAboveTheExactLimit()
    {
        var request = ModeFixture(PlanMode.Cheapest);
        var plan = NewPlanner().Plan(request with { Settings = request.Settings with { ExactWorldLimit = 2 } });

        Assert.True(plan.Limits.HasFlag(PlanSearchLimits.HeuristicSearch));
        Assert.False(plan.IsProvenOptimal);
        Assert.Equal([Beta.WorldId, Delta.WorldId], StopWorldIds(plan));
    }

    [Fact]
    public void CancellationStopsPlanning()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => NewPlanner().Plan(ModeFixture(PlanMode.Cheapest), cancellation.Token));
    }

    [Fact]
    public void ProvenanceAndTotalsDescribeThePlan()
    {
        var plan = NewPlanner().Plan(ModeFixture(PlanMode.Cheapest) with { SourceLabel = "fixture" });

        Assert.Equal("fixture", plan.Provenance.SourceLabel);
        Assert.Equal(ObservedAt, plan.Provenance.PlannedAtUtc);
        Assert.Equal(2, plan.Provenance.EstimatedLineCount);
        Assert.Equal(100, plan.Totals.FeeEstimate);
        Assert.Equal(20, plan.Totals.PlannedQuantity);
        Assert.Equal(0, plan.Totals.MissingQuantity);
        Assert.Equal(1, plan.Version);
    }

    [Fact]
    public void LargeQuantitiesStayExact()
    {
        var crystals = PlanDemand.ForPurchase(Resin, DemandQuality.Any, 2_500, excessAllowance: 100);
        var offers = new List<PlanOffer>();
        for (var index = 0; index < 30; index++)
        {
            offers.Add(Offer(AllWorlds[index % AllWorlds.Length], Resin, 99 + (index * 37 % 900), 5 + (index % 7), key: $"crystal{index}"));
        }

        var plan = NewPlanner().Plan(Request(Settings(), [crystals], [.. offers]));

        Assert.True(plan.IsComplete);
        Assert.InRange(PlannedQuantity(plan, crystals.Key), 2_500, 2_600);
        PlanInvariants.Check(plan);
    }

    internal static PlanRequest ModeFixture(PlanMode mode)
    {
        var settings = Settings(mode, Alpha) with
        {
            StopPenaltyGil = 100,
            CrossDataCenterPenaltyGil = 500,
            FewestStopsPremiumPercent = 20,
        };
        return Request(
            settings,
            [OreDemand, ClothDemand],
            Offer(Alpha, Ore, 10, 120),
            Offer(Alpha, Cloth, 10, 120),
            Offer(Beta, Ore, 10, 100),
            Offer(Beta, Cloth, 10, 125),
            Offer(Delta, Ore, 10, 125),
            Offer(Delta, Cloth, 10, 100));
    }

    private static PlanOffer Overflowing(PlanWorld world, uint itemId)
    {
        return new PlanOffer(world.WorldId, world.DataCenterId, itemId, false, 10, long.MaxValue / 4, 0, "overflow", ObservedAt, true);
    }

    private static PlanRequest SharedStockRequest(Random random, int caseIndex)
    {
        var demands = ImmutableArray.CreateBuilder<PlanDemand>();
        var offers = ImmutableArray.CreateBuilder<PlanOffer>();
        var itemCount = random.Next(1, 4);
        for (var itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            var itemId = 8_000 + (uint)itemIndex;
            for (var quality = 0; quality < 3; quality++)
            {
                if (random.Next(2) == 0)
                {
                    var cap = random.Next(4) == 0 ? random.Next(30, 120) : 0;
                    demands.Add(PlanDemand.ForPurchase(itemId, (DemandQuality)quality, random.Next(1, 30), random.Next(0, 6), cap));
                }
            }

            var offerCount = random.Next(0, 25);
            for (var offerIndex = 0; offerIndex < offerCount; offerIndex++)
            {
                var world = AllWorlds[random.Next(AllWorlds.Length)];
                offers.Add(Offer(world, itemId, random.Next(1, 15), random.Next(5, 130), random.Next(2) == 0, $"s{caseIndex}-{itemIndex}-{offerIndex}"));
            }
        }

        var settings = Settings((PlanMode)random.Next(3), AllWorlds[random.Next(AllWorlds.Length)]) with
        {
            StopPenaltyGil = random.Next(0, 300),
            CrossDataCenterPenaltyGil = random.Next(0, 500),
            FewestStopsPremiumPercent = random.Next(0, 40),
            MaxStops = random.Next(0, 4),
        };
        return new PlanRequest { Demands = demands.ToImmutable(), Offers = offers.ToImmutable(), Worlds = AllWorlds, Settings = settings };
    }
}
