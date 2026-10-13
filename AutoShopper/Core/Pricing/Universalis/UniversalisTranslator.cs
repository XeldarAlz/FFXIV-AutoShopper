using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Pricing.Model;

namespace AutoShopper.Core.Pricing.Universalis;

// Turns one current-listings answer into offers for the planner. Listings outside the allowed worlds, on mannequins
// or with numbers out of range are dropped; every offer is an estimate whose fee is floor(5%) of the stack, as
// Universalis computes it, never the live listing's own fee.
internal static class UniversalisTranslator
{
    // The game client holds 100 listings per item and world, so a set that long may have been cut when uploaded.
    public const int TruncationThreshold = 100;

    // The market board's price field stops at 999,999,999 gil.
    public const long MaximumUnitPrice = 999_999_999;

    private const int MaximumListingIdLength = 64;
    private const string ForeignListingPrefix = "universalis:";

    // DateTime ends on 9999-12-31; a timestamp past it is malformed.
    private const long MaximumUnixMilliseconds = 253_402_300_799_999;
    private const long MaximumUnixSeconds = MaximumUnixMilliseconds / 1000;

    public static ChunkPrices Translate(ReadOnlySpan<byte> body, PriceChunk chunk, DateTime fetchedAtUtc)
    {
        UniversalisView? view;
        try
        {
            view = JsonSerializer.Deserialize(body, UniversalisJsonContext.Default.UniversalisView);
        }
        catch (JsonException)
        {
            return ChunkPrices.FailedWith(chunk, PriceFailure.Malformed);
        }

        return view is null ? ChunkPrices.FailedWith(chunk, PriceFailure.Malformed) : Translate(view, chunk, fetchedAtUtc);
    }

    public static ChunkPrices Translate(UniversalisView view, PriceChunk chunk, DateTime fetchedAtUtc)
    {
        var query = chunk.Query;
        var worldKeys = new string[query.Worlds.Length];
        for (var index = 0; index < worldKeys.Length; index++)
        {
            worldKeys[index] = query.Worlds[index].WorldId.ToString(CultureInfo.InvariantCulture);
        }

        var ids = chunk.ItemIds;
        var items = new ItemPrices[ids.Length];
        for (var index = 0; index < ids.Length; index++)
        {
            var itemId = ids[index];
            if (Contains(view.UnresolvedItems, itemId))
            {
                items[index] = ItemPrices.WithoutData(itemId, query.Worlds, PriceCoverage.NotMarketable);
                continue;
            }

            var itemView = FindItem(view, itemId);
            items[index] = itemView is null
                ? ItemPrices.WithoutData(itemId, query.Worlds, PriceCoverage.NotReturned)
                : TranslateItem(itemView, itemId, query, worldKeys, fetchedAtUtc);
        }

        return new ChunkPrices
        {
            Chunk = chunk,
            Failure = PriceFailure.None,
            Items = ImmutableCollectionsMarshal.AsImmutableArray(items),
        };
    }

    private static UniversalisView? FindItem(UniversalisView view, uint itemId)
    {
        if (view.Items is { } items)
        {
            return items.TryGetValue(itemId.ToString(CultureInfo.InvariantCulture), out var item) ? item : null;
        }

        return view.ItemId == itemId ? view : null;
    }

