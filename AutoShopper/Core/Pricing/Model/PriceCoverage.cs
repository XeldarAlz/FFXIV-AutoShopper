namespace AutoShopper.Core.Pricing.Model;

[Flags]
internal enum PriceCoverage : ushort
{
    None = 0,
    NoData = 1 << 0,
    MissingWorld = 1 << 1,
    RequestFailed = 1 << 2,
    Truncated = 1 << 3,
    // Universalis answers 404 for an item it does not track and for a scope it does not know.
    NotFound = 1 << 4,
    NotMarketable = 1 << 5,
    NotReturned = 1 << 6,
    InvalidListings = 1 << 7,
    NotFetched = 1 << 8,
}

internal static class PriceCoverageGroups
{
    // A cut listing set is not partial coverage: the board sends listings cheapest first, so the cut only hides
    // dearer stock. An item Universalis does not track is reported on its own line, not as a gap.
    public const PriceCoverage Partial =
        PriceCoverage.MissingWorld
        | PriceCoverage.RequestFailed
        | PriceCoverage.NotReturned
        | PriceCoverage.InvalidListings
        | PriceCoverage.NotFetched;

    public const PriceCoverage Unknown = PriceCoverage.NotFound | PriceCoverage.NotMarketable;

    public const PriceCoverage Failed = PriceCoverage.RequestFailed | PriceCoverage.NotReturned;
}
