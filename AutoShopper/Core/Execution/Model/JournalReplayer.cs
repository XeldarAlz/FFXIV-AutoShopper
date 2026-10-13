using System.Collections.Immutable;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Shared;

namespace AutoShopper.Core.Execution.Model;

internal static class JournalReplayer
{
    public static JournalProgress Replay(IReadOnlyList<JournalEvent?> entries)
    {
        return Replay(entries, LatestAuthorizedRun(entries));
    }

    public static JournalProgress Replay(IReadOnlyList<JournalEvent?> entries, Guid runId)
    {
        var replay = new ReplayAccumulator(runId);
        var accepted = Screen(entries, runId, replay);
        accepted.Sort(static (left, right) =>
        {
            var order = left.Event.Sequence.CompareTo(right.Event.Sequence);
            return order != 0 ? order : left.Position.CompareTo(right.Position);
        });

        var seen = new HashSet<(long Sequence, string Kind, Guid AttemptId)>();
        for (var index = 0; index < accepted.Count; index++)
        {
            var entry = accepted[index];
            var journalEvent = entry.Event;
            var attemptId = journalEvent is AttemptEvent attemptEvent ? attemptEvent.AttemptId : Guid.Empty;
            if (!seen.Add((journalEvent.Sequence, journalEvent.Kind, attemptId)))
            {
                replay.Report(JournalAnomalyKind.DuplicateEvent, entry.Position, journalEvent.Sequence, attemptId);
                continue;
            }

            replay.Apply(journalEvent, entry.Position);
        }

        return replay.Build();
    }

    private static Guid LatestAuthorizedRun(IReadOnlyList<JournalEvent?> entries)
    {
        for (var position = entries.Count - 1; position >= 0; position--)
        {
            if (entries[position] is RunAuthorized authorized && authorized.IsWellFormed())
            {
                return authorized.RunId;
            }
        }

        return Guid.Empty;
    }

    private static List<PositionedEvent> Screen(IReadOnlyList<JournalEvent?> entries, Guid runId, ReplayAccumulator replay)
    {
        var authorizedRuns = new HashSet<Guid>();
        for (var position = 0; position < entries.Count; position++)
        {
            if (entries[position] is RunAuthorized authorized && authorized.IsWellFormed())
            {
                authorizedRuns.Add(authorized.RunId);
            }
        }

        var accepted = new List<PositionedEvent>(entries.Count);
        var lastPosition = entries.Count - 1;
        for (var position = 0; position < entries.Count; position++)
        {
            var entry = entries[position];
            if (entry is null || !entry.IsWellFormed())
            {
                var kind = position == lastPosition ? JournalAnomalyKind.TornTail : JournalAnomalyKind.CorruptEntry;
                replay.Report(kind, position, entry?.Sequence ?? 0, Guid.Empty);
                continue;
            }

            if (entry.RunId != runId)
            {
                if (!authorizedRuns.Contains(entry.RunId))
                {
                    replay.Report(JournalAnomalyKind.UnauthorizedRun, position, entry.Sequence, Guid.Empty);
                }

                continue;
            }

            accepted.Add(new PositionedEvent(entry, position));
        }

        return accepted;
    }

    private readonly record struct PositionedEvent(JournalEvent Event, int Position);

    private sealed class AttemptBuilder(Guid attemptId, long firstSequence, int firstPosition)
    {
        public Guid AttemptId { get; } = attemptId;

        public long FirstSequence { get; } = firstSequence;

        public int FirstPosition { get; } = firstPosition;

        public IntentRecorded? Intent { get; set; }

        public bool IntentConflict { get; set; }

        public bool Submitted { get; set; }

        public bool Sent { get; set; }

        public PurchaseConfirmed? Confirmation { get; set; }

        public bool ConfirmationConflict { get; set; }

        public bool Rejected { get; set; }

        public bool ReportedUnknown { get; set; }

        public long UnknownCharge { get; set; } = -1;

        public Reconciled? Reconciliation { get; set; }
    }

