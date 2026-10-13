using System.Collections.Immutable;
using System.Threading;
using AutoShopper.Core.Planning;
using static AutoShopper.Tests.Fixtures;

namespace AutoShopper.Tests;

// The exact world search keeps a knapsack table per depth and single-line item; past the stack allocator's 2^28-bit
// budget it hands over to the heuristic search, and below it nothing changes.
public sealed class PlanSearchGuardTests
{
    private const uint FirstItem = 8_000;
    private const int TableBudgetCosts = (1 << 28) / 64;

    private static readonly ImmutableArray<PlanWorld> ThreeWorlds = [Alpha, Beta, Gamma];

    [Fact]
    public void ARealCrystalRestockStaysWithinTheBudgetAndIsSearchedExactly()
    {
        var demands = ImmutableArray.CreateBuilder<PlanDemand>();
        var offers = ImmutableArray.CreateBuilder<PlanOffer>();
        for (var itemIndex = 0; itemIndex < 18; itemIndex++)
        {
            var itemId = FirstItem + (uint)itemIndex;
            demands.Add(PlanDemand.ForPurchase(itemId, DemandQuality.Any, 9_999));
            for (var worldIndex = 0; worldIndex < AllWorlds.Length; worldIndex++)
            {
                for (var offerIndex = 0; offerIndex < 3; offerIndex++)
                {
                    offers.Add(Offer(AllWorlds[worldIndex], itemId, 999 * (offerIndex + 1), 10 + worldIndex + offerIndex, key: $"r{itemIndex}-{worldIndex}-{offerIndex}"));
                }
            }
        }

        var request = Request(Settings(PlanMode.Cheapest), demands.ToImmutable(), offers.ToArray());

        Assert.True(IncrementalSubsetSearch.Fits(ProblemOf(request), request.Settings.StopLimitFor(AllWorlds.Length)));
        var plan = NewPlanner().Plan(request);
        Assert.False(plan.Limits.HasFlag(PlanSearchLimits.HeuristicSearch));
        Assert.True(plan.IsProvenOptimal);
        PlanInvariants.Check(plan);
    }

    [Fact]
    public void SmallRandomPlansAllFitTheBudget()
    {
        var random = new Random(31_337);
        for (var caseIndex = 0; caseIndex < 100; caseIndex++)
        {
            var request = OracleTests.RandomRequest(random, (PlanMode)random.Next(3), caseIndex);
            var problem = ProblemOf(request);

            Assert.True(IncrementalSubsetSearch.Fits(problem, request.Settings.StopLimitFor(problem.Candidates.Length)));
        }
    }

    [Theory]
    [InlineData(TableBudgetCosts / 4 - 1, true)]
    [InlineData(TableBudgetCosts / 4, false)]
    public void TheBudgetCountsEveryReachableQuantityAtEveryDepth(int ceiling, bool fits)
    {
        var request = HugeRequest(ceiling);

        Assert.Equal(fits, IncrementalSubsetSearch.Fits(ProblemOf(request), stopLimit: 3));
    }

    [Fact]
    public void AHugeQuantityFallsBackToTheHeuristicSearchAndStillPlans()
    {
        var request = HugeRequest(1_500_000);

        var plan = NewPlanner().Plan(request, CancellationToken.None);

        Assert.True(plan.Limits.HasFlag(PlanSearchLimits.HeuristicSearch));
        Assert.True(plan.IsComplete);
        Assert.InRange(PlannedQuantity(plan, new DemandKey(FirstItem, DemandQuality.Any)), 1_000_000, 1_500_000);
        PlanInvariants.Check(plan);
    }

    [Fact]
    public void TheClockIsReadWithoutSpendingAnEvaluation()
    {
        var clock = new SteppingClock(TimeSpan.FromSeconds(1));
        var budget = new SearchBudget(clock, TimeSpan.FromSeconds(2.5), 100, CancellationToken.None);

        Assert.True(budget.HasTimeLeft());
        Assert.True(budget.HasTimeLeft());
        Assert.False(budget.HasTimeLeft());
        Assert.Equal(0, budget.Evaluations);
        Assert.Equal(PlanSearchLimits.TimeBudgetReached, budget.Reached);
        Assert.False(budget.TryCharge());
    }

    // One item over three worlds whose knapsack tracks `ceiling` units: the demand allows exactly that many and the
    // listings hold more.
    private static PlanRequest HugeRequest(int ceiling)
    {
        var required = Math.Min(ceiling, PlanDemand.MaximumQuantity);
        var demand = PlanDemand.ForPurchase(FirstItem, DemandQuality.Any, required, ceiling - required);
        var offers = new PlanOffer[ThreeWorlds.Length * 2];
        for (var index = 0; index < offers.Length; index++)
        {
            offers[index] = Offer(ThreeWorlds[index / 2], FirstItem, 300_000, 5 + index, key: $"h{index}");
        }

        return Request(Settings(PlanMode.Cheapest), [demand], offers) with { Worlds = ThreeWorlds };
    }

    private static PlanningProblem ProblemOf(PlanRequest request)
    {
        var settings = request.Settings;
        return PlanningProblemBuilder.Build(
            DemandNormalizer.Normalize(request.Demands).OpenLines(),
            request.Offers,
            request.Worlds,
            request.ExcludedListingKeys,
            settings.EffectiveOffersPerWorldLimit,
            settings.CurrentWorld.WorldId);
    }

    private sealed class SteppingClock(TimeSpan step) : TimeProvider
    {
        private long now;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            var current = now;
            now += step.Ticks;
            return current;
        }
    }
}
