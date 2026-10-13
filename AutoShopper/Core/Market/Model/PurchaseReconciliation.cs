namespace AutoShopper.Core.Market.Model;

// What the purse and the bags held for the item of a purchase; -1 for what could not be read.
internal readonly record struct PurseReading(long Gil, int Owned)
{
    public static PurseReading Unreadable => new(-1, -1);

    public bool Readable => Gil >= 0 && Owned >= 0;
}

// The game's answer to a purchase must show in the purse and the bags too: a confirmed purchase takes exactly the
// payable and adds exactly the listing's quantity, a refused one changes neither. Both update a few frames after the
// answer, so an answer they do not match yet is given SettleWindowMs to catch up; past that it is uncertain.
internal static class PurchaseReconciliation
{
    // A hitch of a few frames, the inventory update included, fits well inside this.
    public const int SettleWindowMs = 3_000;

    public static PurchaseDecision Reconcile(in PurchaseDecision answer, long payable, int quantity, in PurseReading before, in PurseReading now, long sinceAnswerMs)
    {
        if (answer.Kind is not (PurchaseOutcomeKind.Confirmed or PurchaseOutcomeKind.Rejected))
        {
            return answer;
        }

        var mismatch = Mismatch(answer.Kind == PurchaseOutcomeKind.Confirmed, payable, quantity, before, now);
        if (mismatch == PurchaseUnknownCause.None)
        {
            return answer;
        }

        return sinceAnswerMs < SettleWindowMs ? PurchaseDecision.Pending : PurchaseDecision.Unknown(mismatch);
    }

    // What the purse lost between the two readings, -1 when either could not be read.
    public static long Charged(in PurseReading before, in PurseReading now)
        => before.Gil >= 0 && now.Gil >= 0 ? before.Gil - now.Gil : -1;

    // A purchase without a clear answer is booked at no less than its payable, and at what the purse lost when that is more.
    public static long Booked(long payable, long charged) => Math.Max(payable, charged);

    private static PurchaseUnknownCause Mismatch(bool confirmed, long payable, int quantity, in PurseReading before, in PurseReading now)
    {
        if (!before.Readable || !now.Readable)
        {
            return PurchaseUnknownCause.PurseUnreadable;
        }

        if (before.Gil - now.Gil != (confirmed ? payable : 0))
        {
            return PurchaseUnknownCause.GilMismatch;
        }

        return (long)now.Owned - before.Owned == (confirmed ? quantity : 0) ? PurchaseUnknownCause.None : PurchaseUnknownCause.InventoryMismatch;
    }
}
