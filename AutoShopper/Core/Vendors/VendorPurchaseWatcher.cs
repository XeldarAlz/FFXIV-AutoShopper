using AutoShopper.Core.Inventory;
using AutoShopper.Core.Market;
using AutoShopper.Core.Market.Model;
using AutoShopper.Core.Vendors.Model;

namespace AutoShopper.Core.Vendors;

internal readonly record struct VendorExpectation(Guid AttemptId, uint ItemId, uint ShopId, int Quantity, long Payable, PurseReading Before, long ArmedAtMs);

internal sealed class VendorPurchaseWatcher : IPurchaseWatcher
{
    private VendorExpectation? expected;
    private PurchaseOutcome decided = PurchaseOutcome.None;
    private long lastBusyAtMs = VendorPurchaseDecision.NeverBusy;

    public bool HasAttempt => expected is not null;

    public void Arm(in VendorExpectation expectation)
    {
        expected = expectation;
        decided = PurchaseOutcome.None;
        lastBusyAtMs = VendorPurchaseDecision.NeverBusy;
    }

    public void NoteSent()
    {
        if (expected is { } attempt)
        {
            SampleBusy(attempt.ShopId, Environment.TickCount64);
        }
    }

    public PurchaseOutcome Poll()
    {
        if (expected is not { } attempt)
        {
            return PurchaseOutcome.None;
        }

        if (decided.Kind != PurchaseOutcomeKind.None)
        {
            return decided;
        }

        var nowMs = Environment.TickCount64;
        var busy = SampleBusy(attempt.ShopId, nowMs);
        var idleMs = lastBusyAtMs == VendorPurchaseDecision.NeverBusy ? VendorPurchaseDecision.NeverBusy : nowMs - lastBusyAtMs;
        var live = InventoryReader.ReadLive(attempt.ItemId);
        var now = live.Available ? new PurseReading(live.Gil, live.Owned) : PurseReading.Unreadable;
        var elapsedMs = nowMs - attempt.ArmedAtMs;
        var decision = VendorPurchaseDecision.Decide(attempt.Before, now, attempt.Quantity, attempt.Payable, busy, idleMs, elapsedMs);
        var outcome = new PurchaseOutcome(
            decision.Kind,
            attempt.AttemptId,
            attempt.Quantity,
            attempt.Payable,
            decision.Rejection,
            decision.Cause,
            0,
            0,
            attempt.Before.Gil,
            now.Gil,
            attempt.Before.Owned,
            now.Owned,
            $"vendor shop {attempt.ShopId}, answered after {elapsedMs} ms, {(lastBusyAtMs == VendorPurchaseDecision.NeverBusy ? "never seen busy" : $"idle for {idleMs} ms")}");
        if (decision.Kind == PurchaseOutcomeKind.Pending)
        {
            return outcome;
        }

        decided = outcome;
        RunLog.Info($"Vendor: purchase {attempt.AttemptId} of {attempt.Quantity} x item {attempt.ItemId} is {decision.Kind} ({decision.Rejection}, {decision.Cause}); gil {attempt.Before.Gil} to {now.Gil}, owned {attempt.Before.Owned} to {now.Owned}.");
        return outcome;
    }

    public bool Acknowledge(Guid attemptId)
    {
        if (expected is not { } attempt || attempt.AttemptId != attemptId)
        {
            return false;
        }

        expected = null;
        decided = PurchaseOutcome.None;
        lastBusyAtMs = VendorPurchaseDecision.NeverBusy;
        return true;
    }

    public void Abandon(Guid attemptId) => Acknowledge(attemptId);

    private bool SampleBusy(uint shopId, long nowMs)
    {
        var busy = VendorShop.TransactionBusy(shopId);
        if (busy)
        {
            lastBusyAtMs = nowMs;
        }

        return busy;
    }
}