    private sealed class ReplayAccumulator(Guid runId)
    {
        private readonly List<JournalAnomaly> anomalies = new();
        private readonly List<AttemptBuilder> attempts = new();
        private readonly Dictionary<Guid, AttemptBuilder> attemptsById = new();
        private readonly HashSet<string> excludedListingKeys = new(StringComparer.Ordinal);
        private readonly HashSet<uint> completedWorldIds = new();
        private readonly HashSet<uint> excludedWorldIds = new();
        private RunAuthorization? authorization;
        private bool authorizationSeen;
        private Checkpoint? lastCheckpoint;
        private RunEnded? ended;
        private int checkpointAttemptCount;
        private int checkpointFailedCount;
        private int checkpointRevisits;
        private long lastSequence;
        private bool tornTail;

        public void Report(JournalAnomalyKind kind, int position, long sequence, Guid attemptId)
        {
            if (kind == JournalAnomalyKind.TornTail)
            {
                tornTail = true;
            }

            anomalies.Add(new JournalAnomaly(kind, position, sequence, attemptId));
        }

        public void Apply(JournalEvent journalEvent, int position)
        {
            lastSequence = Math.Max(lastSequence, journalEvent.Sequence);
            switch (journalEvent)
            {
                case RunAuthorized authorized:
                    ApplyAuthorization(authorized, position);
                    return;
                case Checkpoint checkpoint:
                    ApplyCheckpoint(checkpoint);
                    return;
                case RunEnded runEnded:
                    ended = runEnded;
                    return;
                case AttemptEvent attemptEvent:
                    ApplyAttemptEvent(attemptEvent, position);
                    return;
            }
        }

        private void ApplyAuthorization(RunAuthorized authorized, int position)
        {
            if (authorizationSeen)
            {
                Report(JournalAnomalyKind.DuplicateAuthorization, position, authorized.Sequence, Guid.Empty);
                return;
            }

            authorizationSeen = true;
            if (RunAuthorization.TryRestore(authorized, out var restored) != AuthorizationProblem.None)
            {
                Report(JournalAnomalyKind.InvalidAuthorization, position, authorized.Sequence, Guid.Empty);
                return;
            }

            authorization = restored;
        }

        private void ApplyCheckpoint(Checkpoint checkpoint)
        {
            lastCheckpoint = checkpoint;
            checkpointAttemptCount = Math.Max(checkpointAttemptCount, checkpoint.AttemptCount);
            checkpointFailedCount = Math.Max(checkpointFailedCount, checkpoint.FailedAttemptCount);
            checkpointRevisits = Math.Max(checkpointRevisits, checkpoint.Revisits);
            for (var index = 0; index < checkpoint.ExcludedListingKeys.Length; index++)
            {
                var key = checkpoint.ExcludedListingKeys[index];
                if (!string.IsNullOrEmpty(key))
                {
                    excludedListingKeys.Add(key);
                }
            }

            for (var index = 0; index < checkpoint.CompletedWorldIds.Length; index++)
            {
                completedWorldIds.Add(checkpoint.CompletedWorldIds[index]);
            }

            for (var index = 0; index < checkpoint.ExcludedWorldIds.Length; index++)
            {
                excludedWorldIds.Add(checkpoint.ExcludedWorldIds[index]);
            }
        }

        private void ApplyAttemptEvent(AttemptEvent attemptEvent, int position)
        {
            if (!attemptsById.TryGetValue(attemptEvent.AttemptId, out var attempt))
            {
                attempt = new AttemptBuilder(attemptEvent.AttemptId, attemptEvent.Sequence, position);
                attemptsById.Add(attemptEvent.AttemptId, attempt);
                attempts.Add(attempt);
            }

            switch (attemptEvent)
            {
                case IntentRecorded intent:
                    if (attempt.Intent is null)
                    {
                        attempt.Intent = intent;
                    }
                    else if (!SameIntent(attempt.Intent, intent))
                    {
                        attempt.IntentConflict = true;
                    }

                    return;
                case PurchaseSubmitted submitted:
                    attempt.Submitted = true;
                    attempt.Sent |= submitted.Sent;
                    return;
                case PurchaseConfirmed confirmed:
                    if (attempt.Confirmation is null)
                    {
                        attempt.Confirmation = confirmed;
                    }
                    else if (attempt.Confirmation.Quantity != confirmed.Quantity || attempt.Confirmation.Payable != confirmed.Payable)
                    {
                        attempt.ConfirmationConflict = true;
                    }

                    return;
                case PurchaseRejected:
                    attempt.Rejected = true;
                    return;
                case PurchaseUnknown unknown:
                    attempt.ReportedUnknown = true;
                    attempt.UnknownCharge = Math.Max(attempt.UnknownCharge, unknown.Charged);
                    return;
                case Reconciled reconciled:
                    attempt.Reconciliation = reconciled;
                    return;
            }
        }

