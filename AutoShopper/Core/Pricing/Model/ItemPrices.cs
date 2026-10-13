using System.Collections.Immutable;
using System.Runtime.InteropServices;
using AutoShopper.Core.Planning;

namespace AutoShopper.Core.Pricing.Model;

internal readonly record struct WorldPrice(uint WorldId, DateTime UploadedAtUtc, int ListingCount, PriceCoverage Coverage)
{
    public bool HasUpload => UploadedAtUtc != default;
}

internal sealed class ItemPrices
{
    public required uint ItemId { get; init; }

    public required PriceCoverage Coverage { get; init; }

    // Sorted by world id.
    public required ImmutableArray<WorldPrice> Worlds { get; init; }

    public required ImmutableArray<PlanOffer> Offers { get; init; }

    public bool HasAnyUpload
    {
        get
        {
            for (var index = 0; index < Worlds.Length; index++)
            {
                if (Worlds[index].HasUpload)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static ItemPrices WithoutData(uint itemId, ImmutableArray<ScopeWorld> worlds, PriceCoverage coverage)
    {
        var entries = new WorldPrice[worlds.Length];
        for (var index = 0; index < worlds.Length; index++)
        {
            entries[index] = new WorldPrice(worlds[index].WorldId, default, 0, coverage);
        }

        Array.Sort(entries, CompareWorlds);
        return new ItemPrices
        {
            ItemId = itemId,
            Coverage = coverage,
            Worlds = ImmutableCollectionsMarshal.AsImmutableArray(entries),
            Offers = ImmutableArray<PlanOffer>.Empty,
        };
    }

    public static int CompareWorlds(WorldPrice left, WorldPrice right) => left.WorldId.CompareTo(right.WorldId);
}

internal enum PriceFailure : byte
{
    None,
    NotFound,
    RateLimited,
    HttpError,
    Timeout,
    Malformed,
    RetriesExhausted,
    // Never sent: the server had asked every request to wait longer than a fetch holds.
    ServerPaused,
}

internal sealed class ChunkPrices
{
    public required PriceChunk Chunk { get; init; }

    public required PriceFailure Failure { get; init; }

    public required ImmutableArray<ItemPrices> Items { get; init; }

    public bool Failed => Failure != PriceFailure.None;

    // True when the server answered for these items, so their new prices replace the old ones.
    public bool Answered => !Failed || MeansNotTracked(Chunk, Failure);

    public static ChunkPrices FailedWith(PriceChunk chunk, PriceFailure failure)
    {
        var coverage = MeansNotTracked(chunk, failure) ? PriceCoverage.NotFound : PriceCoverage.RequestFailed;
        var items = new ItemPrices[chunk.ItemIds.Length];
        for (var index = 0; index < items.Length; index++)
        {
            items[index] = ItemPrices.WithoutData(chunk.ItemIds[index], chunk.Query.Worlds, coverage);
        }

        return new ChunkPrices
        {
            Chunk = chunk,
            Failure = failure,
            Items = ImmutableCollectionsMarshal.AsImmutableArray(items),
        };
    }

    // Universalis answers a request for several items with the ones it does not track listed as unresolved, so a 404
    // names an untracked item only when the request asked for that item alone; for several items it is a failure.
    private static bool MeansNotTracked(PriceChunk chunk, PriceFailure failure)
        => failure == PriceFailure.NotFound && chunk.ItemIds.Length == 1;
}
