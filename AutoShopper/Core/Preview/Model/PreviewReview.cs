using System.Collections.Immutable;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Pricing.Model;

namespace AutoShopper.Core.Preview.Model;

internal enum PreviewReason : byte
{
    InvalidDemand,
    NoListings,
    AboveUnitCap,
    InsufficientStock,
    NoFittingStacks,
    TakenByStricterDemand,
    StopLimit,
    SearchLimit,
    NotFetched,
    NotMarketable,
    RequestFailed,
    NoMarketData,
    Capped,
}

[Flags]
internal enum PreviewWarnings : ushort
{
    None = 0,
    NoPrices = 1 << 0,
    StaleData = 1 << 1,
    PartialCoverage = 1 << 2,
    SearchLimit = 1 << 3,
    MissingPrices = 1 << 4,
    UnknownOwned = 1 << 5,
    ScopeChanged = 1 << 6,
    Incomplete = 1 << 7,
}

internal static class PreviewReview
{
    public static PreviewWarnings Evaluate(PreviewResult result, DateTime nowUtc, TimeSpan staleAfter)
    {
        var input = result.Input;
        var plan = result.Selected;
        var warnings = PreviewWarnings.None;
        if (!plan.IsComplete)
        {
            warnings |= PreviewWarnings.Incomplete;
        }

        if (plan.SearchLimitReached)
        {
            warnings |= PreviewWarnings.SearchLimit;
        }

        if (input.Demands.TargetsUnknown > 0)
        {
            warnings |= PreviewWarnings.UnknownOwned;
        }

        var prices = input.Prices;
        if (prices is null)
        {
            return warnings | PreviewWarnings.NoPrices;
        }

        if (!SameWorlds(prices.Worlds, input.Worlds))
        {
            warnings |= PreviewWarnings.ScopeChanged;
        }

        var itemIds = input.Demands.ItemIds;
        for (var index = 0; index < itemIds.Length; index++)
        {
            if (!prices.TryGetItem(itemIds[index], out var item))
            {
                warnings |= PreviewWarnings.MissingPrices;
                continue;
            }

            if ((item.Coverage & PriceCoverageGroups.Partial) != PriceCoverage.None)
            {
                warnings |= PreviewWarnings.PartialCoverage;
            }
        }

        var oldestUsed = plan.Provenance.OldestObservationUtc;
        if (IsOlderThan(prices.FetchedAtUtc, nowUtc, staleAfter) || (oldestUsed != default && IsOlderThan(oldestUsed, nowUtc, staleAfter)))
        {
            warnings |= PreviewWarnings.StaleData;
        }

        return warnings;
    }

    // The planner only sees offers, so an item with none reads as "no listings"; the snapshot tells whether that is
    // an empty market, an item Universalis does not track, a failed request or an item never fetched. A world cut at
    // the listing cap keeps only its cheapest listings, which can hide the dearer stock of one quality, so missing or
    // short stock there is reported as capped.
    public static PreviewReason ReasonFor(in UnresolvedDemand unresolved, PriceSnapshot? prices)
    {
        if (unresolved.Reason == UnresolvedReason.InsufficientStock)
        {
            return IsCapped(unresolved.Demand.ItemId, prices) ? PreviewReason.Capped : PreviewReason.InsufficientStock;
        }

        if (unresolved.Reason != UnresolvedReason.NoListings)
        {
            return FromPlanner(unresolved.Reason);
        }

        if (prices is null || !prices.TryGetItem(unresolved.Demand.ItemId, out var item))
        {
            return PreviewReason.NotFetched;
        }

        if ((item.Coverage & PriceCoverageGroups.Unknown) != PriceCoverage.None)
        {
            return PreviewReason.NotMarketable;
        }

        if ((item.Coverage & PriceCoverage.Truncated) != PriceCoverage.None)
        {
            return PreviewReason.Capped;
        }

        if (item.HasAnyUpload)
        {
            return PreviewReason.NoListings;
        }

        return (item.Coverage & PriceCoverageGroups.Failed) != PriceCoverage.None ? PreviewReason.RequestFailed : PreviewReason.NoMarketData;
    }

    private static bool IsCapped(uint itemId, PriceSnapshot? prices)
        => prices is not null && prices.TryGetItem(itemId, out var item) && (item.Coverage & PriceCoverage.Truncated) != PriceCoverage.None;

    public static bool IsOlderThan(DateTime atUtc, DateTime nowUtc, TimeSpan age) => atUtc != default && nowUtc - atUtc > age;

    public static bool SameWorlds(ImmutableArray<ScopeWorld> fetched, ImmutableArray<PlanWorld> planned)
    {
        if (fetched.Length != planned.Length)
        {
            return false;
        }

        for (var index = 0; index < planned.Length; index++)
        {
            if (PriceSnapshot.IndexOfWorld(fetched, planned[index].WorldId) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static PreviewReason FromPlanner(UnresolvedReason reason) => reason switch
    {
        UnresolvedReason.InvalidDemand => PreviewReason.InvalidDemand,
        UnresolvedReason.AboveUnitCap => PreviewReason.AboveUnitCap,
        UnresolvedReason.InsufficientStock => PreviewReason.InsufficientStock,
        UnresolvedReason.NoFittingStacks => PreviewReason.NoFittingStacks,
        UnresolvedReason.TakenByStricterDemand => PreviewReason.TakenByStricterDemand,
        UnresolvedReason.StopLimit => PreviewReason.StopLimit,
        UnresolvedReason.SearchLimit => PreviewReason.SearchLimit,
        _ => PreviewReason.NoListings,
    };
}