    private static ItemPrices TranslateItem(UniversalisView item, uint itemId, PriceQuery query, string[] worldKeys, DateTime fetchedAtUtc)
    {
        var worlds = query.Worlds;
        var uploads = new DateTime[worlds.Length];
        var coverages = new PriceCoverage[worlds.Length];
        var counts = new int[worlds.Length];
        for (var index = 0; index < worlds.Length; index++)
        {
            uploads[index] = UploadTime(item, query.Kind, worldKeys[index], out coverages[index]);
        }

        var itemCoverage = PriceCoverage.None;
        var listings = item.Listings ?? [];
        var offers = new List<PlanOffer>(listings.Length);
        for (var index = 0; index < listings.Length; index++)
        {
            var listing = listings[index];
            var worldIndex = query.IndexOfWorld(ListingWorld(listing, item, query));
            if (worldIndex < 0 || listing.OnMannequin)
            {
                continue;
            }

            var observedAt = uploads[worldIndex] != default ? uploads[worldIndex] : FromUnixSeconds(listing.LastReviewTime, fetchedAtUtc);
            if (!TryCreateOffer(listing, worlds[worldIndex], itemId, observedAt, out var offer))
            {
                itemCoverage |= PriceCoverage.InvalidListings;
                continue;
            }

            offers.Add(offer);
            counts[worldIndex]++;
            if (uploads[worldIndex] == default)
            {
                uploads[worldIndex] = observedAt;
                coverages[worldIndex] &= ~(PriceCoverage.NoData | PriceCoverage.MissingWorld);
            }
        }

        var entries = new WorldPrice[worlds.Length];
        for (var index = 0; index < worlds.Length; index++)
        {
            if (counts[index] >= TruncationThreshold)
            {
                coverages[index] |= PriceCoverage.Truncated;
            }

            entries[index] = new WorldPrice(worlds[index].WorldId, uploads[index], counts[index], coverages[index]);
            itemCoverage |= coverages[index];
        }

        Array.Sort(entries, ItemPrices.CompareWorlds);
        return new ItemPrices
        {
            ItemId = itemId,
            Coverage = itemCoverage,
            Worlds = ImmutableCollectionsMarshal.AsImmutableArray(entries),
            Offers = [.. offers],
        };
    }

    // A data center or region answer stamps each world it covers, 0 for a world with no upload; a world answer only
    // carries the item's own upload time.
    private static DateTime UploadTime(UniversalisView item, PriceQueryKind kind, string worldKey, out PriceCoverage coverage)
    {
        coverage = PriceCoverage.None;
        if (item.HasData == false)
        {
            coverage = PriceCoverage.NoData;
            return default;
        }

        long milliseconds;
        if (kind == PriceQueryKind.World)
        {
            milliseconds = item.LastUploadTime ?? 0;
        }
        else if (item.WorldUploadTimes is null || !item.WorldUploadTimes.TryGetValue(worldKey, out milliseconds))
        {
            coverage = PriceCoverage.MissingWorld;
            return default;
        }

        if (milliseconds <= 0 || milliseconds > MaximumUnixMilliseconds)
        {
            coverage = PriceCoverage.NoData;
            return default;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
    }

    private static uint ListingWorld(UniversalisListing listing, UniversalisView item, PriceQuery query)
    {
        var worldId = listing.WorldId ?? item.WorldId ?? (query.Kind == PriceQueryKind.World ? query.Worlds[0].WorldId : 0);
        return worldId is > 0 and <= uint.MaxValue ? (uint)worldId : 0;
    }

    private static bool TryCreateOffer(UniversalisListing listing, in ScopeWorld world, uint itemId, DateTime observedAt, out PlanOffer offer)
    {
        offer = default;
        var listingId = listing.ListingId;
        if (string.IsNullOrEmpty(listingId) || listingId.Length > MaximumListingIdLength)
        {
            return false;
        }

        if (listing.Quantity is <= 0 or > PlanDemand.MaximumQuantity || listing.PricePerUnit is < 0 or > MaximumUnitPrice)
        {
            return false;
        }

        // Universalis passes the game's own listing id through, so the same listing seen live later keeps its key.
        var key = ulong.TryParse(listingId, NumberStyles.None, CultureInfo.InvariantCulture, out var numericId)
            ? ListingKeys.ForGameListing(numericId)
            : string.Concat(ForeignListingPrefix, listingId);
        return PlanOffer.TryCreateEstimate(world.WorldId, world.DataCenterId, itemId, listing.IsHq, (int)listing.Quantity, listing.PricePerUnit, key, observedAt, out offer);
    }

    private static DateTime FromUnixSeconds(long seconds, DateTime fallback)
    {
        if (seconds <= 0 || seconds > MaximumUnixSeconds)
        {
            return fallback;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
    }

    private static bool Contains(long[]? values, uint itemId)
    {
        if (values is null)
        {
            return false;
        }

        for (var index = 0; index < values.Length; index++)
        {
            if (values[index] == itemId)
            {
                return true;
            }
        }

        return false;
    }
}
