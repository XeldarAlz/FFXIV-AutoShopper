namespace AutoShopper.Core.Planning;

internal readonly record struct PlanProvenance(
    string SourceLabel,
    DateTime PlannedAtUtc,
    DateTime OldestObservationUtc,
    DateTime NewestObservationUtc,
    int OfferCount,
    int UsableOfferCount,
    int EstimatedLineCount,
    int CandidateWorldCount,
    int Evaluations);