        private static bool SameIntent(IntentRecorded left, IntentRecorded right)
        {
            return left.Demand == right.Demand
                && left.ListingId == right.ListingId
                && left.Quantity == right.Quantity
                && left.Payable == right.Payable
                && left.WorldId == right.WorldId;
        }

        public JournalProgress Build()
        {
            var demands = authorization is null ? ImmutableArray<DemandTally>.Empty : CreateRows(authorization);
            var records = ImmutableArray.CreateBuilder<AttemptRecord>(attempts.Count);
            var spendOverflowed = false;
            long confirmedSpend = 0;
            long reservedSpend = 0;
            var intentCount = 0;
            var failedCount = 0;
            for (var index = 0; index < attempts.Count; index++)
            {
                var builder = attempts[index];
                var record = Resolve(builder);
                records.Add(record);
                if (record.HasIntent)
                {
                    intentCount++;
                }

                switch (record.Status)
                {
                    case AttemptStatus.Confirmed:
                        confirmedSpend = AddChecked(confirmedSpend, record.ChargedAmount, ref spendOverflowed);
                        break;
                    case AttemptStatus.Unknown:
                        reservedSpend = AddChecked(reservedSpend, record.ReservedAmount, ref spendOverflowed);
                        break;
                    default:
                        failedCount++;
                        break;
                }

                if (record.HasIntent && !Tally(demands, record))
                {
                    Report(JournalAnomalyKind.UnauthorizedDemand, builder.FirstPosition, builder.FirstSequence, builder.AttemptId);
                }
            }

            if (spendOverflowed)
            {
                Report(JournalAnomalyKind.SpendOverflow, -1, lastSequence, Guid.Empty);
            }

            return new JournalProgress
            {
                RunId = runId,
                Authorization = authorization,
                Demands = ToDemandProgress(demands),
                Attempts = records.MoveToImmutable(),
                ConfirmedSpend = confirmedSpend,
                ReservedSpend = reservedSpend,
                AttemptCount = Math.Max(intentCount, checkpointAttemptCount),
                FailedAttemptCount = Math.Max(failedCount, checkpointFailedCount),
                Revisits = checkpointRevisits,
                CompletedWorldIds = Sorted(completedWorldIds),
                ExcludedWorldIds = Sorted(excludedWorldIds),
                ExcludedListingKeys = SortedKeys(),
                LastCheckpointState = lastCheckpoint?.State,
                FinalState = ended?.FinalState,
                LastSequence = lastSequence,
                TornTail = tornTail,
                Anomalies = [.. anomalies],
            };
        }

