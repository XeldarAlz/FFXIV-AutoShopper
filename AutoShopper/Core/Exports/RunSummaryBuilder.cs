using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Shared;

namespace AutoShopper.Core.Exports;

// The replayed progress decides every status and amount; the raw entries only add what progress does not keep (the
// bought quality, the rejection code, a reconciliation mark and the end time). Free text from the journal (evidence,
// reasons, notes) is never copied, because the game's log lines it may quote can carry character and retainer names.
internal static class RunSummaryBuilder
{
    private const int ShortIdLength = 8;
    private const int GuidDigits = 32;
    private const int ListingHashBytes = 4;
    private const int RandomSaltBytes = 16;

    // For a journal read back later: the run's progress is replayed from the same entries the details come from.
    public static RunSummary Build(Guid runId, IReadOnlyList<JournalEvent?> entries, bool dryRun, IExportNames names, RunSummaryOptions options = default)
        => Build(JournalReplayer.Replay(entries, runId), entries, dryRun, names, options);

    public static RunSummary Build(
        JournalProgress progress,
        IReadOnlyList<JournalEvent?> entries,
        bool dryRun,
        IExportNames names,
        RunSummaryOptions options = default)
    {
        var details = CollectDetails(progress.RunId, entries, out var startedAtUtc, out var endedAtUtc);
        var purchases = ImmutableArray.CreateBuilder<SummaryAttempt>();
        var rejected = ImmutableArray.CreateBuilder<SummaryAttempt>();
        var unknown = ImmutableArray.CreateBuilder<SummaryAttempt>();
        var notSent = ImmutableArray.CreateBuilder<SummaryAttempt>();
        var attempts = progress.Attempts;
        for (var index = 0; index < attempts.Length; index++)
        {
            var attempt = attempts[index];
            if (!attempt.HasIntent)
            {
                continue;
            }

            details.TryGetValue(attempt.AttemptId, out var detail);
            switch (attempt.Status)
            {
                case AttemptStatus.Confirmed:
                    purchases.Add(Describe(attempt, detail, attempt.ConfirmedQuantity, attempt.ChargedAmount, names));
                    break;
                case AttemptStatus.Rejected:
                    rejected.Add(Describe(attempt, detail, attempt.Quantity, attempt.ReservedAmount, names) with { ReasonCode = detail.ReasonCode });
                    break;
                case AttemptStatus.NotSent:
                    notSent.Add(Describe(attempt, detail, attempt.Quantity, attempt.ReservedAmount, names));
                    break;
                default:
                    unknown.Add(Describe(attempt, detail, attempt.Quantity, attempt.ReservedAmount, names));
                    break;
            }
        }

        var authorization = progress.Authorization;
        var cap = authorization?.MaxSpend ?? 0;
        return new RunSummary
        {
            RunId = ShortId(progress.RunId),
            StartedAtUtc = authorization?.CreatedAtUtc ?? startedAtUtc,
            EndedAtUtc = endedAtUtc,
            Outcome = progress.RecoveredState,
            DryRun = dryRun,
            Spend = new SummarySpend(progress.ConfirmedSpend, cap, progress.ReservedSpend, Remaining(cap, progress.ConfirmedSpend, progress.ReservedSpend)),
            Purchases = purchases.ToImmutable(),
            Excess = CollectExcess(progress.Demands, names),
            Unresolved = CollectShortfalls(progress.Demands, dryRun, names),
            Rejected = rejected.ToImmutable(),
            Unknown = unknown.ToImmutable(),
            NotSent = notSent.ToImmutable(),
            Diagnostics = options.IncludeDiagnostics ? BuildDiagnostics(progress, options.DiagnosticsSalt) : null,
        };
    }

    private static Dictionary<Guid, AttemptDetail> CollectDetails(
        Guid runId,
        IReadOnlyList<JournalEvent?> entries,
        out DateTime? startedAtUtc,
        out DateTime? endedAtUtc)
    {
        var details = new Dictionary<Guid, AttemptDetail>();
        startedAtUtc = null;
        endedAtUtc = null;
        long endedSequence = 0;
        for (var position = 0; position < entries.Count; position++)
        {
            var entry = entries[position];
            if (entry is null || entry.RunId != runId || !entry.IsWellFormed())
            {
                continue;
            }

            if (startedAtUtc is null || entry.AtUtc < startedAtUtc)
            {
                startedAtUtc = entry.AtUtc;
            }

            switch (entry)
            {
                case RunEnded ended when ended.Sequence > endedSequence:
                    endedSequence = ended.Sequence;
                    endedAtUtc = ended.AtUtc;
                    break;
                case IntentRecorded intent:
                    ref var intentDetail = ref CollectionsMarshal.GetValueRefOrAddDefault(details, intent.AttemptId, out _);
                    if (intentDetail.IntentSequence == 0 || intent.Sequence < intentDetail.IntentSequence)
                    {
                        intentDetail.IntentSequence = intent.Sequence;
                        intentDetail.IsHq = intent.IsHq;
                    }

                    break;
                case PurchaseRejected rejection:
                    ref var rejectionDetail = ref CollectionsMarshal.GetValueRefOrAddDefault(details, rejection.AttemptId, out _);
                    if (rejectionDetail.RejectionSequence == 0 || rejection.Sequence < rejectionDetail.RejectionSequence)
                    {
                        rejectionDetail.RejectionSequence = rejection.Sequence;
                        rejectionDetail.ReasonCode = rejection.ReasonCode;
                    }

                    break;
                case Reconciled reconciled:
                    CollectionsMarshal.GetValueRefOrAddDefault(details, reconciled.AttemptId, out _).Reconciled = true;
                    break;
            }
        }

        return details;
    }

