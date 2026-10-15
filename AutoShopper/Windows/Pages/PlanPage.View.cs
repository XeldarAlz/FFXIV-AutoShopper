using System.Collections.Immutable;
using AutoShopper.Core.Items;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Preview.Model;
using AutoShopper.Core.Pricing.Model;
using AutoShopper.Core.Vendors;
using AutoShopper.Core.Vendors.Model;
using AutoShopper.Windows.Sections.Config;
using ECommons.DalamudServices;

namespace AutoShopper.Windows.Pages;

// Every string the plan draws is built here once per result, language or minute, so drawing the page allocates
// nothing on an idle frame.
internal sealed partial class PlanPage
{
    private enum FreshnessState : byte
    {
        Fresh,
        Stale,
        NoData,
        Failed,
    }

    private readonly record struct LineView(uint IconId, bool HighQuality, string Name, string? Quality, string Quantity, string Listings, string Subtotal);

    private sealed record StopView(string Number, string World, string DataCenter, string Detail, LineView[] Lines);

    private readonly record struct MissingView(uint IconId, bool HighQuality, string Name, string? Quality, string Coverage, string Reason);

    private readonly record struct ModeView(PlanMode Mode, string Name, string Total, string Stops, string DataCenters, string Coverage, bool Complete, bool Lowest);

    private readonly record struct FreshnessView(string? DataCenter, string World, string Age, string Items, FreshnessState State);

    private sealed class PlanView
    {
        public required PreviewResult Result { get; init; }

        public required LanguageInfo Language { get; init; }

        public required bool WithNames { get; init; }

        public required int StaleHours { get; init; }

        public required long Bucket { get; init; }

        public required PreviewWarnings Warnings { get; init; }

        public required string StaleWarning { get; init; }

        public required string Total { get; init; }

        public required string Fee { get; init; }

        public required string Stops { get; init; }

        public required string StopsDetail { get; init; }

        public required string DataCenters { get; init; }

        public required string? Partial { get; init; }

        public required StopView[] Route { get; init; }

        public required StopView[] Vendors { get; init; }

        public required MissingView[] Missing { get; init; }

        public required MissingView[] LeftOut { get; init; }

        public required ModeView[] Modes { get; init; }

        public required FreshnessView[] Freshness { get; init; }
    }

    private static PlanView BuildView(ItemCatalog catalog, VendorCatalog vendors, ResolvedScope scope, PreviewResult result, int staleHours, long bucket)
    {
        var now = DateTime.UtcNow;
        var culture = Loc.Culture;
        var plan = result.Selected;
        var totals = plan.Totals;
        return new PlanView
        {
            Result = result,
            Language = Loc.Current,
            WithNames = catalog.IsReady,
            StaleHours = staleHours,
            Bucket = bucket,
            Warnings = WarningsFor(result, now, staleHours),
            StaleWarning = Loc.T(L.Plan.WarnStale, staleHours),
            Total = Formatting.Gil(result.SelectedTotal),
            Fee = Formatting.Gil(totals.FeeEstimate),
            Stops = totals.StopCount.ToString("N0", culture),
            StopsDetail = Loc.Plural(L.Plan.TileStopsSub, totals.WorldChanges),
            DataCenters = totals.DataCenterChanges.ToString("N0", culture),
            Partial = plan.IsComplete ? null : Loc.Plural(L.Plan.Partial, totals.MissingQuantity),
            Route = BuildRoute(catalog, plan, scope),
            Vendors = BuildVendorStops(catalog, vendors, result),
            Missing = BuildMissing(catalog, plan, result.Input.Prices),
            LeftOut = BuildLeftOut(catalog, result),
            Modes = BuildModes(result),
            Freshness = BuildFreshness(result, now, TimeSpan.FromHours(staleHours)),
        };
    }

    private static StopView[] BuildRoute(ItemCatalog catalog, PurchasePlan plan, ResolvedScope scope)
    {
        var culture = Loc.Culture;
        var stops = new StopView[plan.Stops.Length];
        for (var stopIndex = 0; stopIndex < stops.Length; stopIndex++)
        {
            var stop = plan.Stops[stopIndex];
            var lines = BuildStopLines(catalog, stop.Lines);
            var detail = string.Concat(Loc.Plural(L.Plan.StopItems, lines.Length), TextDraw.Separator, Formatting.Gil(stop.PurchaseEstimate));
            stops[stopIndex] = new StopView((stopIndex + 1).ToString(culture), stop.World.Name, DataCenterName(scope, stop.World.WorldId), detail, lines);
        }

        return stops;
    }

    private static PreviewWarnings WarningsFor(PreviewResult result, DateTime nowUtc, int staleHours)
    {
        var warnings = PreviewReview.Evaluate(result, nowUtc, TimeSpan.FromHours(staleHours));
        return result.VendorsOnly ? warnings & PreviewWarnings.UnknownOwned : warnings;
    }

