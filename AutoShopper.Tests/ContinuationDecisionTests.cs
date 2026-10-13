using AutoShopper.Core.Execution.Model;

namespace AutoShopper.Tests;

public sealed class ContinuationDecisionTests
{
    private const ulong Character = 1_001;
    private const ulong OtherCharacter = 2_002;
    private const uint Target = 401;
    private const uint Origin = 54;
    private const string TargetName = "Phantom";

    private static readonly Guid RunId = new("5a0b1c2d-3e4f-4a5b-8c6d-7e8f9a0b1c2d");
    private static readonly DateTime AuthorizedAt = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime JourneyStarted = new(2026, 10, 13, 12, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime ArmedAt = JourneyStarted.AddSeconds(5);

    [Fact]
    public void TheLoginThatCompletesThePlannedTransferCarriesTheRunOn()
    {
        Assert.Equal(ContinuationVerdict.Continue, ContinuationDecision.Evaluate(Facts()));
    }

    [Fact]
    public void NoContinuationMeansTheLogoutWasNotPlanned()
    {
        Assert.Equal(ContinuationVerdict.NoContinuation, ContinuationDecision.Evaluate(Facts() with { Continuation = null }));
    }

    [Fact]
    public void AContinuationOfAnotherRunOrAuthorizationIsNotThisRuns()
    {
        Assert.Equal(ContinuationVerdict.OtherRun, ContinuationDecision.Evaluate(Facts() with { RunId = Guid.NewGuid() }));
        Assert.Equal(ContinuationVerdict.OtherRun, ContinuationDecision.Evaluate(Facts() with { AuthorizedAtUtc = AuthorizedAt.AddMinutes(1) }));
    }

    [Fact]
    public void ALoginTheRunNeverSawDoesNotCount()
    {
        Assert.Equal(ContinuationVerdict.NoLogin, ContinuationDecision.Evaluate(Facts() with { LoginSeen = false }));
    }

    [Fact]
    public void ThePendingJourneyMustBeTheOneTheRunWrote()
    {
        Assert.Equal(ContinuationVerdict.NoPendingJourney, ContinuationDecision.Evaluate(Facts() with { PendingJourneyWorld = null }));
        Assert.Equal(ContinuationVerdict.OtherJourney, ContinuationDecision.Evaluate(Facts() with { PendingJourneyWorld = "Lamia" }));
        Assert.Equal(ContinuationVerdict.OtherJourney, ContinuationDecision.Evaluate(Facts() with { PendingJourneyStartedAtUtc = JourneyStarted.AddSeconds(1) }));
        Assert.Equal(ContinuationVerdict.Continue, ContinuationDecision.Evaluate(Facts() with { PendingJourneyWorld = "phantom" }));
    }

    [Fact]
    public void AnotherCharacterNeverCarriesTheRunOn()
    {
        Assert.Equal(ContinuationVerdict.OtherCharacter, ContinuationDecision.Evaluate(Facts() with { ContentId = OtherCharacter }));
        Assert.Equal(ContinuationVerdict.OtherCharacter, ContinuationDecision.Evaluate(Facts() with { ContentId = 0 }));
    }

    [Fact]
    public void ALoginOnAnotherWorldIsNotTheTransfersEnd()
    {
        Assert.Equal(ContinuationVerdict.NotArrived, ContinuationDecision.Evaluate(Facts() with { CurrentWorldId = Origin }));
    }

    [Fact]
    public void APurchaseWithoutAnAnswerKeepsTheRunForThePlayer()
    {
        Assert.Equal(ContinuationVerdict.PurchaseUnresolved, ContinuationDecision.Evaluate(Facts() with { PurchaseUnresolved = true }));
    }

    [Fact]
    public void AContinuationOlderThanAnyTransferHasExpired()
    {
        var late = ArmedAt + ContinuationDecision.MaximumAge + TimeSpan.FromSeconds(1);

        Assert.Equal(ContinuationVerdict.Expired, ContinuationDecision.Evaluate(Facts() with { NowUtc = late }));
        Assert.Equal(ContinuationVerdict.Continue, ContinuationDecision.Evaluate(Facts() with { NowUtc = ArmedAt + ContinuationDecision.MaximumAge }));
    }

    private static ContinuationFacts Facts() => new()
    {
        Continuation = new TransferContinuation(RunId, Character, AuthorizedAt, 2, Target, TargetName, JourneyStarted, ArmedAt),
        RunId = RunId,
        AuthorizedAtUtc = AuthorizedAt,
        LoginSeen = true,
        PendingJourneyWorld = TargetName,
        PendingJourneyStartedAtUtc = JourneyStarted,
        ContentId = Character,
        CurrentWorldId = Target,
        PurchaseUnresolved = false,
        NowUtc = ArmedAt.AddMinutes(4),
    };
}