    private static SummaryAttempt Describe(AttemptRecord attempt, AttemptDetail detail, int quantity, long payable, IExportNames names)
    {
        return new SummaryAttempt(
            attempt.Demand.ItemId,
            ExportText.ItemName(names, attempt.Demand.ItemId),
            detail.IsHq,
            quantity,
            payable,
            attempt.WorldId,
            ExportText.WorldName(names, attempt.WorldId),
            0,
            detail.Reconciled);
    }

    private static ImmutableArray<SummaryExcess> CollectExcess(ImmutableArray<DemandProgress> demands, IExportNames names)
    {
        var excess = ImmutableArray.CreateBuilder<SummaryExcess>();
        for (var index = 0; index < demands.Length; index++)
        {
            var progress = demands[index];
            var demand = progress.Demand;
            var extra = progress.ConfirmedQuantity - demand.RequiredQuantity;
            if (extra > 0)
            {
                excess.Add(new SummaryExcess(demand.ItemId, ExportText.ItemName(names, demand.ItemId), demand.Quality, extra));
            }
        }

        return excess.ToImmutable();
    }

    private static ImmutableArray<SummaryShortfall> CollectShortfalls(ImmutableArray<DemandProgress> demands, bool dryRun, IExportNames names)
    {
        var shortfalls = ImmutableArray.CreateBuilder<SummaryShortfall>();
        for (var index = 0; index < demands.Length; index++)
        {
            var progress = demands[index];
            var missing = progress.Outstanding.RequiredQuantity;
            if (missing <= 0)
            {
                continue;
            }

            var demand = progress.Demand;
            shortfalls.Add(new SummaryShortfall(
                demand.ItemId,
                ExportText.ItemName(names, demand.ItemId),
                demand.Quality,
                demand.RequiredQuantity,
                progress.ConfirmedQuantity,
                missing,
                progress.UnknownQuantity,
                ShortfallReasonFor(progress, dryRun)));
        }

        return shortfalls.ToImmutable();
    }

    private static ShortfallReason ShortfallReasonFor(DemandProgress progress, bool dryRun)
    {
        if (progress.UnknownQuantity > 0)
        {
            return ShortfallReason.AwaitingReconciliation;
        }

        if (dryRun)
        {
            return ShortfallReason.DryRun;
        }

        return progress.FailedAttempts > 0 ? ShortfallReason.PurchasesFailed : ShortfallReason.NotAttempted;
    }

    private static long Remaining(long cap, long confirmed, long reserved)
    {
        if (!Gil.TrySubtract(cap, confirmed, out var afterConfirmed) || !Gil.TrySubtract(afterConfirmed, reserved, out var remaining))
        {
            return 0;
        }

        return Math.Max(0, remaining);
    }

    private static RunDiagnostics BuildDiagnostics(JournalProgress progress, string? salt)
    {
        var key = string.IsNullOrEmpty(salt) ? RandomNumberGenerator.GetBytes(RandomSaltBytes) : Encoding.UTF8.GetBytes(salt);
        var anomalies = ImmutableArray.CreateBuilder<DiagnosticAnomaly>(progress.Anomalies.Length);
        for (var index = 0; index < progress.Anomalies.Length; index++)
        {
            var anomaly = progress.Anomalies[index];
            var attempt = anomaly.AttemptId == Guid.Empty ? string.Empty : ShortId(anomaly.AttemptId);
            anomalies.Add(new DiagnosticAnomaly(anomaly.Kind, anomaly.Position, anomaly.Sequence, attempt));
        }

        var attempts = ImmutableArray.CreateBuilder<DiagnosticAttempt>(progress.Attempts.Length);
        for (var index = 0; index < progress.Attempts.Length; index++)
        {
            var attempt = progress.Attempts[index];
            var listing = attempt.HasIntent ? ListingHash(attempt.ListingId, key) : string.Empty;
            attempts.Add(new DiagnosticAttempt(ShortId(attempt.AttemptId), attempt.Status, listing, attempt.WorldId, attempt.FirstSequence));
        }

        return new RunDiagnostics
        {
            LastSequence = progress.LastSequence,
            TornTail = progress.TornTail,
            AttemptCount = progress.AttemptCount,
            FailedAttemptCount = progress.FailedAttemptCount,
            ExcludedListingCount = progress.ExcludedListingKeys.Length,
            Anomalies = anomalies.MoveToImmutable(),
            Attempts = attempts.MoveToImmutable(),
        };
    }

    private static string ShortId(Guid id)
    {
        Span<char> digits = stackalloc char[GuidDigits];
        id.TryFormat(digits, out _, "N");
        return new string(digits[..ShortIdLength]);
    }

    // A keyed hash lets two attempts on the same listing be matched inside one export without revealing the listing id.
    private static string ListingHash(ulong listingId, byte[] key)
    {
        Span<byte> source = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(source, listingId);
        Span<byte> hash = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(key, source, hash);
        return Convert.ToHexStringLower(hash[..ListingHashBytes]);
    }

    private struct AttemptDetail
    {
        public long IntentSequence;
        public bool IsHq;
        public long RejectionSequence;
        public int ReasonCode;
        public bool Reconciled;
    }
}
