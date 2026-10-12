using System.Collections.Immutable;

namespace AutoShopper.Core.Planning;

internal sealed record PurchasePlan
{
    public required int Version { get; init; }

    public required PlanMode Mode { get; init; }

    public required PlanProvenance Provenance { get; init; }

    public required ImmutableArray<PlanStop> Stops { get; init; }

    public required ImmutableArray<UnresolvedDemand> Unresolved { get; init; }

    public required PlanTotals Totals { get; init; }

    public required PlanSearchLimits Limits { get; init; }

    public required long CheapestBaselineEstimate { get; init; }

    public required bool BaselineIsPartial { get; init; }

    public required PlanRequest Request { get; init; }

    public required ImmutableArray<PlanDemand> AuthorizedDemands { get; init; }

    public bool IsComplete => Unresolved.IsDefaultOrEmpty;

    public bool SearchLimitReached => (Limits & PlanSearchLimitGroups.SearchCut) != PlanSearchLimits.None;

    public bool IsProvenOptimal => (Limits & PlanSearchLimitGroups.NotProvenOptimal) == PlanSearchLimits.None;
}
