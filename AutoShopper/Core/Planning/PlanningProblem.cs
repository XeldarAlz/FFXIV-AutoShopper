namespace AutoShopper.Core.Planning;

internal readonly record struct LineScope(long MatchingQuantity, long WithinCapQuantity);

internal sealed class ItemProblem
{
    public required uint ItemId { get; init; }

    public required PlanDemand[] Lines { get; init; }

    public required LineScope[] Scopes { get; init; }

    public required PlanOffer[] Offers { get; init; }

    public required long[] Payables { get; init; }

    public required int[] WorldIndexes { get; init; }

    public required bool[][] Eligible { get; init; }

    public required UInt128 WorldMask { get; init; }
}

internal sealed class PlanningProblem
{
    public required PlanWorld[] Candidates { get; init; }

    public required ItemProblem[] Items { get; init; }

    public required int CurrentWorldIndex { get; init; }

    public required PlanSearchLimits Limits { get; init; }

    public required int UsableOfferCount { get; init; }
}
