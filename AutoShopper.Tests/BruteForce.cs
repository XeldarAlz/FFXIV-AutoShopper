using System.Collections.Immutable;
using System.Numerics;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Shared;

namespace AutoShopper.Tests;

internal readonly record struct OracleLine(int Missing, long Cost);

internal readonly record struct OracleBasket(long Missing, long Cost, int Stops, int TravelStops, int DataCenterChanges, long Score);

internal static class BruteForce
{
    public static OracleLine BestLine(PlanDemand demand, ReadOnlySpan<PlanOffer> offers)
    {
        var eligible = new List<(int Quantity, long Payable)>();
        for (var index = 0; index < offers.Length; index++)
        {
            var offer = offers[index];
            if (offer.ItemId != demand.ItemId || !demand.Key.Accepts(offer.IsHq) || offer.Quantity > demand.MaximumTotal)
            {
                continue;
            }

            if (!offer.TryGetPayable(out var payable) || !demand.AllowsStackCost(payable, offer.Quantity))
            {
                continue;
            }

            eligible.Add((offer.Quantity, payable));
        }

        var bestCompleteCost = long.MaxValue;
        var bestPartialQuantity = 0;
        long bestPartialCost = 0;
        var subsetCount = 1 << eligible.Count;
        for (var subset = 0; subset < subsetCount; subset++)
        {
            var quantity = 0;
            long cost = 0;
            for (var index = 0; index < eligible.Count; index++)
            {
                if ((subset & (1 << index)) == 0)
                {
                    continue;
                }

                quantity += eligible[index].Quantity;
                cost += eligible[index].Payable;
            }

            if (quantity > demand.MaximumTotal)
            {
                continue;
            }

            if (quantity >= demand.RequiredQuantity)
            {
                bestCompleteCost = Math.Min(bestCompleteCost, cost);
                continue;
            }

            if (quantity > bestPartialQuantity || (quantity == bestPartialQuantity && cost < bestPartialCost))
            {
                bestPartialQuantity = quantity;
                bestPartialCost = cost;
            }
        }

        if (bestCompleteCost != long.MaxValue)
        {
            return new OracleLine(0, bestCompleteCost);
        }

        return new OracleLine(demand.RequiredQuantity - bestPartialQuantity, bestPartialCost);
    }

    public static OracleBasket BestBasket(PlanRequest request)
    {
        var settings = request.Settings;
        var worlds = request.Worlds;
        var subsetCount = 1 << worlds.Length;
        var evaluations = new List<OracleBasket>();
        for (var subset = 0; subset < subsetCount; subset++)
        {
            var size = BitOperations.PopCount((uint)subset);
            if (settings.MaxStops > 0 && size > settings.MaxStops)
            {
                continue;
            }

            evaluations.Add(Evaluate(request, subset));
        }

        var cheapest = evaluations[0];
        for (var index = 1; index < evaluations.Count; index++)
        {
            if (CompareCheapest(evaluations[index], cheapest) < 0)
            {
                cheapest = evaluations[index];
            }
        }

        if (settings.Mode == PlanMode.Cheapest)
        {
            return cheapest;
        }

        var allowed = AllowedPayable(cheapest.Cost, settings.FewestStopsPremiumPercent);
        var best = evaluations[0];
        for (var index = 1; index < evaluations.Count; index++)
        {
            var candidate = evaluations[index];
            var order = settings.Mode == PlanMode.Balanced ? CompareBalanced(candidate, best) : CompareFewestStops(candidate, best, allowed);
            if (order < 0)
            {
                best = candidate;
            }
        }

        return best;
    }

    public static OracleBasket Measure(PurchasePlan plan)
    {
        var settings = plan.Request.Settings;
        var stops = plan.Totals.StopCount;
        var travelStops = stops;
        for (var index = 0; index < plan.Stops.Length; index++)
        {
            if (plan.Stops[index].World.WorldId == settings.CurrentWorld.WorldId)
            {
                travelStops--;
            }
        }

        var changes = plan.Totals.DataCenterChanges;
        return new OracleBasket(plan.Totals.MissingQuantity, plan.Totals.PurchaseEstimate, stops, travelStops, changes, Score(settings, plan.Totals.PurchaseEstimate, travelStops, changes));
    }

