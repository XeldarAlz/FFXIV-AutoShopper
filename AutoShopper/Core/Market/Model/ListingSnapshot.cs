using System.Collections.Immutable;
using System.Runtime.InteropServices;
using AutoShopper.Core.Execution.Model;

namespace AutoShopper.Core.Market.Model;

[Flags]
internal enum ListingMarks : byte
{
    None = 0,
    Own = 1,
    Mannequin = 2,
}

// One completed search, copied out of the game: every listing tagged with the world, the search generation and the
// capture time, and marked when the guard must refuse it.
internal sealed class ListingSnapshot
{
    private ListingSnapshot()
    {
    }

    public required uint ItemId { get; init; }

    public required uint WorldId { get; init; }

    public required int Generation { get; init; }

    public required DateTime CapturedAtUtc { get; init; }

    public required ulong PlayerContentId { get; init; }

    // The count the server announced for the search, or -1 when its summary was not seen.
    public required int ExpectedCount { get; init; }

    public required ImmutableArray<ObservedListing> Listings { get; init; }

    public required ImmutableArray<ListingMarks> Marks { get; init; }

    public bool IsComplete => ExpectedCount < 0 || Listings.Length == ExpectedCount;

    public static ListingSnapshot Create(
        uint itemId,
        uint worldId,
        int generation,
        DateTime capturedAtUtc,
        ulong playerContentId,
        int expectedCount,
        ReadOnlySpan<ObservedListing> listings)
    {
        var copies = new ObservedListing[listings.Length];
        var marks = new ListingMarks[listings.Length];
        for (var index = 0; index < listings.Length; index++)
        {
            copies[index] = listings[index];
            marks[index] = MarksOf(listings[index], playerContentId);
        }

        return new ListingSnapshot
        {
            ItemId = itemId,
            WorldId = worldId,
            Generation = generation,
            CapturedAtUtc = capturedAtUtc,
            PlayerContentId = playerContentId,
            ExpectedCount = expectedCount,
            Listings = ImmutableCollectionsMarshal.AsImmutableArray(copies),
            Marks = ImmutableCollectionsMarshal.AsImmutableArray(marks),
        };
    }

    public static ListingMarks MarksOf(in ObservedListing listing, ulong playerContentId)
    {
        var marks = ListingMarks.None;
        if (listing.SellerContentId != 0 && listing.SellerContentId == playerContentId)
        {
            marks |= ListingMarks.Own;
        }

        if (listing.IsMannequin)
        {
            marks |= ListingMarks.Mannequin;
        }

        return marks;
    }

    public bool TryFind(ulong listingId, out ObservedListing listing)
    {
        for (var index = 0; index < Listings.Length; index++)
        {
            if (Listings[index].ListingId == listingId)
            {
                listing = Listings[index];
                return true;
            }
        }

        listing = default;
        return false;
    }
}
