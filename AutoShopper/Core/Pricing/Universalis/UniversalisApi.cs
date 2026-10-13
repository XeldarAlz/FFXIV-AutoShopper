using System.Globalization;
using System.Text;
using AutoShopper.Core.Pricing.Model;

namespace AutoShopper.Core.Pricing.Universalis;

internal static class UniversalisApi
{
    public const string BaseAddress = "https://universalis.app/";
    public const string RepositoryUrl = "https://github.com/XeldarAlz/FFXIV-AutoShopper";

    // The current-listings endpoint reads at most 100 item ids and silently drops the rest.
    public const int MaximumIdsPerRequest = 100;

    // A region answer merges every world of the region, so fewer ids per request keeps each body a few megabytes and
    // well inside the server's 10 second timeout.
    public const int RegionIdsPerRequest = 25;

    private const string CurrentListingsPrefix = "api/v2/";
    private const string Query = "?entries=0&fields=";

    // A multi-item answer nests every field under "items."; one requested id switches the server to the single shape.
    private const string MultiItemFields =
        "itemIDs,unresolvedItems,items.itemID,items.lastUploadTime,items.worldUploadTimes,items.hasData,"
        + "items.listings.listingID,items.listings.pricePerUnit,items.listings.quantity,items.listings.hq,"
        + "items.listings.worldID,items.listings.onMannequin,items.listings.lastReviewTime";

    private const string SingleItemFields =
        "itemID,worldID,lastUploadTime,worldUploadTimes,hasData,"
        + "listings.listingID,listings.pricePerUnit,listings.quantity,listings.hq,"
        + "listings.worldID,listings.onMannequin,listings.lastReviewTime";

    private const int CharactersPerId = 7;

    public static string UserAgent(string version) => string.Concat("AutoShopper/", version, " (+", RepositoryUrl, ")");

    public static int IdsPerRequest(PriceQueryKind kind) => kind == PriceQueryKind.Region ? RegionIdsPerRequest : MaximumIdsPerRequest;

    public static string CurrentListingsPath(PriceChunk chunk)
    {
        var ids = chunk.ItemIds;
        var single = ids.Length == 1;
        var builder = new StringBuilder(CurrentListingsPrefix.Length + chunk.Query.Segment.Length + ids.Length * CharactersPerId + Query.Length + MultiItemFields.Length);
        builder.Append(CurrentListingsPrefix).Append(Uri.EscapeDataString(chunk.Query.Segment)).Append('/');
        for (var index = 0; index < ids.Length; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(ids[index].ToString(CultureInfo.InvariantCulture));
        }

        builder.Append(Query).Append(single ? SingleItemFields : MultiItemFields);
        return builder.ToString();
    }

    // Region names as the API spells them; matching is case-sensitive. The ids follow the game's region column.
    public static string? RegionSegment(byte regionId) => regionId switch
    {
        1 => "Japan",
        2 => "North-America",
        3 => "Europe",
        4 => "Oceania",
        _ => null,
    };
}
