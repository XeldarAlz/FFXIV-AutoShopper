using System.Collections.Immutable;

namespace AutoShopper.Core.Planning;

internal readonly record struct LineOutcome(
    int[] ChosenOffers,
    int PlannedQuantity,
    int MissingQuantity,
    long Payable,
    long Fee,
    bool LimitedByStricterDemand);

internal sealed class ItemOutcome
{
    public required LineOutcome[] Lines { get; init; }

    public required long Payable { get; init; }

    public required long Fee { get; init; }

    public required long MissingQuantity { get; init; }

    public required UInt128 UsedWorlds { get; init; }

    public required bool Overflowed { get; init; }

    public required bool UsedGreedyFill { get; init; }
}

internal sealed record LineAllocation(PlanDemand Demand, ImmutableArray<PlanOffer> Offers, int PlannedQuantity, int MissingQuantity, long Payable);
