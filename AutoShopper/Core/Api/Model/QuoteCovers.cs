using System.Collections.Immutable;
using AutoShopper.Core.Planning;

namespace AutoShopper.Core.Api.Model;

internal static class QuoteCovers
{
    private const int HoursDigits = 1;

    public static QuoteItemResult[] Compute(ReadOnlySpan<QuoteRequestItem> items, ImmutableArray<PlanOffer> offers, DateTime fetchedAtUtc)
    {
        var largestStacks = LargestStacks(offers);
        var demands = ImmutableArray.CreateBuilder<PlanDemand>(items.Length * 2);
        for (var index = 0; index < items.Length; index++)
        {
            demands.Add(Demand(items[index], DemandQuality.NormalQuality, largestStacks));
            demands.Add(Demand(items[index], DemandQuality.HighQuality, largestStacks));
        }

        var allocations = StackAllocator.AllocateAll(demands.MoveToImmutable(), offers);
        var results = new QuoteItemResult[items.Length];
        for (var index = 0; index < items.Length; index++)
        {
            var item = items[index];
            results[index] = new QuoteItemResult(
                item.ItemId,
                item.Quantity,
                Cover(Find(allocations, new DemandKey(item.ItemId, DemandQuality.NormalQuality)), fetchedAtUtc),
                Cover(Find(allocations, new DemandKey(item.ItemId, DemandQuality.HighQuality)), fetchedAtUtc));
        }

        return results;
    }

    private static Dictionary<DemandKey, int> LargestStacks(ImmutableArray<PlanOffer> offers)
    {
        var largest = new Dictionary<DemandKey, int>();
        for (var index = 0; index < offers.Length; index++)
        {
            var offer = offers[index];
            var key = new DemandKey(offer.ItemId, offer.IsHq ? DemandQuality.HighQuality : DemandQuality.NormalQuality);
            if (!largest.TryGetValue(key, out var quantity) || offer.Quantity > quantity)
            {
                largest[key] = offer.Quantity;
            }
        }

        return largest;
    }

    private static PlanDemand Demand(in QuoteRequestItem item, DemandQuality quality, Dictionary<DemandKey, int> largestStacks)
    {
        largestStacks.TryGetValue(new DemandKey(item.ItemId, quality), out var largest);
        var excess = Math.Clamp(largest - 1, 0, PlanDemand.MaximumQuantity);
        return PlanDemand.ForPurchase(item.ItemId, quality, item.Quantity, excess);
    }

    private static LineAllocation? Find(ImmutableArray<LineAllocation> allocations, DemandKey key)
    {
        var low = 0;
        var high = allocations.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var order = allocations[middle].Demand.Key.CompareTo(key);
            if (order == 0)
            {
                return allocations[middle];
            }

            if (order < 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return null;
    }

    private static QuoteCover Cover(LineAllocation? allocation, DateTime fetchedAtUtc)
    {
        if (allocation is null || allocation.MissingQuantity > 0 || allocation.PlannedQuantity <= 0)
        {
            return default;
        }

        var units = allocation.PlannedQuantity;
        var total = allocation.Payable;
        return new QuoteCover(
            true,
            units,
            total,
            UnitPriceOf(total, units),
            HighestUnitPrice(allocation.Offers),
            OldestHours(allocation.Offers, fetchedAtUtc),
            WorldCount(allocation.Offers));
    }

    private static long UnitPriceOf(long payable, int units) => (payable / units) + (payable % units == 0 ? 0 : 1);

    private static long HighestUnitPrice(ImmutableArray<PlanOffer> offers)
    {
        long highest = 0;
        for (var index = 0; index < offers.Length; index++)
        {
            var offer = offers[index];
            var payable = checked((offer.UnitPrice * offer.Quantity) + offer.Fee);
            highest = Math.Max(highest, UnitPriceOf(payable, offer.Quantity));
        }

        return highest;
    }

    private static double OldestHours(ImmutableArray<PlanOffer> offers, DateTime fetchedAtUtc)
    {
        var oldest = DateTime.MaxValue;
        for (var index = 0; index < offers.Length; index++)
        {
            var observed = offers[index].ObservedAtUtc;
            if (observed != default && observed < oldest)
            {
                oldest = observed;
            }
        }

        if (oldest == DateTime.MaxValue || oldest >= fetchedAtUtc)
        {
            return 0d;
        }

        return Math.Round((fetchedAtUtc - oldest).TotalHours, HoursDigits, MidpointRounding.AwayFromZero);
    }

    private static int WorldCount(ImmutableArray<PlanOffer> offers)
    {
        var count = 0;
        for (var index = 0; index < offers.Length; index++)
        {
            if (IndexOfWorld(offers, offers[index].WorldId) == index)
            {
                count++;
            }
        }

        return count;
    }

    private static int IndexOfWorld(ImmutableArray<PlanOffer> offers, uint worldId)
    {
        for (var index = 0; index < offers.Length; index++)
        {
            if (offers[index].WorldId == worldId)
            {
                return index;
            }
        }

        return -1;
    }
}
