using System.Globalization;

namespace AutoShopper.Core.Planning;

internal static class ListingKeys
{
    private const string GameListingPrefix = "game:";
    private const string VendorPurchasePrefix = "vendor:";
    private const string AttemptFormat = "N";

    public static string ForGameListing(ulong listingId)
    {
        return string.Concat(GameListingPrefix, listingId.ToString(CultureInfo.InvariantCulture));
    }

    public static string ForVendorPurchase(Guid attemptId)
    {
        return string.Concat(VendorPurchasePrefix, attemptId.ToString(AttemptFormat, CultureInfo.InvariantCulture));
    }
}
