using System.Collections.Immutable;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Shared;

namespace AutoShopper.Core.Vendors.Model;

internal static class VendorSplit
{
    public static VendorSplitResult Split(ImmutableArray<PlanDemand> demands, ImmutableArray<PlanOffer> offers, VendorPriceTable prices)
    {
        if (demands.IsDefaultOrEmpty || prices.IsEmpty)
        {
            return VendorSplitResult.MarketOnly(demands);
        }

        var listingsByItem = MarketListings(demands, offers, prices);
        var market = ImmutableArray.CreateBuilder<PlanDemand>(demands.Length);
        var vendor = ImmutableArray.CreateBuilder<VendorLine>();
        long total = 0;
        for (var index = 0; index < demands.Length; index++)
        {
            var demand = demands[index];
            if (!TryVendorPrice(demand, prices, out var unitPrice) || !VendorCostsNoMore(demand, unitPrice, listingsByItem))
            {
                market.Add(demand);
                continue;
            }

            var line = new VendorLine(demand, demand.RequiredQuantity, unitPrice);
            vendor.Add(line);
            total = Gil.TryAdd(total, line.Payable, out var sum) ? sum : long.MaxValue;
        }

        return new VendorSplitResult(market.ToImmutable(), vendor.ToImmutable(), total);
    }

    private static bool TryVendorPrice(PlanDemand demand, VendorPriceTable prices, out long unitPrice)
    {
        unitPrice = 0;
        if (demand.RequiredQuantity <= 0 || demand.Quality == DemandQuality.HighQuality)
        {
            return false;
        }

        if (!prices.TryGetPrice(demand.ItemId, out unitPrice) || unitPrice <= 0)
        {
            return false;
        }

        return !demand.HasUnitCap || unitPrice <= demand.UnitCapIncludingFee;
    }

    private static bool VendorCostsNoMore(PlanDemand demand, long vendorUnitPrice, Dictionary<uint, List<MarketListing>> listingsByItem)
    {
        if (!listingsByItem.TryGetValue(demand.ItemId, out var listings))
        {
            return true;
        }

        var remaining = demand.RequiredQuantity;
        var marketCost = 0d;
        for (var index = 0; index < listings.Count && remaining > 0; index++)
        {
            var listing = listings[index];
            if (!demand.Key.Accepts(listing.IsHq))
            {
                continue;
            }

            var taken = Math.Min(remaining, listing.Quantity);
            marketCost += taken * listing.UnitCost;
            remaining -= taken;
        }

        return remaining > 0 || marketCost >= (double)vendorUnitPrice * demand.RequiredQuantity;
    }

    private static Dictionary<uint, List<MarketListing>> MarketListings(ImmutableArray<PlanDemand> demands, ImmutableArray<PlanOffer> offers, VendorPriceTable prices)
    {
        var listingsByItem = new Dictionary<uint, List<MarketListing>>();
        for (var index = 0; index < demands.Length; index++)
        {
            var itemId = demands[index].ItemId;
            if (prices.TryGetPrice(itemId, out _) && !listingsByItem.ContainsKey(itemId))
            {
                listingsByItem.Add(itemId, []);
            }
        }

        if (offers.IsDefaultOrEmpty)
        {
            return listingsByItem;
        }

        for (var index = 0; index < offers.Length; index++)
        {
            var offer = offers[index];
            if (offer.Quantity <= 0 || !listingsByItem.TryGetValue(offer.ItemId, out var listings) || !offer.TryGetPayable(out var payable))
            {
                continue;
            }

            listings.Add(new MarketListing(offer.IsHq, offer.Quantity, (double)payable / offer.Quantity));
        }

        foreach (var listings in listingsByItem.Values)
        {
            listings.Sort(static (left, right) => left.UnitCost.CompareTo(right.UnitCost));
        }

        return listingsByItem;
    }

    private readonly record struct MarketListing(bool IsHq, int Quantity, double UnitCost);
}
