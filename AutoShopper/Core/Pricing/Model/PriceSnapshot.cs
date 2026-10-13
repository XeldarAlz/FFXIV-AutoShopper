using System.Collections.Immutable;
using AutoShopper.Core.Planning;

namespace AutoShopper.Core.Pricing.Model;

internal readonly record struct WorldFreshness(
    ScopeWorld World,
    DateTime OldestUploadUtc,
    DateTime NewestUploadUtc,
    int ItemsWithData,
    int ItemsWithoutData,
    PriceCoverage Coverage)
{
    public bool HasData => ItemsWithData > 0;
}

// Prices of one fetch, never changed after it is built, so planning threads and the window read it without locks.
// Every number in it is a Universalis estimate; a run reads live listings before it buys.
internal sealed class PriceSnapshot
{
    public required long Generation { get; init; }

    public required DateTime FetchedAtUtc { get; init; }

    public required ImmutableArray<ScopeWorld> Worlds { get; init; }

    // Sorted by item id.
    public required ImmutableArray<ItemPrices> Items { get; init; }

    public required ImmutableArray<PlanOffer> Offers { get; init; }

    // In the order of Worlds.
    public required ImmutableArray<WorldFreshness> Freshness { get; init; }

    public required PriceCoverage Coverage { get; init; }

    public required int RequestCount { get; init; }

    public required int FailedRequestCount { get; init; }

    public bool TryGetItem(uint itemId, out ItemPrices item)
    {
        var low = 0;
        var high = Items.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var candidate = Items[middle];
            if (candidate.ItemId == itemId)
            {
                item = candidate;
                return true;
            }

            if (candidate.ItemId < itemId)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        item = null!;
        return false;
    }

    public bool HasSameWorlds(ImmutableArray<ScopeWorld> worlds)
    {
        if (worlds.Length != Worlds.Length)
        {
            return false;
        }

        for (var index = 0; index < worlds.Length; index++)
        {
            if (IndexOfWorld(Worlds, worlds[index].WorldId) < 0)
            {
                return false;
            }
        }

        return true;
    }

    public static int IndexOfWorld(ImmutableArray<ScopeWorld> worlds, uint worldId)
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
}
