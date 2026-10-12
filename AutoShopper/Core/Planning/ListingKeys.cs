using System.Globalization;

namespace AutoShopper.Core.Planning;

internal static class ListingKeys
{
    private const string GameListingPrefix = "game:";

    public static string ForGameListing(ulong listingId)
    {
        return string.Concat(GameListingPrefix, listingId.ToString(CultureInfo.InvariantCulture));
    }
}
