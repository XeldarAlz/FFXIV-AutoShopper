using System.Collections.Immutable;
using AutoShopper.Core.Planning;

namespace AutoShopper.Core.Execution.Model;

internal enum AttemptStatus : byte
{
    Unknown = 0,
    Confirmed = 1,
    Rejected = 2,
    NotSent = 3,
}

internal sealed record AttemptRecord(
    Guid AttemptId,
    long FirstSequence,
    bool HasIntent,
    DemandKey Demand,
    ulong ListingId,
    uint WorldId,
    int Quantity,
    long ReservedAmount,
    AttemptStatus Status,
    int ConfirmedQuantity,
    long ChargedAmount);

internal readonly record struct DemandProgress(PlanDemand Demand, int ConfirmedQuantity, int UnknownQuantity, int FailedAttempts)
{
    public PlanDemand Outstanding => Demand.ReducedBy(ConfirmedQuantity);
}

internal enum JournalAnomalyKind : byte
{
    TornTail = 0,
    DuplicateEvent = 1,
    CorruptEntry = 2,
    UnauthorizedRun = 3,
    InvalidAuthorization = 4,
    DuplicateAuthorization = 5,
    OrphanOutcome = 6,
    ConflictingOutcome = 7,
    UnauthorizedDemand = 8,
    SpendOverflow = 9,
}

internal readonly record struct JournalAnomaly(JournalAnomalyKind Kind, int Position, long Sequence, Guid AttemptId)
{
    public bool BlocksBuying => Kind is not (JournalAnomalyKind.TornTail or JournalAnomalyKind.DuplicateEvent);
}

internal sealed class JournalProgress
{
    public required Guid RunId { get; init; }

    public required RunAuthorization? Authorization { get; init; }

    public required ImmutableArray<DemandProgress> Demands { get; init; }

    public required ImmutableArray<AttemptRecord> Attempts { get; init; }

    public required long ConfirmedSpend { get; init; }

    public required long ReservedSpend { get; init; }

    public required int AttemptCount { get; init; }

    public required int FailedAttemptCount { get; init; }

    public required ImmutableArray<uint> CompletedWorldIds { get; init; }

    public required ImmutableArray<uint> ExcludedWorldIds { get; init; }

    public required ImmutableArray<string> ExcludedListingKeys { get; init; }

    public required RunState? LastCheckpointState { get; init; }

    public required RunState? FinalState { get; init; }

    public required long LastSequence { get; init; }

    public required bool TornTail { get; init; }

    public required ImmutableArray<JournalAnomaly> Anomalies { get; init; }

    public bool HasUnknownAttempts
    {
        get
        {
            for (var index = 0; index < Attempts.Length; index++)
            {
                if (Attempts[index].Status == AttemptStatus.Unknown)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public bool NeedsReconciliation
    {
        get
        {
            if (HasUnknownAttempts)
            {
                return true;
            }

            for (var index = 0; index < Anomalies.Length; index++)
            {
                if (Anomalies[index].BlocksBuying)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public RunState RecoveredState
    {
        get
        {
            if (NeedsReconciliation)
            {
                return RunState.NeedsReconciliation;
            }

            if (FinalState is { } final)
            {
                return final;
            }

            return Authorization is null ? RunState.Idle : RunState.Paused;
        }
    }

    public ImmutableArray<ConfirmedPurchase> ConfirmedPurchases()
    {
        var purchases = ImmutableArray.CreateBuilder<ConfirmedPurchase>();
        for (var index = 0; index < Attempts.Length; index++)
        {
            var attempt = Attempts[index];
            if (attempt.Status != AttemptStatus.Confirmed || !attempt.HasIntent)
            {
                continue;
            }

            purchases.Add(new ConfirmedPurchase(attempt.Demand, ListingKeys.ForGameListing(attempt.ListingId), attempt.ConfirmedQuantity, attempt.ChargedAmount));
        }

        return purchases.ToImmutable();
    }

    public SpendBook ToSpendBook()
    {
        var pendingAttemptId = Guid.Empty;
        var settled = new List<Guid>(Attempts.Length);
        for (var index = 0; index < Attempts.Length; index++)
        {
            var attempt = Attempts[index];
            if (attempt.Status != AttemptStatus.Unknown)
            {
                settled.Add(attempt.AttemptId);
                continue;
            }

            if (pendingAttemptId == Guid.Empty)
            {
                pendingAttemptId = attempt.AttemptId;
            }
        }

        var maxSpend = Authorization?.MaxSpend ?? 0;
        return SpendBook.Restore(maxSpend, ConfirmedSpend, pendingAttemptId, ReservedSpend, settled.ToArray());
    }
}