    private static MissingView[] BuildLeftOut(ItemCatalog catalog, PreviewResult result)
    {
        var culture = Loc.Culture;
        var leftOut = new MissingView[result.LeftOut.Length];
        for (var index = 0; index < leftOut.Length; index++)
        {
            var demand = result.LeftOut[index];
            var (iconId, name) = Describe(catalog, demand.ItemId);
            leftOut[index] = new MissingView(
                iconId,
                demand.Quality == DemandQuality.HighQuality,
                name,
                QualityLabel(demand.Quality),
                Loc.T(L.Plan.Quantity, demand.RequiredQuantity.ToString("N0", culture)),
                Loc.T(L.Plan.LeftOutReason));
        }

        return leftOut;
    }

    private static StopView[] BuildVendorStops(ItemCatalog catalog, VendorCatalog vendors, PreviewResult result)
    {
        if (result.VendorLines.IsEmpty || !vendors.IsReady)
        {
            return [];
        }

        var culture = Loc.Culture;
        var route = VendorRoute.Plan(result.VendorLines, vendors, Svc.ClientState.TerritoryType, VendorReachability.CanReach);
        var stops = new StopView[route.Stops.Length];
        for (var stopIndex = 0; stopIndex < stops.Length; stopIndex++)
        {
            var stop = route.Stops[stopIndex];
            var lines = new LineView[stop.Purchases.Length];
            long subtotal = 0;
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = stop.Purchases[lineIndex].Line;
                var (iconId, name) = Describe(catalog, line.Demand.ItemId);
                lines[lineIndex] = new LineView(
                    iconId,
                    false,
                    name,
                    QualityLabel(line.Demand.Quality),
                    Loc.T(L.Plan.Quantity, line.Quantity.ToString("N0", culture)),
                    Loc.T(L.Plan.VendorUnitPrice, Formatting.Gil(line.UnitPrice)),
                    Formatting.Gil(line.Payable));
                subtotal = Core.Shared.Gil.TryAdd(subtotal, line.Payable, out var sum) ? sum : long.MaxValue;
            }

            var detail = string.Concat(Loc.Plural(L.Plan.StopItems, lines.Length), TextDraw.Separator, Formatting.Gil(subtotal));
            stops[stopIndex] = new StopView((stopIndex + 1).ToString(culture), vendors.NpcName(stop.SpotIndex), vendors.PlaceName(stop.SpotIndex), detail, lines);
        }

