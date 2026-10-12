namespace AutoShopper.Core.Planning;

internal sealed record PlanSettings
{
    public const int DefaultExactWorldLimit = 12;
    public const int DefaultOffersPerWorldLimit = 20;
    public const int DefaultEvaluationLimit = 50_000;

    // Exhaustive subset search doubles per world; past 20 worlds it is a million subsets, so the heuristic takes over.
    public const int HighestExactWorldLimit = 20;

    public static readonly TimeSpan DefaultSearchTimeBudget = TimeSpan.FromSeconds(2);

    public PlanMode Mode { get; init; } = PlanMode.Cheapest;

    public long StopPenaltyGil { get; init; }

    public long CrossDataCenterPenaltyGil { get; init; }

    public int FewestStopsPremiumPercent { get; init; }

    public int MaxStops { get; init; }

    public required PlanWorld CurrentWorld { get; init; }

    public required PlanWorld HomeWorld { get; init; }

    public bool ReturnHome { get; init; }

    public TimeSpan SearchTimeBudget { get; init; } = DefaultSearchTimeBudget;

    public int SearchEvaluationLimit { get; init; } = DefaultEvaluationLimit;

    public int ExactWorldLimit { get; init; } = DefaultExactWorldLimit;

    public int OffersPerWorldLimit { get; init; } = DefaultOffersPerWorldLimit;

    public long EffectiveStopPenalty => Math.Max(0, StopPenaltyGil);

    public long EffectiveCrossDataCenterPenalty => Math.Max(0, CrossDataCenterPenaltyGil);

    public int EffectivePremiumPercent => Math.Max(0, FewestStopsPremiumPercent);

    public int EffectiveExactWorldLimit => Math.Clamp(ExactWorldLimit, 0, HighestExactWorldLimit);

    public int EffectiveOffersPerWorldLimit => OffersPerWorldLimit > 0 ? OffersPerWorldLimit : DefaultOffersPerWorldLimit;

    public int EffectiveEvaluationLimit => SearchEvaluationLimit > 0 ? SearchEvaluationLimit : DefaultEvaluationLimit;

    public TimeSpan EffectiveTimeBudget => SearchTimeBudget > TimeSpan.Zero ? SearchTimeBudget : DefaultSearchTimeBudget;

    public int StopLimitFor(int candidateCount)
    {
        if (MaxStops <= 0)
        {
            return candidateCount;
        }

        return Math.Min(MaxStops, candidateCount);
    }
}
