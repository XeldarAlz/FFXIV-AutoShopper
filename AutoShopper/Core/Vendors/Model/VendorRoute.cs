using System.Collections.Immutable;

namespace AutoShopper.Core.Vendors.Model;

internal readonly record struct VendorPurchase(VendorLine Line, uint ShopId);

internal sealed record VendorStop(int SpotIndex, VendorSpot Spot, ImmutableArray<VendorPurchase> Purchases);

internal readonly record struct VendorRoutePlan(ImmutableArray<VendorStop> Stops, ImmutableArray<VendorLine> Unreachable)
{
    public static VendorRoutePlan Empty => new(ImmutableArray<VendorStop>.Empty, ImmutableArray<VendorLine>.Empty);
}

internal static class VendorRoute
{
    private const int SameTerritoryCost = 0;
    private const int TravelCostBase = 1;

    public static VendorRoutePlan Plan(ImmutableArray<VendorLine> lines, IVendorSpots spots, uint currentTerritoryId, Func<VendorSpot, bool> canReach)
    {
        if (lines.IsDefaultOrEmpty)
        {
            return VendorRoutePlan.Empty;
        }

        var reachable = new Dictionary<int, bool>();
        var open = new List<int>(lines.Length);
        var unreachable = ImmutableArray.CreateBuilder<VendorLine>();
        for (var index = 0; index < lines.Length; index++)
        {
            if (HasReachableOffer(lines[index].Demand.ItemId, spots, reachable, canReach))
            {
                open.Add(index);
            }
            else
            {
                unreachable.Add(lines[index]);
            }
        }

        var stops = new List<VendorStop>();
        var coverage = new Dictionary<int, int>();
        var lastLine = new Dictionary<int, int>();
        while (open.Count > 0)
        {
            var best = BestSpot(lines, open, spots, reachable, canReach, coverage, lastLine, currentTerritoryId);
            stops.Add(new VendorStop(best, spots.SpotAt(best), TakeLinesAt(best, lines, open, spots)));
        }

        stops.Sort((left, right) =>
        {
            var byCost = Cost(left.Spot, currentTerritoryId).CompareTo(Cost(right.Spot, currentTerritoryId));
            return byCost != 0 ? byCost : left.SpotIndex.CompareTo(right.SpotIndex);
        });

        return new VendorRoutePlan([.. stops], unreachable.ToImmutable());
    }

    private static bool HasReachableOffer(uint itemId, IVendorSpots spots, Dictionary<int, bool> reachable, Func<VendorSpot, bool> canReach)
    {
        var offers = spots.OffersFor(itemId);
        for (var index = 0; index < offers.Length; index++)
        {
            if (IsReachable(offers[index].SpotIndex, spots, reachable, canReach))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReachable(int spotIndex, IVendorSpots spots, Dictionary<int, bool> reachable, Func<VendorSpot, bool> canReach)
    {
        if (!reachable.TryGetValue(spotIndex, out var known))
        {
            known = canReach(spots.SpotAt(spotIndex));
            reachable.Add(spotIndex, known);
        }

        return known;
    }

    private static int BestSpot(ImmutableArray<VendorLine> lines, List<int> open, IVendorSpots spots, Dictionary<int, bool> reachable,
        Func<VendorSpot, bool> canReach, Dictionary<int, int> coverage, Dictionary<int, int> lastLine, uint currentTerritoryId)
    {
        coverage.Clear();
        lastLine.Clear();
        for (var openIndex = 0; openIndex < open.Count; openIndex++)
        {
            var lineIndex = open[openIndex];
            var offers = spots.OffersFor(lines[lineIndex].Demand.ItemId);
            for (var offerIndex = 0; offerIndex < offers.Length; offerIndex++)
            {
                var spotIndex = offers[offerIndex].SpotIndex;
                if (!IsReachable(spotIndex, spots, reachable, canReach))
                {
                    continue;
                }

                if (lastLine.TryGetValue(spotIndex, out var seen) && seen == lineIndex)
                {
                    continue;
                }

                lastLine[spotIndex] = lineIndex;
                coverage[spotIndex] = coverage.GetValueOrDefault(spotIndex) + 1;
            }
        }

        var best = -1;
        var bestCoverage = 0;
        var bestCost = int.MaxValue;
        foreach (var (spotIndex, covered) in coverage)
        {
            var cost = Cost(spots.SpotAt(spotIndex), currentTerritoryId);
            var better = covered > bestCoverage
                || (covered == bestCoverage && cost < bestCost)
                || (covered == bestCoverage && cost == bestCost && spotIndex < best);
            if (!better)
            {
                continue;
            }

            best = spotIndex;
            bestCoverage = covered;
            bestCost = cost;
        }

        return best;
    }

    private static ImmutableArray<VendorPurchase> TakeLinesAt(int spotIndex, ImmutableArray<VendorLine> lines, List<int> open, IVendorSpots spots)
    {
        var purchases = new List<VendorPurchase>();
        for (var openIndex = open.Count - 1; openIndex >= 0; openIndex--)
        {
            var line = lines[open[openIndex]];
            if (!TryShopAt(spotIndex, spots.OffersFor(line.Demand.ItemId), out var shopId))
            {
                continue;
            }

            purchases.Add(new VendorPurchase(line, shopId));
            open.RemoveAt(openIndex);
        }

        purchases.Sort(static (left, right) =>
        {
            var byShop = left.ShopId.CompareTo(right.ShopId);
            return byShop != 0 ? byShop : left.Line.Demand.Key.CompareTo(right.Line.Demand.Key);
        });

        return [.. purchases];
    }

    private static bool TryShopAt(int spotIndex, ReadOnlySpan<VendorOffer> offers, out uint shopId)
    {
        for (var index = 0; index < offers.Length; index++)
        {
            if (offers[index].SpotIndex == spotIndex)
            {
                shopId = offers[index].ShopId;
                return true;
            }
        }

        shopId = 0;
        return false;
    }

    private static int Cost(VendorSpot spot, uint currentTerritoryId)
        => spot.TerritoryId == currentTerritoryId ? SameTerritoryCost : TravelCostBase + (int)spot.Zone;
}
