using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Threading;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Vendors.Model;

namespace AutoShopper.Core.Preview.Model;

internal static class PreviewRunner
{
    public const string SourceLabel = "Universalis";

    public static readonly PlanMode[] Modes = [PlanMode.Cheapest, PlanMode.Balanced, PlanMode.FewestStops];

    // The three modes run one after another, each inside the planner's own time budget, so the comparison is made on
    // exactly the same demand, offers and worlds.
    public static PreviewResult Run(PreviewInput input, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var planner = new Planner(timeProvider);
        var offers = input.Prices?.Offers ?? ImmutableArray<PlanOffer>.Empty;
        var split = Split(input, offers);
        var vendorsOnly = input.Source == BuySource.NpcVendors;
        var marketDemands = vendorsOnly ? ImmutableArray<PlanDemand>.Empty : split.Market;
        var plans = new PurchasePlan[Modes.Length];
        for (var index = 0; index < Modes.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mode = Modes[index];
            var request = new PlanRequest
            {
                Demands = marketDemands,
                Offers = offers,
                Worlds = input.Worlds,
                Settings = input.Settings with { Mode = mode },
                SourceLabel = SourceLabel,
            };
            plans[(int)mode] = planner.Plan(request, cancellationToken);
        }

        return new PreviewResult
        {
            Input = input,
            Plans = ImmutableCollectionsMarshal.AsImmutableArray(plans),
            PlannedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            VendorLines = split.Vendor,
            VendorTotal = split.VendorTotal,
            LeftOut = vendorsOnly ? split.Market : ImmutableArray<PlanDemand>.Empty,
        };
    }

    private static VendorSplitResult Split(PreviewInput input, ImmutableArray<PlanOffer> offers) => input.Source switch
    {
        BuySource.MarketBoard => VendorSplitResult.MarketOnly(input.Demands.Demands),
        BuySource.NpcVendors => VendorSplit.Split(input.Demands.Demands, ImmutableArray<PlanOffer>.Empty, input.Vendors),
        _ => input.Prices is null ? VendorSplitResult.MarketOnly(input.Demands.Demands) : VendorSplit.Split(input.Demands.Demands, offers, input.Vendors),
    };
}
