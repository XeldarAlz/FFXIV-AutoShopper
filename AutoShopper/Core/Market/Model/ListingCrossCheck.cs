using AutoShopper.Core.Execution.Model;

namespace AutoShopper.Core.Market.Model;

internal enum CrossCheckResult : byte
{
    Match = 0,
    CountMismatch = 1,
    DuplicateListing = 2,
    MissingListing = 3,
    FieldMismatch = 4,
}

// Where a cross check stopped: the proxy listing and the page listing it was looking at, and the field they differ in.
internal readonly record struct CrossCheck(CrossCheckResult Result, int CapturedIndex, int OfferedIndex, ListingField Field)
{
    public static readonly CrossCheck Match = new(CrossCheckResult.Match, -1, -1, ListingField.None);

    public static CrossCheck Of(CrossCheckResult result) => new(result, -1, -1, ListingField.None);
}

// The search proxy and the pages Dalamud parsed come from the same packets, so any difference means the proxy still
// holds part of an older search, or a page was lost, and the copy is not fit to buy from.
internal static class ListingCrossCheck
{
    public static CrossCheck Compare(ReadOnlySpan<ObservedListing> captured, ReadOnlySpan<OfferedListing> offered)
    {
        if (captured.Length != offered.Length)
        {
            return CrossCheck.Of(CrossCheckResult.CountMismatch);
        }

        for (var index = 0; index < captured.Length; index++)
        {
            var listing = captured[index];
            if (AppearsBefore(captured, index, listing.ListingId))
            {
                return new CrossCheck(CrossCheckResult.DuplicateListing, index, -1, ListingField.ListingId);
            }

            var offeredIndex = IndexOf(offered, listing.ListingId);
            if (offeredIndex < 0)
            {
                return new CrossCheck(CrossCheckResult.MissingListing, index, -1, ListingField.ListingId);
            }

            var field = FirstDifference(listing, offered[offeredIndex]);
            if (field != ListingField.None)
            {
                return new CrossCheck(CrossCheckResult.FieldMismatch, index, offeredIndex, field);
            }
        }

        return CrossCheck.Match;
    }

    private static bool AppearsBefore(ReadOnlySpan<ObservedListing> captured, int end, ulong listingId)
    {
        for (var index = 0; index < end; index++)
        {
            if (captured[index].ListingId == listingId)
            {
                return true;
            }
        }

        return false;
    }

    private static int IndexOf(ReadOnlySpan<OfferedListing> offered, ulong listingId)
    {
        for (var index = 0; index < offered.Length; index++)
        {
            if (offered[index].ListingId == listingId)
            {
                return index;
            }
        }

        return -1;
    }

    private static ListingField FirstDifference(in ObservedListing captured, in OfferedListing offered)
    {
        if (captured.RetainerId != offered.RetainerId)
        {
            return ListingField.RetainerId;
        }

        if (MarketItemId.Normalize(captured.ItemId) != MarketItemId.Normalize(offered.ItemId))
        {
            return ListingField.ItemId;
        }

        if (captured.Quantity != offered.Quantity)
        {
            return ListingField.Quantity;
        }

        if (captured.UnitPrice != offered.UnitPrice)
        {
            return ListingField.UnitPrice;
        }

        if (captured.TotalTax != offered.TotalTax)
        {
            return ListingField.TotalTax;
        }

        if (captured.IsHq != offered.IsHq)
        {
            return ListingField.Quality;
        }

        return captured.IsMannequin != offered.OnMannequin ? ListingField.Mannequin : ListingField.None;
    }
}
