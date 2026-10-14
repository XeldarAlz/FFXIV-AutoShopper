using System.Collections.Immutable;
using System.Text.Json.Serialization;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Planning;

namespace AutoShopper.Core.Exports;

internal enum ShortfallReason : byte
{
    NotAttempted = 0,
    PurchasesFailed = 1,
    AwaitingReconciliation = 2,
}

internal readonly record struct SummaryAttempt(
    uint ItemId,
    string ItemName,
    bool IsHq,
    int Quantity,
    long Payable,
    uint WorldId,
    string WorldName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int ReasonCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Reconciled);

internal readonly record struct SummaryShortfall(
    uint ItemId,
    string ItemName,
    DemandQuality Quality,
    int RequiredQuantity,
    int ConfirmedQuantity,
    int MissingQuantity,
    int UnknownQuantity,
    ShortfallReason Reason);

internal readonly record struct SummaryExcess(uint ItemId, string ItemName, DemandQuality Quality, int Quantity);

internal readonly record struct SummarySpend(long Confirmed, long Cap, long Reserved, long Remaining);

internal readonly record struct DiagnosticAnomaly(JournalAnomalyKind Kind, int Position, long Sequence, string Attempt);

internal readonly record struct DiagnosticAttempt(string Attempt, AttemptStatus Status, string Listing, uint WorldId, long FirstSequence);

internal readonly record struct RunSummaryOptions(bool IncludeDiagnostics, string? DiagnosticsSalt)
{
    public static RunSummaryOptions Standard => default;

    // Without a salt of its own, each export draws a random one, so hashes from two exports never line up.
    public static RunSummaryOptions WithDiagnostics(string? salt = null) => new(true, salt);
}

internal sealed record RunDiagnostics
{
    public required long LastSequence { get; init; }

    public required bool TornTail { get; init; }

    public required int AttemptCount { get; init; }

    public required int FailedAttemptCount { get; init; }

    public required int ExcludedListingCount { get; init; }

    public required ImmutableArray<DiagnosticAnomaly> Anomalies { get; init; }

    public required ImmutableArray<DiagnosticAttempt> Attempts { get; init; }
}

// Ids of the character, retainers, sellers and listings never enter this shape, so no export of it can leak them.
internal sealed record RunSummary
{
    public const string FormatName = "AutoShopper.RunSummary";
    public const int CurrentVersion = 1;

    public string Format { get; init; } = FormatName;

    public int Version { get; init; } = CurrentVersion;

    public required string RunId { get; init; }

    public required DateTime? StartedAtUtc { get; init; }

    public required DateTime? EndedAtUtc { get; init; }

    public required RunState Outcome { get; init; }

    public required SummarySpend Spend { get; init; }

    public required ImmutableArray<SummaryAttempt> Purchases { get; init; }

    public required ImmutableArray<SummaryExcess> Excess { get; init; }

    public required ImmutableArray<SummaryShortfall> Unresolved { get; init; }

    public required ImmutableArray<SummaryAttempt> Rejected { get; init; }

    public required ImmutableArray<SummaryAttempt> Unknown { get; init; }

    public required ImmutableArray<SummaryAttempt> NotSent { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RunDiagnostics? Diagnostics { get; init; }

    public int ExcessQuantity
    {
        get
        {
            var total = 0;
            for (var index = 0; index < Excess.Length; index++)
            {
                total += Excess[index].Quantity;
            }

            return total;
        }
    }
}
