using System.Collections.Immutable;
using AutoShopper.Core.Planning;
using static AutoShopper.Tests.Fixtures;

namespace AutoShopper.Tests;

public sealed class OracleTests
{
    private const int CaseCount = 150;
    private const uint FirstItem = 7_000;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ExactSearchMatchesBruteForceOracle(int modeValue)
    {
        var mode = (PlanMode)modeValue;
        var random = new Random(9_000 + modeValue);
        for (var caseIndex = 0; caseIndex < CaseCount; caseIndex++)
        {
            var request = RandomRequest(random, mode, caseIndex);
            var plan = NewPlanner().Plan(request);
            var expected = BruteForce.BestBasket(request);
            var actual = BruteForce.Measure(plan);
            var allowed = BruteForce.AllowedPayable(BruteForce.CheapestBasket(request).Cost, request.Settings.FewestStopsPremiumPercent);

            Assert.True(plan.IsProvenOptimal, $"case {caseIndex} was not searched exhaustively");
            Assert.True(
                BruteForce.Compare(mode, actual, expected, allowed) == 0,
                $"case {caseIndex}: planner {actual} oracle {expected}");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void HeuristicSearchAgreesWithBruteForceOracleOnSmallFixtures(int modeValue)
    {
        var mode = (PlanMode)modeValue;
        var random = new Random(19_000 + modeValue);
        var agreements = 0;
        for (var caseIndex = 0; caseIndex < CaseCount; caseIndex++)
        {
            var request = RandomRequest(random, mode, caseIndex);
            var heuristicRequest = request with { Settings = request.Settings with { ExactWorldLimit = 0 } };
            var plan = NewPlanner().Plan(heuristicRequest);
            var expected = BruteForce.BestBasket(request);
            var actual = BruteForce.Measure(plan);
            var allowed = BruteForce.AllowedPayable(BruteForce.CheapestBasket(request).Cost, request.Settings.FewestStopsPremiumPercent);
            var order = BruteForce.Compare(mode, actual, expected, allowed);

            Assert.True(plan.Provenance.CandidateWorldCount == 0 || plan.Limits.HasFlag(PlanSearchLimits.HeuristicSearch));
            Assert.True(order >= 0, $"case {caseIndex}: heuristic {actual} beat the oracle {expected}");
            Assert.Equal(expected.Missing, actual.Missing);
            if (order == 0)
            {
                agreements++;
            }
        }

        Assert.True(agreements >= CaseCount * 95 / 100, $"heuristic agreed on {agreements} of {CaseCount}");
    }

    [Fact]
    public void ExactAndHeuristicPlansAreValidOnRandomFixtures()
    {
        var random = new Random(4_242);
        for (var caseIndex = 0; caseIndex < CaseCount; caseIndex++)
        {
            var mode = (PlanMode)random.Next(3);
            var request = RandomRequest(random, mode, caseIndex);
            PlanInvariants.Check(NewPlanner().Plan(request));
            PlanInvariants.Check(NewPlanner().Plan(request with { Settings = request.Settings with { ExactWorldLimit = 0 } }));
        }
    }

    internal static PlanRequest RandomRequest(Random random, PlanMode mode, int caseIndex)
    {
        var itemCount = random.Next(1, 4);
        var demands = ImmutableArray.CreateBuilder<PlanDemand>(itemCount);
        var offers = ImmutableArray.CreateBuilder<PlanOffer>();
        for (var itemIndex = 0; itemIndex < itemCount; itemIndex++)
        {
            var itemId = FirstItem + (uint)itemIndex;
            var quality = (DemandQuality)random.Next(3);
            var cap = random.Next(10) < 3 ? random.Next(40, 150) : 0;
            demands.Add(PlanDemand.ForPurchase(itemId, quality, random.Next(1, 11), random.Next(0, 4), cap));
            var offerCount = random.Next(0, 7);
            for (var offerIndex = 0; offerIndex < offerCount; offerIndex++)
            {
                var world = AllWorlds[random.Next(AllWorlds.Length)];
                var key = $"c{caseIndex}-i{itemIndex}-o{offerIndex}";
                offers.Add(Offer(world, itemId, random.Next(1, 7), random.Next(10, 121), random.Next(2) == 0, key));
            }
        }

        var settings = Settings(mode, AllWorlds[random.Next(AllWorlds.Length)], AllWorlds[random.Next(AllWorlds.Length)], random.Next(2) == 0) with
        {
            StopPenaltyGil = random.Next(0, 201),
            CrossDataCenterPenaltyGil = random.Next(0, 401),
            FewestStopsPremiumPercent = random.Next(0, 51),
            MaxStops = random.Next(0, 4),
        };
        return new PlanRequest
        {
            Demands = demands.ToImmutable(),
            Offers = offers.ToImmutable(),
            Worlds = AllWorlds,
            Settings = settings,
        };
    }
}
