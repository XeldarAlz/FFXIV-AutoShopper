using AutoShopper.Core.Market.Model;

namespace AutoShopper.Core.Vendors.Model;

internal static class VendorPurchaseDecision
{
    public const int DeadlineMs = 10_000;
    public const long NeverBusy = -1;

    public static PurchaseDecision Decide(in PurseReading before, in PurseReading now, int quantity, long payable, bool transactionBusy, long idleMs, long elapsedMs)
    {
        var pastDeadline = elapsedMs >= DeadlineMs;
        if (!before.Readable || !now.Readable)
        {
            return pastDeadline ? PurchaseDecision.Unknown(PurchaseUnknownCause.PurseUnreadable) : PurchaseDecision.Pending;
        }

        var gained = now.Owned - before.Owned;
        var charged = before.Gil - now.Gil;
        if (gained == quantity && charged == payable)
        {
            return PurchaseDecision.Confirmed;
        }

        if (transactionBusy)
        {
            return pastDeadline ? PurchaseDecision.Unknown(PurchaseUnknownCause.Timeout) : PurchaseDecision.Pending;
        }

        var unchanged = gained == 0 && charged == 0;
        if (unchanged && idleMs != NeverBusy && idleMs >= PurchaseReconciliation.SettleWindowMs)
        {
            return PurchaseDecision.Rejected(PurchaseRejection.Generic);
        }

        if (!pastDeadline)
        {
            return PurchaseDecision.Pending;
        }

        if (unchanged)
        {
            return PurchaseDecision.Unknown(PurchaseUnknownCause.Timeout);
        }

        return PurchaseDecision.Unknown(gained == quantity ? PurchaseUnknownCause.GilMismatch : PurchaseUnknownCause.InventoryMismatch);
    }
}