        return stops;
    }

    // Lines arrive sorted by demand, so the listings of one item at one stop are neighbours.
    private static LineView[] BuildStopLines(ItemCatalog catalog, ImmutableArray<PlanLine> lines)
    {
        var views = new List<LineView>();
        var start = 0;
        while (start < lines.Length)
        {
            var demand = lines[start].Demand;
            var end = start;
            var quantity = 0;
            long payable = 0;
            while (end < lines.Length && lines[end].Demand == demand)
            {
                quantity += lines[end].Offer.Quantity;
                payable += lines[end].Payable;
                end++;
            }

            var (iconId, name) = Describe(catalog, demand.ItemId);
            views.Add(new LineView(
                iconId,
                demand.Quality == DemandQuality.HighQuality,
                name,
                QualityLabel(demand.Quality),
                Loc.T(L.Plan.Quantity, quantity.ToString("N0", Loc.Culture)),
                Loc.Plural(L.Plan.Listings, end - start),
                Formatting.Gil(payable)));
            start = end;
        }

        return [.. views];
    }

    private static MissingView[] BuildMissing(ItemCatalog catalog, PurchasePlan plan, PriceSnapshot? prices)
    {
        var culture = Loc.Culture;
        var missing = new MissingView[plan.Unresolved.Length];
        for (var index = 0; index < missing.Length; index++)
        {
            var entry = plan.Unresolved[index];
            var (iconId, name) = Describe(catalog, entry.Demand.ItemId);
            missing[index] = new MissingView(
                iconId,
                entry.Demand.Quality == DemandQuality.HighQuality,
                name,
                QualityLabel(entry.Demand.Quality),
                Loc.T(L.Plan.CoveredLine, entry.CoveredQuantity.ToString("N0", culture), entry.RequiredQuantity.ToString("N0", culture), entry.MissingQuantity.ToString("N0", culture)),
                ReasonText(PreviewReview.ReasonFor(entry, prices)));
        }

        return missing;
    }

    // Only a complete plan can be the lowest: a partial basket is never shown as the cheaper choice.
    private static ModeView[] BuildModes(PreviewResult result)
    {
        var modes = PreviewRunner.Modes;
        var lowest = -1;
        for (var index = 0; index < modes.Length; index++)
        {
            var plan = result.For(modes[index]);
            if (plan.IsComplete && plan.Totals.StopCount > 0 && (lowest < 0 || plan.Totals.PurchaseEstimate < result.For(modes[lowest]).Totals.PurchaseEstimate))
            {
                lowest = index;
            }
        }

        var culture = Loc.Culture;
        var views = new ModeView[modes.Length];
        for (var index = 0; index < modes.Length; index++)
        {
            var mode = modes[index];
            var plan = result.For(mode);
            var totals = plan.Totals;
            views[index] = new ModeView(
                mode,
                ShoppingSettings.ModeName(Core.Preview.PlanPreview.ToPlanningMode(mode)),
                Formatting.Gil(result.TotalOf(plan)),
                totals.StopCount.ToString("N0", culture),
                totals.DataCenterChanges.ToString("N0", culture),
                plan.IsComplete ? Loc.T(L.Plan.Complete) : Loc.T(L.Plan.MissingUnits, totals.MissingQuantity.ToString("N0", culture)),
                plan.IsComplete,
                index == lowest);
        }

        return views;
    }

    private static FreshnessView[] BuildFreshness(PreviewResult result, DateTime nowUtc, TimeSpan staleAfter)
    {
        var prices = result.Input.Prices;
        if (prices is null)
        {
            return [];
        }

        var itemIds = result.Input.Demands.ItemIds;
        var items = new List<ItemPrices>(itemIds.Length);
        for (var index = 0; index < itemIds.Length; index++)
        {
            if (prices.TryGetItem(itemIds[index], out var item))
            {
                items.Add(item);
            }
        }

        var freshness = PriceSnapshotBuilder.Freshness(prices.Worlds, items);
        var views = new FreshnessView[freshness.Length];
        var dataCenter = uint.MaxValue;
        for (var index = 0; index < views.Length; index++)
        {
            var entry = freshness[index];
            var header = entry.World.DataCenterId == dataCenter ? null : entry.World.DataCenterName;
            dataCenter = entry.World.DataCenterId;
            var failed = (entry.Coverage & PriceCoverageGroups.Failed) != PriceCoverage.None;
            var state = !entry.HasData ? (failed ? FreshnessState.Failed : FreshnessState.NoData)
                : PreviewReview.IsOlderThan(entry.OldestUploadUtc, nowUtc, staleAfter) ? FreshnessState.Stale
                : FreshnessState.Fresh;
            var age = entry.HasData ? Formatting.RelativeTime(entry.OldestUploadUtc, nowUtc) : Loc.T(failed ? L.Plan.RequestFailed : L.Plan.NoUpload);
            var counted = entry.ItemsWithData + entry.ItemsWithoutData;
            var capped = (entry.Coverage & PriceCoverage.Truncated) != PriceCoverage.None;
            var itemsText = Loc.T(capped ? L.Plan.FreshnessCapped : L.Plan.FreshnessItems, entry.ItemsWithData, counted);
            views[index] = new FreshnessView(header, entry.World.Name, age, itemsText, state);
        }

        return views;
    }

    private static string DataCenterName(ResolvedScope scope, uint worldId)
    {
        var index = PriceSnapshot.IndexOfWorld(scope.Candidates, worldId);
        return index < 0 ? string.Empty : scope.Candidates[index].DataCenterName;
    }

    private static (uint IconId, string Name) Describe(ItemCatalog catalog, uint itemId)
    {
        return catalog.TryGet(itemId, out var item)
            ? (item.IconId, item.Name)
            : (0u, Loc.T(L.Lists.ItemFallback, itemId));
    }

    private static string? QualityLabel(DemandQuality quality) => quality switch
    {
        DemandQuality.HighQuality => Loc.T(L.Lists.QualityHigh),
        DemandQuality.NormalQuality => Loc.T(L.Lists.QualityNormal),
        _ => null,
    };

    private static string ReasonText(PreviewReason reason) => reason switch
    {
        PreviewReason.InvalidDemand => Loc.T(L.Plan.ReasonInvalid),
        PreviewReason.AboveUnitCap => Loc.T(L.Plan.ReasonAboveCap),
        PreviewReason.InsufficientStock => Loc.T(L.Plan.ReasonInsufficient),
        PreviewReason.NoFittingStacks => Loc.T(L.Plan.ReasonNoFit),
        PreviewReason.TakenByStricterDemand => Loc.T(L.Plan.ReasonStricter),
        PreviewReason.StopLimit => Loc.T(L.Plan.ReasonStopLimit),
        PreviewReason.SearchLimit => Loc.T(L.Plan.ReasonSearchLimit),
        PreviewReason.NotFetched => Loc.T(L.Plan.ReasonNotFetched),
        PreviewReason.NotMarketable => Loc.T(L.Plan.ReasonNotMarketable),
        PreviewReason.RequestFailed => Loc.T(L.Plan.ReasonRequestFailed),
        PreviewReason.NoMarketData => Loc.T(L.Plan.ReasonNoMarketData),
        PreviewReason.Capped => Loc.T(L.Plan.ReasonCapped),
        _ => Loc.T(L.Plan.ReasonNoListings),
    };
}
