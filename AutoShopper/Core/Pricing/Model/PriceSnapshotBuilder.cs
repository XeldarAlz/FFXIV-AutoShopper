using System.Collections.Immutable;
using System.Runtime.InteropServices;
using AutoShopper.Core.Planning;

namespace AutoShopper.Core.Pricing.Model;

internal static class PriceSnapshotBuilder
{
    // Items the previous snapshot priced over the same worlds stay in, so switching lists does not drop prices that
    // are still the newest known; each item keeps its own upload times, so carried prices age honestly.
    public static PriceSnapshot Build(
        long generation,
        DateTime fetchedAtUtc,
        ImmutableArray<ScopeWorld> worlds,
        IReadOnlyList<ChunkPrices> chunks,
        PriceSnapshot? previous)
    {
        var accumulators = new Dictionary<uint, ItemAccumulator>();
        var failed = 0;
        for (var chunkIndex = 0; chunkIndex < chunks.Count; chunkIndex++)
        {
            var chunk = chunks[chunkIndex];
            // A 404 is an answer: the item or scope is unknown to Universalis, which the item's coverage records.
            if (chunk.Failed && chunk.Failure != PriceFailure.NotFound)
            {
                failed++;
            }

            var items = chunk.Items;
            for (var itemIndex = 0; itemIndex < items.Length; itemIndex++)
            {
                var prices = items[itemIndex];
                if (!accumulators.TryGetValue(prices.ItemId, out var accumulator))
                {
                    accumulator = new ItemAccumulator(prices.ItemId);
                    accumulators.Add(prices.ItemId, accumulator);
                }

                accumulator.Add(prices);
            }
        }

        var built = new List<ItemPrices>(accumulators.Count);
        foreach (var accumulator in accumulators.Values)
        {
            built.Add(accumulator.Finish(worlds));
        }

        if (previous is not null && previous.HasSameWorlds(worlds))
        {
            var carried = previous.Items;
            for (var index = 0; index < carried.Length; index++)
            {
                if (!accumulators.ContainsKey(carried[index].ItemId))
                {
                    built.Add(carried[index]);
                }
            }
        }

        built.Sort(static (left, right) => left.ItemId.CompareTo(right.ItemId));
        var coverage = PriceCoverage.None;
        var offerCount = 0;
        for (var index = 0; index < built.Count; index++)
        {
            coverage |= built[index].Coverage;
            offerCount += built[index].Offers.Length;
        }

        return new PriceSnapshot
        {
            Generation = generation,
            FetchedAtUtc = fetchedAtUtc,
            Worlds = worlds,
            Items = [.. built],
            Offers = CollectOffers(built, offerCount),
            Freshness = Freshness(worlds, built),
            Coverage = coverage,
            RequestCount = chunks.Count,
            FailedRequestCount = failed,
        };
    }

    private static ImmutableArray<PlanOffer> CollectOffers(List<ItemPrices> items, int count)
    {
        var offers = new PlanOffer[count];
        var write = 0;
        for (var index = 0; index < items.Count; index++)
        {
            var source = items[index].Offers;
            source.CopyTo(offers, write);
            write += source.Length;
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(offers);
    }

    // Upload ages per world over the given items, oldest and newest, with how many of them have an upload there.
    public static ImmutableArray<WorldFreshness> Freshness(ImmutableArray<ScopeWorld> worlds, IReadOnlyList<ItemPrices> items)
    {
        var freshness = new WorldFreshness[worlds.Length];
        for (var worldIndex = 0; worldIndex < worlds.Length; worldIndex++)
        {
            var world = worlds[worldIndex];
            var oldest = default(DateTime);
            var newest = default(DateTime);
            var withData = 0;
            var withoutData = 0;
            var coverage = PriceCoverage.None;
            for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                if (!TryFindWorld(items[itemIndex].Worlds, world.WorldId, out var price))
                {
                    continue;
                }

                coverage |= price.Coverage;
                if (!price.HasUpload)
                {
                    withoutData++;
                    continue;
                }

                withData++;
                if (oldest == default || price.UploadedAtUtc < oldest)
                {
                    oldest = price.UploadedAtUtc;
                }

                if (price.UploadedAtUtc > newest)
                {
                    newest = price.UploadedAtUtc;
                }
            }

            freshness[worldIndex] = new WorldFreshness(world, oldest, newest, withData, withoutData, coverage);
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(freshness);
    }

    public static bool TryFindWorld(ImmutableArray<WorldPrice> sorted, uint worldId, out WorldPrice price)
    {
        var low = 0;
        var high = sorted.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var candidate = sorted[middle];
            if (candidate.WorldId == worldId)
            {
                price = candidate;
                return true;
            }

            if (candidate.WorldId < worldId)
            {
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        price = default;
        return false;
    }

    private sealed class ItemAccumulator
    {
        private readonly uint itemId;
        private readonly Dictionary<uint, WorldPrice> worlds = [];
        private readonly List<PlanOffer> offers = [];
        private PriceCoverage coverage;

        public ItemAccumulator(uint itemId)
        {
            this.itemId = itemId;
        }

        public void Add(ItemPrices prices)
        {
            coverage |= prices.Coverage;
            var entries = prices.Worlds;
            for (var index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                if (worlds.TryGetValue(entry.WorldId, out var existing) && existing.HasUpload && !entry.HasUpload)
                {
                    continue;
                }

                worlds[entry.WorldId] = entry;
            }

            var source = prices.Offers;
            for (var index = 0; index < source.Length; index++)
            {
                offers.Add(source[index]);
            }
        }

        public ItemPrices Finish(ImmutableArray<ScopeWorld> allowed)
        {
            for (var index = 0; index < allowed.Length; index++)
            {
                var worldId = allowed[index].WorldId;
                if (worlds.ContainsKey(worldId))
                {
                    continue;
                }

                worlds.Add(worldId, new WorldPrice(worldId, default, 0, PriceCoverage.MissingWorld));
                coverage |= PriceCoverage.MissingWorld;
            }

            var entries = new WorldPrice[worlds.Count];
            worlds.Values.CopyTo(entries, 0);
            Array.Sort(entries, ItemPrices.CompareWorlds);
            return new ItemPrices
            {
                ItemId = itemId,
                Coverage = coverage,
                Worlds = ImmutableCollectionsMarshal.AsImmutableArray(entries),
                Offers = [.. offers],
            };
        }
    }
}
