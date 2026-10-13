using System.Collections.Immutable;

namespace AutoShopper.Core.Execution.Model;

internal readonly record struct UnresolvedAttempt(
    Guid RunId,
    Guid AttemptId,
    bool HasIntent,
    uint ItemId,
    bool IsHq,
    uint WorldId,
    int Quantity,
    long Payable,
    DateTime AtUtc,
    long GilBefore,
    int OwnedBefore);

// Every run the journal holds, replayed one by one. Buying stays blocked while any run has an attempt without an
// outcome or a damaged entry; a run that was authorized and never ended was interrupted and may be resumed.
internal sealed class JournalReview
{
    public static readonly JournalReview Empty = new() { Runs = [], Unresolved = [] };

    public required ImmutableArray<JournalProgress> Runs { get; init; }

    public required ImmutableArray<UnresolvedAttempt> Unresolved { get; init; }

    public bool NeedsReconciliation
    {
        get
        {
            for (var index = 0; index < Runs.Length; index++)
            {
                if (Runs[index].NeedsReconciliation)
                {
                    return true;
                }
            }

            return false;
        }
    }

    // Damage no answer about an attempt can repair: an unreadable line in the middle, an outcome without an intent, an
    // event of a run that was never authorized. Conflicting outcomes are left to the answer, which overrides them.
    public bool IsDamaged
    {
        get
        {
            for (var index = 0; index < Runs.Length; index++)
            {
                var anomalies = Runs[index].Anomalies;
                for (var anomalyIndex = 0; anomalyIndex < anomalies.Length; anomalyIndex++)
                {
                    if (anomalies[anomalyIndex].BlocksBuying && anomalies[anomalyIndex].Kind != JournalAnomalyKind.ConflictingOutcome)
                    {
                        return true;
                    }
                }
            }

            for (var index = 0; index < Unresolved.Length; index++)
            {
                if (!Unresolved[index].HasIntent)
                {
                    return true;
                }
            }

            return false;
        }
    }

    // The newest run that was authorized, never ended and has nothing to reconcile.
    public JournalProgress? Interrupted
    {
        get
        {
            for (var index = Runs.Length - 1; index >= 0; index--)
            {
                var run = Runs[index];
                if (run.Authorization is not null && run.FinalState is null && !run.NeedsReconciliation)
                {
                    return run;
                }
            }

            return null;
        }
    }

    public static JournalReview Build(IReadOnlyList<JournalEvent?> entries)
    {
        var runIds = new List<Guid>();
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index] is RunAuthorized authorized && authorized.IsWellFormed() && !runIds.Contains(authorized.RunId))
            {
                runIds.Add(authorized.RunId);
            }
        }

        if (runIds.Count == 0 && entries.Count > 0)
        {
            runIds.Add(Guid.Empty);
        }

        var runs = ImmutableArray.CreateBuilder<JournalProgress>(runIds.Count);
        var unresolved = ImmutableArray.CreateBuilder<UnresolvedAttempt>();
        for (var index = 0; index < runIds.Count; index++)
        {
            var progress = JournalReplayer.Replay(entries, runIds[index]);
            runs.Add(progress);
            CollectUnresolved(progress, entries, unresolved);
        }

        return new JournalReview { Runs = runs.ToImmutable(), Unresolved = unresolved.ToImmutable() };
    }

    private static void CollectUnresolved(JournalProgress progress, IReadOnlyList<JournalEvent?> entries, ImmutableArray<UnresolvedAttempt>.Builder unresolved)
    {
        var attempts = progress.Attempts;
        for (var index = 0; index < attempts.Length; index++)
        {
            var attempt = attempts[index];
            if (attempt.Status != AttemptStatus.Unknown)
            {
                continue;
            }

            var intent = FindIntent(entries, progress.RunId, attempt.AttemptId);
            unresolved.Add(new UnresolvedAttempt(
                progress.RunId,
                attempt.AttemptId,
                intent is not null,
                intent?.ItemId ?? 0,
                intent?.IsHq ?? false,
                intent?.WorldId ?? 0,
                intent?.Quantity ?? 0,
                intent?.Payable ?? 0,
                intent?.AtUtc ?? default,
                intent?.GilBefore ?? -1,
                intent?.OwnedBefore ?? -1));
        }
    }

    private static IntentRecorded? FindIntent(IReadOnlyList<JournalEvent?> entries, Guid runId, Guid attemptId)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index] is IntentRecorded intent && intent.IsWellFormed() && intent.RunId == runId && intent.AttemptId == attemptId)
            {
                return intent;
            }
        }

        return null;
    }
}
