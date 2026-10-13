namespace AutoShopper.Core.Market.Model;

// How reading a chosen listing back from the live search went.
internal enum LiveListingRead : byte
{
    Found = 0,
    // The proxy no longer holds the search the listing came from: no copy of it, no proxy, or another item searched.
    Stale = 1,
    // The proxy still holds that search, and the listing is not in it any more.
    Gone = 2,
}
