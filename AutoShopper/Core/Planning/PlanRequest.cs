using System.Collections.Immutable;

namespace AutoShopper.Core.Planning;

internal sealed record PlanRequest
{
    public required ImmutableArray<PlanDemand> Demands { get; init; }

    public required ImmutableArray<PlanOffer> Offers { get; init; }

    public required ImmutableArray<PlanWorld> Worlds { get; init; }

    public required PlanSettings Settings { get; init; }

    public ImmutableArray<string> ExcludedListingKeys { get; init; } = ImmutableArray<string>.Empty;

    public int PlanVersion { get; init; } = 1;

    public string SourceLabel { get; init; } = string.Empty;
}
