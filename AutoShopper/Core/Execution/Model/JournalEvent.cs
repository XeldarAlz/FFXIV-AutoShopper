using System.Text.Json.Serialization;

namespace AutoShopper.Core.Execution.Model;

internal static class JournalEventKind
{
    public const string PropertyName = "Kind";
    public const string RunAuthorized = "RunAuthorized";
    public const string IntentRecorded = "IntentRecorded";
    public const string PurchaseSubmitted = "PurchaseSubmitted";
    public const string PurchaseConfirmed = "PurchaseConfirmed";
    public const string PurchaseRejected = "PurchaseRejected";
    public const string PurchaseUnknown = "PurchaseUnknown";
    public const string Reconciled = "Reconciled";
    public const string Checkpoint = "Checkpoint";
    public const string RunEnded = "RunEnded";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = JournalEventKind.PropertyName)]
[JsonDerivedType(typeof(RunAuthorized), JournalEventKind.RunAuthorized)]
[JsonDerivedType(typeof(IntentRecorded), JournalEventKind.IntentRecorded)]
[JsonDerivedType(typeof(PurchaseSubmitted), JournalEventKind.PurchaseSubmitted)]
[JsonDerivedType(typeof(PurchaseConfirmed), JournalEventKind.PurchaseConfirmed)]
[JsonDerivedType(typeof(PurchaseRejected), JournalEventKind.PurchaseRejected)]
[JsonDerivedType(typeof(PurchaseUnknown), JournalEventKind.PurchaseUnknown)]
[JsonDerivedType(typeof(Reconciled), JournalEventKind.Reconciled)]
[JsonDerivedType(typeof(Checkpoint), JournalEventKind.Checkpoint)]
[JsonDerivedType(typeof(RunEnded), JournalEventKind.RunEnded)]
internal abstract record JournalEvent
{
    public required long Sequence { get; init; }

    public required Guid RunId { get; init; }

    public required DateTime AtUtc { get; init; }

    [JsonIgnore]
    public abstract string Kind { get; }

    public virtual bool IsWellFormed()
    {
        return Sequence > 0 && RunId != Guid.Empty;
    }
}

internal abstract record AttemptEvent : JournalEvent
{
    public required Guid AttemptId { get; init; }

    public override bool IsWellFormed()
    {
        return base.IsWellFormed() && AttemptId != Guid.Empty;
    }
}
