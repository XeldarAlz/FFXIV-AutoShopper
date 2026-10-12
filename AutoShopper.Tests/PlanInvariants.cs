using AutoShopper.Core.Planning;

namespace AutoShopper.Tests;

internal static class PlanInvariants
{
    public static void Check(PurchasePlan plan)
    {
        var demands = MergedDemands(plan.Request);
        var excluded = new HashSet<string>(plan.Request.ExcludedListingKeys, StringComparer.Ordinal);
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);
        var stopWorlds = new HashSet<uint>();
        var planned = new Dictionary<DemandKey, int>();
        long purchase = 0;
        var plannedTotal = 0;
        for (var stopIndex = 0; stopIndex < plan.Stops.Length; stopIndex++)
        {
            var stop = plan.Stops[stopIndex];
            Assert.True(stopWorlds.Add(stop.World.WorldId), "a world appears twice in the route");
            Assert.Contains(stop.World, plan.Request.Worlds);
            Assert.NotEmpty(stop.Lines);
            for (var lineIndex = 0; lineIndex < stop.Lines.Length; lineIndex++)
            {
                var line = stop.Lines[lineIndex];
                Assert.True(usedKeys.Add(line.Offer.ListingKey), $"listing {line.Offer.ListingKey} used twice");
                Assert.DoesNotContain(line.Offer.ListingKey, excluded);
                Assert.Equal(stop.World.WorldId, line.Offer.WorldId);
                Assert.True(demands.TryGetValue(line.Demand, out var demand), $"line for unknown demand {line.Demand}");
                Assert.Equal(demand.ItemId, line.Offer.ItemId);
                Assert.True(line.Demand.Accepts(line.Offer.IsHq), "quality requirement broken");
                Assert.True(line.Offer.TryGetPayable(out var payable));
                Assert.Equal(payable, line.Payable);
                Assert.True(demand.AllowsStackCost(line.Payable, line.Offer.Quantity), "unit cap broken");
                planned.TryGetValue(line.Demand, out var soFar);
                planned[line.Demand] = soFar + line.Offer.Quantity;
                purchase += line.Payable;
                plannedTotal += line.Offer.Quantity;
            }
        }

        long missingTotal = 0;
        foreach (var demand in demands.Values)
        {
            planned.TryGetValue(demand.Key, out var quantity);
            Assert.True(quantity <= demand.MaximumTotal, $"{demand.Key} planned {quantity} above {demand.MaximumTotal}");
            var unresolved = FindUnresolved(plan, demand.Key);
            if (quantity >= demand.RequiredQuantity)
            {
                Assert.Null(unresolved);
                continue;
            }

            Assert.NotNull(unresolved);
            Assert.Equal(demand.RequiredQuantity, unresolved.Value.RequiredQuantity);
            Assert.Equal(quantity, unresolved.Value.CoveredQuantity);
            Assert.Equal(demand.RequiredQuantity - quantity, unresolved.Value.MissingQuantity);
            missingTotal += unresolved.Value.MissingQuantity;
        }

        Assert.Equal(purchase, plan.Totals.PurchaseEstimate);
        Assert.Equal(plannedTotal, plan.Totals.PlannedQuantity);
        Assert.Equal(missingTotal, plan.Totals.MissingQuantity);
        Assert.Equal(plan.Stops.Length, plan.Totals.StopCount);
        Assert.Equal(plan.Unresolved.IsEmpty, plan.IsComplete);
    }

    private static UnresolvedDemand? FindUnresolved(PurchasePlan plan, DemandKey key)
    {
        for (var index = 0; index < plan.Unresolved.Length; index++)
        {
            if (plan.Unresolved[index].Demand == key)
            {
                return plan.Unresolved[index];
            }
        }

        return null;
    }

    private static Dictionary<DemandKey, PlanDemand> MergedDemands(PlanRequest request)
    {
        var merged = new Dictionary<DemandKey, PlanDemand>();
        for (var index = 0; index < request.Demands.Length; index++)
        {
            var demand = request.Demands[index];
            if (!demand.IsValid || demand.RequiredQuantity == 0)
            {
                continue;
            }

            if (!merged.TryGetValue(demand.Key, out var existing))
            {
                merged[demand.Key] = demand;
                continue;
            }

            var cap = existing.UnitCapIncludingFee <= 0
                ? demand.UnitCapIncludingFee
                : demand.UnitCapIncludingFee <= 0 ? existing.UnitCapIncludingFee : Math.Min(existing.UnitCapIncludingFee, demand.UnitCapIncludingFee);
            merged[demand.Key] = new PlanDemand(
                demand.Key,
                existing.RequiredQuantity + demand.RequiredQuantity,
                existing.ExcessAllowance + demand.ExcessAllowance,
                cap);
        }

        return merged;
    }
}
