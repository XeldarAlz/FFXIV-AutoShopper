using System.Collections.Immutable;
using AutoShopper.Core.Planning;
using static AutoShopper.Tests.Fixtures;

namespace AutoShopper.Tests;

public sealed class RouteOrdererTests
{
    private const uint Ore = 5_100;
    private const uint Cloth = 5_200;

    [Fact]
    public void CurrentWorldComesFirstWhenItIsAStop()
    {
        var route = RouteOrderer.Order([Alpha, Beta, Gamma], Gamma, Gamma, false);

        Assert.Equal([Gamma, Alpha, Beta], route.AsSpan().ToArray());
    }

    [Fact]
    public void StopsAreGroupedByDataCenterStartingWithTheCurrentOne()
    {
        var route = RouteOrderer.Order([Delta, Beta, Zeta, Epsilon, Alpha], Gamma, Gamma, false);

        Assert.Equal([Alpha, Beta, Delta, Epsilon, Zeta], route.AsSpan().ToArray());
        Assert.Equal(2, RouteOrderer.CountDataCenterChanges(route.AsSpan(), Gamma, Gamma, false));
    }

    [Fact]
    public void HomeDataCenterAndHomeWorldComeLastWhenReturningHome()
    {
        var route = RouteOrderer.Order([Delta, Beta, Zeta, Epsilon], Alpha, Delta, true);

        Assert.Equal([Beta, Zeta, Epsilon, Delta], route.AsSpan().ToArray());
        Assert.Equal(2, RouteOrderer.CountDataCenterChanges(route.AsSpan(), Alpha, Delta, true));
        Assert.Equal(4, RouteOrderer.CountWorldChanges(route.AsSpan(), Alpha, Delta, true));
    }

    [Fact]
    public void HomeWorldStaysLastWhenTheHomeDataCenterIsAlsoTheCurrentOne()
    {
        var route = RouteOrderer.Order([Alpha, Beta, Delta, Gamma], Gamma, Alpha, true);

        Assert.Equal([Gamma, Delta, Beta, Alpha], route.AsSpan().ToArray());
        Assert.Equal(2, RouteOrderer.CountDataCenterChanges(route.AsSpan(), Gamma, Alpha, true));
        Assert.Equal(3, RouteOrderer.CountWorldChanges(route.AsSpan(), Gamma, Alpha, true));
    }

    [Fact]
    public void ReturningHomeCountsTheTripBack()
    {
        var route = RouteOrderer.Order([Delta], Alpha, Alpha, true);

        Assert.Equal(2, RouteOrderer.CountDataCenterChanges(route.AsSpan(), Alpha, Alpha, true));
        Assert.Equal(2, RouteOrderer.CountWorldChanges(route.AsSpan(), Alpha, Alpha, true));
        Assert.Equal(0, RouteOrderer.CountDataCenterChanges(ImmutableArray<PlanWorld>.Empty.AsSpan(), Alpha, Alpha, true));
    }

    [Fact]
    public void OrderDoesNotDependOnInputOrder()
    {
        var random = new Random(5);
        var stops = ImmutableArray.Create(Alpha, Beta, Gamma, Delta, Epsilon, Zeta);
        var expected = RouteOrderer.Order(stops.AsSpan(), Epsilon, Beta, true).AsSpan().ToArray();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var shuffled = Shuffled(stops, random);
            Assert.Equal(expected, RouteOrderer.Order(shuffled.AsSpan(), Epsilon, Beta, true).AsSpan().ToArray());
        }
    }

    [Fact]
    public void SelectorChangeCountMatchesTheOrderedRoute()
    {
        var random = new Random(11);
        for (var caseIndex = 0; caseIndex < 300; caseIndex++)
        {
            var stops = new List<PlanWorld>();
            for (var worldIndex = 0; worldIndex < AllWorlds.Length; worldIndex++)
            {
                if (random.Next(2) == 0)
                {
                    stops.Add(AllWorlds[worldIndex]);
                }
            }

            var current = AllWorlds[random.Next(AllWorlds.Length)];
            var home = AllWorlds[random.Next(AllWorlds.Length)];
            var returnHome = random.Next(2) == 0;
            var route = RouteOrderer.Order(stops.ToArray(), current, home, returnHome);
            var distinct = new List<uint>();
            for (var index = 0; index < stops.Count; index++)
            {
                if (!distinct.Contains(stops[index].DataCenterId))
                {
                    distinct.Add(stops[index].DataCenterId);
                }
            }

            Assert.Equal(
                RouteOrderer.CountDataCenterChanges(route.AsSpan(), current, home, returnHome),
                RouteOrderer.CountDataCenterChanges(distinct.ToArray(), current.DataCenterId, home.DataCenterId, returnHome));
            if (stops.Contains(current))
            {
                Assert.Equal(current, route[0]);
            }

            if (returnHome && stops.Contains(home) && home != current)
            {
                Assert.Equal(home, route[^1]);
            }
        }
    }

    [Fact]
    public void PlannedStopsFollowTheRouteRules()
    {
        var request = Request(
            Settings(PlanMode.Cheapest, Beta, Delta, returnHome: true),
            [PlanDemand.ForPurchase(Ore, DemandQuality.Any, 5), PlanDemand.ForPurchase(Cloth, DemandQuality.Any, 5)],
            Offer(Delta, Ore, 5, 10),
            Offer(Beta, Cloth, 5, 10),
            Offer(Zeta, Ore, 5, 50));

        var plan = NewPlanner().Plan(request);

        Assert.Equal([Beta.WorldId, Delta.WorldId], StopWorldIds(plan));
        Assert.Equal(1, plan.Totals.DataCenterChanges);
        Assert.Equal(1, plan.Totals.WorldChanges);
    }
}
