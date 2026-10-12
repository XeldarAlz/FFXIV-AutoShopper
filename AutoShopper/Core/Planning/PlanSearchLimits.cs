namespace AutoShopper.Core.Planning;

[Flags]
internal enum PlanSearchLimits : ushort
{
    None = 0,
    OffersPruned = 1 << 0,
    HeuristicSearch = 1 << 1,
    TimeBudgetReached = 1 << 2,
    EvaluationLimitReached = 1 << 3,
    CandidateWorldsTruncated = 1 << 4,
    GreedyAllocation = 1 << 5,
    InvalidOffersDropped = 1 << 6,
    ArithmeticOverflow = 1 << 7,
}

internal static class PlanSearchLimitGroups
{
    public const PlanSearchLimits SearchCut =
        PlanSearchLimits.OffersPruned
        | PlanSearchLimits.TimeBudgetReached
        | PlanSearchLimits.EvaluationLimitReached
        | PlanSearchLimits.CandidateWorldsTruncated
        | PlanSearchLimits.GreedyAllocation;

    public const PlanSearchLimits NotProvenOptimal = SearchCut | PlanSearchLimits.HeuristicSearch;
}