    public static int Compare(PlanMode mode, OracleBasket left, OracleBasket right, long allowedPayable)
    {
        return mode switch
        {
            PlanMode.Balanced => CompareBalanced(left, right),
            PlanMode.FewestStops => CompareFewestStops(left, right, allowedPayable),
            _ => CompareCheapest(left, right),
        };
    }

    public static long AllowedPayable(long baseline, int premiumPercent)
    {
        Assert.True(Gil.TryPercentFloor(baseline, premiumPercent, out var premium));
        return baseline + premium;
    }

    public static OracleBasket CheapestBasket(PlanRequest request)
    {
        return BestBasket(request with { Settings = request.Settings with { Mode = PlanMode.Cheapest } });
    }

    private static OracleBasket Evaluate(PlanRequest request, int subset)
    {
        var settings = request.Settings;
        var worlds = request.Worlds;
        var inScope = new List<PlanOffer>();
        for (var index = 0; index < request.Offers.Length; index++)
        {
            var offer = request.Offers[index];
            var worldIndex = IndexOf(worlds, offer.WorldId);
            if (worldIndex >= 0 && (subset & (1 << worldIndex)) != 0)
            {
                inScope.Add(offer);
            }
        }

        long missing = 0;
        long cost = 0;
        for (var index = 0; index < request.Demands.Length; index++)
        {
            var line = BestLine(request.Demands[index], inScope.ToArray());
            missing += line.Missing;
            cost += line.Cost;
        }

        var stops = 0;
        var travelStops = 0;
        var dataCenters = new List<uint>();
        for (var worldIndex = 0; worldIndex < worlds.Length; worldIndex++)
        {
            if ((subset & (1 << worldIndex)) == 0)
            {
                continue;
            }

            stops++;
            if (worlds[worldIndex].WorldId != settings.CurrentWorld.WorldId)
            {
                travelStops++;
            }

            if (!dataCenters.Contains(worlds[worldIndex].DataCenterId))
            {
                dataCenters.Add(worlds[worldIndex].DataCenterId);
            }
        }

        var changes = RouteOrderer.CountDataCenterChanges(
            dataCenters.ToArray(),
            settings.CurrentWorld.DataCenterId,
            settings.HomeWorld.DataCenterId,
            settings.ReturnHome);
        return new OracleBasket(missing, cost, stops, travelStops, changes, Score(settings, cost, travelStops, changes));
    }

    private static long Score(PlanSettings settings, long cost, int travelStops, int changes)
    {
        return cost + (settings.EffectiveStopPenalty * travelStops) + (settings.EffectiveCrossDataCenterPenalty * changes);
    }

    private static int IndexOf(ImmutableArray<PlanWorld> worlds, uint worldId)
    {
        for (var index = 0; index < worlds.Length; index++)
        {
            if (worlds[index].WorldId == worldId)
            {
                return index;
            }
        }

        return -1;
    }

    private static int CompareCheapest(OracleBasket left, OracleBasket right)
    {
        var order = left.Missing.CompareTo(right.Missing);
        return order != 0 ? order : left.Cost.CompareTo(right.Cost);
    }

    private static int CompareBalanced(OracleBasket left, OracleBasket right)
    {
        var order = left.Missing.CompareTo(right.Missing);
        return order != 0 ? order : left.Score.CompareTo(right.Score);
    }

    private static int CompareFewestStops(OracleBasket left, OracleBasket right, long allowed)
    {
        var order = left.Missing.CompareTo(right.Missing);
        if (order != 0)
        {
            return order;
        }

        order = (left.Cost > allowed).CompareTo(right.Cost > allowed);
        if (order != 0)
        {
            return order;
        }

        order = left.Stops.CompareTo(right.Stops);
        return order != 0 ? order : left.Cost.CompareTo(right.Cost);
    }
}