        private AttemptRecord Resolve(AttemptBuilder builder)
        {
            var intent = builder.Intent;
            var status = AttemptStatus.Unknown;
            var confirmedQuantity = 0;
            long charged = 0;
            if (intent is null)
            {
                Report(JournalAnomalyKind.OrphanOutcome, builder.FirstPosition, builder.FirstSequence, builder.AttemptId);
            }
            else if (builder.Reconciliation is { } reconciliation)
            {
                status = reconciliation.Purchased ? AttemptStatus.Confirmed : AttemptStatus.Rejected;
                confirmedQuantity = reconciliation.Purchased ? reconciliation.Quantity : 0;
                charged = reconciliation.Purchased ? reconciliation.Payable : 0;
            }
            else if (HasConflict(builder, intent))
            {
                Report(JournalAnomalyKind.ConflictingOutcome, builder.FirstPosition, builder.FirstSequence, builder.AttemptId);
            }
            else if (builder.Confirmation is { } confirmation)
            {
                status = AttemptStatus.Confirmed;
                confirmedQuantity = confirmation.Quantity;
                charged = confirmation.Payable;
            }
            else if (builder.Rejected)
            {
                status = AttemptStatus.Rejected;
            }
            else if (builder.Submitted && !builder.Sent && !builder.ReportedUnknown)
            {
                status = AttemptStatus.NotSent;
            }

            // An uncertain attempt holds no less gil than the purse was seen to lose across it.
            var reserved = intent?.Payable ?? 0;
            if (status == AttemptStatus.Unknown && intent is not null)
            {
                reserved = Math.Max(reserved, builder.UnknownCharge);
            }

            return new AttemptRecord(
                builder.AttemptId,
                builder.FirstSequence,
                intent is not null,
                intent?.Demand ?? default,
                intent?.ListingId ?? 0,
                intent?.WorldId ?? 0,
                intent?.Quantity ?? 0,
                reserved,
                status,
                confirmedQuantity,
                charged);
        }

        private static bool HasConflict(AttemptBuilder builder, IntentRecorded intent)
        {
            if (builder.IntentConflict || builder.ConfirmationConflict)
            {
                return true;
            }

            if (builder.Confirmation is null)
            {
                return false;
            }

            return builder.Rejected || builder.Confirmation.Quantity != intent.Quantity;
        }

        private static long AddChecked(long total, long amount, ref bool overflowed)
        {
            if (Gil.TryAdd(total, amount, out var sum))
            {
                return sum;
            }

            overflowed = true;
            return long.MaxValue;
        }

        private static ImmutableArray<DemandTally> CreateRows(RunAuthorization source)
        {
            var rows = ImmutableArray.CreateBuilder<DemandTally>(source.Demands.Length);
            for (var index = 0; index < source.Demands.Length; index++)
            {
                rows.Add(new DemandTally(source.Demands[index]));
            }

            return rows.MoveToImmutable();
        }

        private static bool Tally(ImmutableArray<DemandTally> rows, AttemptRecord record)
        {
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                if (row.Demand.Key != record.Demand)
                {
                    continue;
                }

                switch (record.Status)
                {
                    case AttemptStatus.Confirmed:
                        row.ConfirmedQuantity += record.ConfirmedQuantity;
                        break;
                    case AttemptStatus.Unknown:
                        row.UnknownQuantity += record.Quantity;
                        break;
                    default:
                        row.FailedAttempts++;
                        break;
                }

                return true;
            }

            return false;
        }

        private static ImmutableArray<DemandProgress> ToDemandProgress(ImmutableArray<DemandTally> rows)
        {
            var progress = ImmutableArray.CreateBuilder<DemandProgress>(rows.Length);
            for (var index = 0; index < rows.Length; index++)
            {
                var row = rows[index];
                progress.Add(new DemandProgress(row.Demand, row.ConfirmedQuantity, row.UnknownQuantity, row.FailedAttempts));
            }

            return progress.MoveToImmutable();
        }

        private static ImmutableArray<uint> Sorted(HashSet<uint> worldIds)
        {
            var worlds = new uint[worldIds.Count];
            worldIds.CopyTo(worlds);
            Array.Sort(worlds);
            return [.. worlds];
        }

        private ImmutableArray<string> SortedKeys()
        {
            var keys = new string[excludedListingKeys.Count];
            excludedListingKeys.CopyTo(keys);
            Array.Sort(keys, StringComparer.Ordinal);
            return [.. keys];
        }
    }

    private sealed class DemandTally(PlanDemand demand)
    {
        public PlanDemand Demand { get; } = demand;

        public int ConfirmedQuantity { get; set; }

        public int UnknownQuantity { get; set; }

        public int FailedAttempts { get; set; }
    }
}
