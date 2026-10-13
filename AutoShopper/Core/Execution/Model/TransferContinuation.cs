namespace AutoShopper.Core.Execution.Model;

// Written next to the pending journey right before a run asks for a data center transfer, which logs the character out.
// The login that ends the transfer reads it to tell that this run asked for the logout. Progress is not copied here: it
// stays in the run's journal.
public readonly record struct TransferContinuation(
    Guid RunId,
    ulong CharacterContentId,
    DateTime AuthorizedAtUtc,
    int StopIndex,
    uint TargetWorldId,
    string TargetWorld,
    DateTime JourneyStartedAtUtc,
    DateTime ArmedAtUtc);

internal enum ContinuationVerdict : byte
{
    Continue = 0,
    NoContinuation = 1,
    OtherRun = 2,
    NoLogin = 3,
    NoPendingJourney = 4,
    OtherJourney = 5,
    OtherCharacter = 6,
    NotArrived = 7,
    PurchaseUnresolved = 8,
    Expired = 9,
}

internal readonly record struct ContinuationFacts
{
    public required TransferContinuation? Continuation { get; init; }

    public required Guid RunId { get; init; }

    public required DateTime AuthorizedAtUtc { get; init; }

    public required bool LoginSeen { get; init; }

    // The pending journey as it stood at the logout the transfer brought, null when there was none.
    public required string? PendingJourneyWorld { get; init; }

    public required DateTime PendingJourneyStartedAtUtc { get; init; }

    public required ulong ContentId { get; init; }

    public required uint CurrentWorldId { get; init; }

    public required bool PurchaseUnresolved { get; init; }

    public required DateTime NowUtc { get; init; }
}

// Only the login that completes the transfer this run planned carries the run on by itself: the continuation names this
// run, the pending journey is the one it wrote, the same character is back, it stands on the planned world, and no
// purchase waits for an answer. Anything else leaves the run for the player's Resume.
internal static class ContinuationDecision
{
    // Lifestream gives a data center transfer up to an hour; a continuation older than this outlived its transfer.
    public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(90);

    public static ContinuationVerdict Evaluate(in ContinuationFacts facts)
    {
        if (facts.Continuation is not { } continuation)
        {
            return ContinuationVerdict.NoContinuation;
        }

        if (continuation.RunId != facts.RunId || continuation.AuthorizedAtUtc != facts.AuthorizedAtUtc)
        {
            return ContinuationVerdict.OtherRun;
        }

        if (facts.NowUtc - continuation.ArmedAtUtc > MaximumAge)
        {
            return ContinuationVerdict.Expired;
        }

        if (!facts.LoginSeen)
        {
            return ContinuationVerdict.NoLogin;
        }

        if (facts.PendingJourneyWorld is not { } world)
        {
            return ContinuationVerdict.NoPendingJourney;
        }

        if (!string.Equals(world, continuation.TargetWorld, StringComparison.OrdinalIgnoreCase) || facts.PendingJourneyStartedAtUtc != continuation.JourneyStartedAtUtc)
        {
            return ContinuationVerdict.OtherJourney;
        }

        if (facts.ContentId == 0 || facts.ContentId != continuation.CharacterContentId)
        {
            return ContinuationVerdict.OtherCharacter;
        }

        if (facts.CurrentWorldId != continuation.TargetWorldId)
        {
            return ContinuationVerdict.NotArrived;
        }

        return facts.PurchaseUnresolved ? ContinuationVerdict.PurchaseUnresolved : ContinuationVerdict.Continue;
    }
}
