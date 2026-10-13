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

// The search proxy and the pages Dalamud parsed come from the same packets, so any difference means the proxy still
// holds part of an older search, or a page was lost, and the copy is not fit to buy from.
internal static class ListingCrossCheck
{
    public static CrossCheckResult Compare(ReadOnlySpan<ObservedListing> captured, ReadOnlySpan<OfferedListing> offered)
    {
        if (captured.Length != offered.Length)
        {
            return CrossCheckResult.CountMismatch;
        }

        for (var index = 0; index < captured.Length; index++)
        {
            var listing = captured[index];
            if (AppearsBefore(captured, index, listing.ListingId))
            {
                return CrossCheckResult.DuplicateListing;
            }

            var offeredIndex = IndexOf(offered, listing.ListingId);
            if (offeredIndex < 0)
            {
                return CrossCheckResult.MissingListing;
            }

            if (!SameFields(listing, offered[offeredIndex]))
            {
                return CrossCheckResult.FieldMismatch;
            }
        }

        return CrossCheckResult.Match;
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

    private static bool SameFields(in ObservedListing captured, in OfferedListing offered)
    {
        return captured.RetainerId == offered.RetainerId
            && MarketItemId.Normalize(captured.ItemId) == MarketItemId.Normalize(offered.ItemId)
            && captured.IsHq == offered.IsHq
            && captured.Quantity == offered.Quantity
            && captured.UnitPrice == offered.UnitPrice
            && captured.TotalTax == offered.TotalTax
            && captured.IsMannequin == offered.OnMannequin;
    }
}
