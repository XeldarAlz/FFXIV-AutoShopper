using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Threading;
using AutoShopper.Core.Planning;

namespace AutoShopper.Core.Preview.Model;

internal static class PreviewRunner
{
    private const string SourceLabel = "Universalis";

    public static readonly PlanMode[] Modes = [PlanMode.Cheapest, PlanMode.Balanced, PlanMode.FewestStops];

    // The three modes run one after another, each inside the planner's own time budget, so the comparison is made on
    // exactly the same demand, offers and worlds.
    public static PreviewResult Run(PreviewInput input, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var planner = new Planner(timeProvider);
        var offers = input.Prices?.Offers ?? ImmutableArray<PlanOffer>.Empty;
        var plans = new PurchasePlan[Modes.Length];
        for (var index = 0; index < Modes.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mode = Modes[index];
            var request = new PlanRequest
            {
                Demands = input.Demands.Demands,
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
        };
    }
}
