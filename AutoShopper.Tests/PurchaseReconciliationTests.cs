using AutoShopper.Core.Market.Model;

namespace AutoShopper.Tests;

public sealed class PurchaseReconciliationTests
{
    private const long Payable = 5_050;
    private const int Quantity = 5;
    private static readonly PurseReading Before = new(100_000, 10);
    private static readonly PurseReading Bought = new(100_000 - Payable, 10 + Quantity);
    private static readonly long WithinWindow = PurchaseReconciliation.SettleWindowMs - 1;
    private static readonly long PastWindow = PurchaseReconciliation.SettleWindowMs;

    [Fact]
    public void AConfirmedPurchaseThatTheBagsAndThePurseShowSettlesAtOnce()
    {
        var decision = PurchaseReconciliation.Reconcile(PurchaseDecision.Confirmed, Payable, Quantity, Before, Bought, 0);

        Assert.Equal(PurchaseDecision.Confirmed, decision);
    }

    [Fact]
    public void APurseThatUpdatesAFewFramesLateIsWaitedFor()
    {
        var notYet = PurchaseReconciliation.Reconcile(PurchaseDecision.Confirmed, Payable, Quantity, Before, Before, 50);
        var caughtUp = PurchaseReconciliation.Reconcile(PurchaseDecision.Confirmed, Payable, Quantity, Before, Bought, 120);

        Assert.Equal(PurchaseOutcomeKind.Pending, notYet.Kind);
        Assert.Equal(PurchaseDecision.Confirmed, caughtUp);
    }

    [Fact]
    public void ABagUpdateThatTrailsThePurseIsWaitedFor()
    {
        var purseOnly = new PurseReading(Bought.Gil, Before.Owned);

        var decision = PurchaseReconciliation.Reconcile(PurchaseDecision.Confirmed, Payable, Quantity, Before, purseOnly, WithinWindow);

        Assert.Equal(PurchaseOutcomeKind.Pending, decision.Kind);
    }

    [Fact]
    public void AConfirmationThePurseNeverShowsIsUncertain()
    {
        var decision = PurchaseReconciliation.Reconcile(PurchaseDecision.Confirmed, Payable, Quantity, Before, Before, PastWindow);

        Assert.Equal(PurchaseDecision.Unknown(PurchaseUnknownCause.GilMismatch), decision);
    }

    [Fact]
    public void AConfirmationThatTookMoreThanThePayableIsUncertain()
    {
        var overcharged = Bought with { Gil = Bought.Gil - 1 };

        Assert.Equal(PurchaseOutcomeKind.Pending, PurchaseReconciliation.Reconcile(PurchaseDecision.Confirmed, Payable, Quantity, Before, overcharged, WithinWindow).Kind);
        Assert.Equal(PurchaseDecision.Unknown(PurchaseUnknownCause.GilMismatch), PurchaseReconciliation.Reconcile(PurchaseDecision.Confirmed, Payable, Quantity, Before, overcharged, PastWindow));
    }

    [Fact]
    public void AConfirmationTheBagsNeverShowIsUncertain()
    {
        var purseOnly = new PurseReading(Bought.Gil, Before.Owned + Quantity - 1);

        var decision = PurchaseReconciliation.Reconcile(PurchaseDecision.Confirmed, Payable, Quantity, Before, purseOnly, PastWindow);

        Assert.Equal(PurchaseDecision.Unknown(PurchaseUnknownCause.InventoryMismatch), decision);
    }

    [Fact]
    public void ARefusalThatLeftThePurseAndTheBagsAloneSettlesAtOnce()
    {
        var refusal = PurchaseDecision.Rejected(PurchaseRejection.ListingGone);

        Assert.Equal(refusal, PurchaseReconciliation.Reconcile(refusal, Payable, Quantity, Before, Before, 0));
    }

    [Fact]
    public void ARefusalThatTookGilIsUncertain()
    {
        var refusal = PurchaseDecision.Rejected(PurchaseRejection.ListingGone);

        Assert.Equal(PurchaseOutcomeKind.Pending, PurchaseReconciliation.Reconcile(refusal, Payable, Quantity, Before, Bought, WithinWindow).Kind);
        Assert.Equal(PurchaseDecision.Unknown(PurchaseUnknownCause.GilMismatch), PurchaseReconciliation.Reconcile(refusal, Payable, Quantity, Before, Bought, PastWindow));
    }

    [Fact]
    public void APurseThatCannotBeReadLeavesTheAnswerUncertain()
    {
        var decision = PurchaseReconciliation.Reconcile(PurchaseDecision.Confirmed, Payable, Quantity, PurseReading.Unreadable, Bought, PastWindow);

        Assert.Equal(PurchaseDecision.Unknown(PurchaseUnknownCause.PurseUnreadable), decision);
    }

    [Fact]
    public void AnAnswerThatIsAlreadyUncertainStaysSo()
    {
        var uncertain = PurchaseDecision.Unknown(PurchaseUnknownCause.Timeout);

        Assert.Equal(uncertain, PurchaseReconciliation.Reconcile(uncertain, Payable, Quantity, Before, Bought, 0));
        Assert.Equal(PurchaseDecision.Pending, PurchaseReconciliation.Reconcile(PurchaseDecision.Pending, Payable, Quantity, Before, Bought, 0));
    }

    [Fact]
    public void AnUncertainPurchaseIsBookedAtTheLargerOfThePayableAndWhatThePurseLost()
    {
        Assert.Equal(Payable, PurchaseReconciliation.Charged(Before, Bought));
        Assert.Equal(-1, PurchaseReconciliation.Charged(PurseReading.Unreadable, Bought));
        Assert.Equal(Payable, PurchaseReconciliation.Booked(Payable, -1));
        Assert.Equal(Payable, PurchaseReconciliation.Booked(Payable, 0));
        Assert.Equal(Payable + 900, PurchaseReconciliation.Booked(Payable, Payable + 900));
    }
}
