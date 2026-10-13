using AutoShopper.Core.Market.Model;

namespace AutoShopper.Tests;

public sealed class PurchaseEvidenceTests
{
    private const uint Item = 5_000;
    private const long ResponseAt = 200;

    private static readonly Guid Attempt = new("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly ListingFields Normal = new(77, 501, Item, 5, 1_000, 50, false);

    [Fact]
    public void AgreeingSignalsConfirmOnceTheSettleWindowPassed()
    {
        var evidence = Evidence(Normal);
        evidence.RecordRequest(Normal);
        evidence.RecordResponse(Item, 0, ResponseAt);
        evidence.RecordItemPurchased(Item, 5);
        evidence.RecordLogMessage(PurchaseMessages.SuccessCandidate);

        Assert.Equal(PurchaseOutcomeKind.Pending, evidence.Decide(ResponseAt + PurchaseEvidence.SettleMs - 1).Kind);
        Assert.Equal(PurchaseDecision.Confirmed, evidence.Decide(ResponseAt + PurchaseEvidence.SettleMs));
    }

    [Fact]
    public void HighQualityOffsetIsAcceptedForAHighQualityPurchaseOnly()
    {
        var highQuality = Normal with { IsHq = true };
        var accepted = Evidence(highQuality);
        accepted.RecordRequest(highQuality);
        accepted.RecordResponse(Item + MarketItemId.HighQualityOffset, 0, ResponseAt);
        accepted.RecordItemPurchased(Item + MarketItemId.HighQualityOffset, 5);

        var refused = Evidence(Normal);
        refused.RecordRequest(Normal);
        refused.RecordResponse(Item, 0, ResponseAt);
        refused.RecordItemPurchased(Item + MarketItemId.HighQualityOffset, 5);

        Assert.Equal(PurchaseDecision.Confirmed, accepted.Decide(ResponseAt + PurchaseEvidence.SettleMs));
        Assert.Equal(PurchaseDecision.Unknown(PurchaseUnknownCause.Contradiction), refused.Decide(ResponseAt));
    }

    [Fact]
    public void SuccessWithoutTheSecondSignalIsUnknown()
    {
        var evidence = Evidence(Normal);
        evidence.RecordRequest(Normal);
        evidence.RecordResponse(Item, 0, ResponseAt);

        Assert.Equal(PurchaseOutcomeKind.Pending, evidence.Decide(ResponseAt + PurchaseEvidence.AgreementMs - 1).Kind);
        Assert.Equal(PurchaseDecision.Unknown(PurchaseUnknownCause.MissingAgreement), evidence.Decide(ResponseAt + PurchaseEvidence.AgreementMs));
    }

    [Fact]
    public void SuccessWithoutTheRequestEventIsUnknown()
    {
        var evidence = Evidence(Normal);
        evidence.RecordResponse(Item, 0, ResponseAt);
        evidence.RecordItemPurchased(Item, 5);

        Assert.Equal(PurchaseDecision.Unknown(PurchaseUnknownCause.MissingRequest), evidence.Decide(ResponseAt + PurchaseEvidence.SettleMs));
    }

    [Fact]
    public void RefusalIsMappedFromTheResponseCode()
    {
        var evidence = Evidence(Normal);
        evidence.RecordRequest(Normal);
        evidence.RecordResponse(Item, 4_523, ResponseAt);
        evidence.RecordLogMessage(4_523);

        Assert.Equal(PurchaseOutcomeKind.Pending, evidence.Decide(ResponseAt).Kind);
        Assert.Equal(PurchaseDecision.Rejected(PurchaseRejection.ListingGone), evidence.Decide(ResponseAt + PurchaseEvidence.SettleMs));
    }

    [Fact]
    public void UnknownResponseCodeTakesTheLogLineClassAndKeepsUnclassifiedOtherwise()
    {
        var withLine = Evidence(Normal);
        withLine.RecordResponse(0, 99_999, ResponseAt);
        withLine.RecordLogMessage(4_521);
        var withoutLine = Evidence(Normal);
        withoutLine.RecordResponse(Item, 99_999, ResponseAt);

        Assert.Equal(PurchaseDecision.Rejected(PurchaseRejection.InventoryFull), withLine.Decide(ResponseAt + PurchaseEvidence.SettleMs));
        Assert.Equal(PurchaseDecision.Rejected(PurchaseRejection.Unclassified), withoutLine.Decide(ResponseAt + PurchaseEvidence.SettleMs));
        Assert.Equal(99_999u, withoutLine.ResponseErrorId);
    }

    [Fact]
    public void SuccessContradictedByAFailureLineIsUnknown()
    {
        var evidence = Evidence(Normal);
        evidence.RecordRequest(Normal);
        evidence.RecordResponse(Item, 0, ResponseAt);
        evidence.RecordItemPurchased(Item, 5);
        evidence.RecordLogMessage(4_523);

        Assert.Equal(PurchaseDecision.Unknown(PurchaseUnknownCause.Contradiction), evidence.Decide(ResponseAt));
    }

    [Fact]
    public void RefusalContradictedByAMatchingPurchaseIsUnknown()
    {
        var evidence = Evidence(Normal);
        evidence.RecordRequest(Normal);
        evidence.RecordResponse(Item, 4_523, ResponseAt);
        evidence.RecordItemPurchased(Item, 5);

        Assert.Equal(PurchaseDecision.Unknown(PurchaseUnknownCause.Contradiction), evidence.Decide(ResponseAt + PurchaseEvidence.SettleMs));
    }

    [Fact]
    public void RefusalContradictedByTheSuccessLineIsUnknown()
    {
        var evidence = Evidence(Normal);
        evidence.RecordResponse(Item, 386, ResponseAt);
        evidence.RecordLogMessage(PurchaseMessages.SuccessCandidate);

        Assert.Equal(PurchaseDecision.Unknown(PurchaseUnknownCause.Contradiction), evidence.Decide(ResponseAt));
    }

    [Fact]
    public void PurchaseOfADifferentQuantityIsUnknown()
    {
        var evidence = Evidence(Normal);
        evidence.RecordRequest(Normal);
        evidence.RecordResponse(Item, 0, ResponseAt);
        evidence.RecordItemPurchased(Item, 4);

        Assert.Equal(PurchaseDecision.Unknown(PurchaseUnknownCause.Contradiction), evidence.Decide(ResponseAt));
    }

    [Fact]
    public void RequestThatLeftWithOtherValuesIsUnknown()
    {
        var evidence = Evidence(Normal);
        evidence.RecordRequest(Normal with { UnitPrice = 999 });

        Assert.Equal(PurchaseDecision.Unknown(PurchaseUnknownCause.RequestMismatch), evidence.Decide(10));
        Assert.Equal(ListingField.UnitPrice, evidence.RequestDifference);
    }

    [Fact]
    public void DisagreeingResponsesAreUnknown()
    {
        var evidence = Evidence(Normal);
        evidence.RecordRequest(Normal);
        evidence.RecordResponse(Item, 0, ResponseAt);
        evidence.RecordResponse(Item, 4_523, ResponseAt + 10);

        Assert.Equal(PurchaseDecision.Unknown(PurchaseUnknownCause.Contradiction), evidence.Decide(ResponseAt + 10));
    }

    [Fact]
    public void SilenceUntilTheDeadlineIsUnknownEvenWithAFailureLine()
    {
        var evidence = Evidence(Normal);
        evidence.RecordRequest(Normal);
        evidence.RecordLogMessage(4_523);

        Assert.Equal(PurchaseOutcomeKind.Pending, evidence.Decide(PurchaseEvidence.DeadlineMs - 1).Kind);
        Assert.Equal(PurchaseDecision.Unknown(PurchaseUnknownCause.Timeout), evidence.Decide(PurchaseEvidence.DeadlineMs));
    }

    [Fact]
    public void SearchFailureLineIsNotPurchaseEvidence()
    {
        var evidence = Evidence(Normal);
        evidence.RecordRequest(Normal);
        evidence.RecordResponse(Item, 0, ResponseAt);
        evidence.RecordItemPurchased(Item, 5);
        evidence.RecordLogMessage(385);

        Assert.Equal(0, evidence.FailureLogCount);
        Assert.Equal(PurchaseDecision.Confirmed, evidence.Decide(ResponseAt + PurchaseEvidence.SettleMs));
    }

    private static PurchaseEvidence Evidence(ListingFields listing)
    {
        return new PurchaseEvidence(new PurchaseExpectation(Attempt, listing, 5_050), 0);
    }
}
