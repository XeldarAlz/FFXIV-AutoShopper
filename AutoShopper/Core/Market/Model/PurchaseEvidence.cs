using System.Text;

namespace AutoShopper.Core.Market.Model;

internal enum PurchaseOutcomeKind : byte
{
    None = 0,
    Pending = 1,
    Confirmed = 2,
    Rejected = 3,
    Unknown = 4,
}

internal enum PurchaseUnknownCause : byte
{
    None = 0,
    Timeout = 1,
    Contradiction = 2,
    RequestMismatch = 3,
    MissingRequest = 4,
    MissingAgreement = 5,
}

internal readonly record struct PurchaseDecision(PurchaseOutcomeKind Kind, PurchaseRejection Rejection, PurchaseUnknownCause Cause)
{
    public static PurchaseDecision Pending => new(PurchaseOutcomeKind.Pending, PurchaseRejection.None, PurchaseUnknownCause.None);

    public static PurchaseDecision Confirmed => new(PurchaseOutcomeKind.Confirmed, PurchaseRejection.None, PurchaseUnknownCause.None);

    public static PurchaseDecision Rejected(PurchaseRejection rejection) => new(PurchaseOutcomeKind.Rejected, rejection, PurchaseUnknownCause.None);

    public static PurchaseDecision Unknown(PurchaseUnknownCause cause) => new(PurchaseOutcomeKind.Unknown, PurchaseRejection.None, cause);
}

internal readonly record struct PurchaseExpectation(Guid AttemptId, ListingFields Listing, long Payable);

// Everything heard about the one purchase in flight. The purchase response hook is the primary signal; Dalamud's
// request and purchase events must agree with it before a purchase counts as confirmed, and the chat log lines may
// only corroborate. Silence past the deadline, or any two signals that disagree, leaves the purchase Unknown.
internal sealed class PurchaseEvidence(PurchaseExpectation expected, long sentAtMs)
{
    // Late log lines and the second signal normally land in the same frame as the response; this covers a slow frame.
    public const int SettleMs = 1_000;
    public const int AgreementMs = 3_000;
    public const int DeadlineMs = 15_000;

    public PurchaseExpectation Expected { get; } = expected;

    public long SentAtMs { get; } = sentAtMs;

    public int RequestCount { get; private set; }

    public ListingField RequestDifference { get; private set; }

    public int ResponseCount { get; private set; }

    public uint ResponseItemId { get; private set; }

    public uint ResponseErrorId { get; private set; }

    public long ResponseAtMs { get; private set; }

    public bool ResponsesDisagree { get; private set; }

    public int PurchasedCount { get; private set; }

    public uint PurchasedCatalogId { get; private set; }

    public uint PurchasedQuantity { get; private set; }

    public bool SuccessLogSeen { get; private set; }

    public int FailureLogCount { get; private set; }

    public uint FailureLogId { get; private set; }

    private uint ExpectedItemId => MarketItemId.Normalize(Expected.Listing.ItemId);

    public void RecordRequest(in ListingFields sent)
    {
        RequestCount++;
        if (RequestCount == 1)
        {
            RequestDifference = Expected.Listing.FirstDifference(sent);
        }
    }

    public void RecordResponse(uint itemId, uint errorMessageId, long nowMs)
    {
        ResponseCount++;
        if (ResponseCount == 1)
        {
            ResponseItemId = itemId;
            ResponseErrorId = errorMessageId;
            ResponseAtMs = nowMs;
            return;
        }

        if (itemId != ResponseItemId || errorMessageId != ResponseErrorId)
        {
            ResponsesDisagree = true;
        }
    }

    public void RecordItemPurchased(uint catalogId, uint quantity)
    {
        PurchasedCount++;
        if (PurchasedCount == 1)
        {
            PurchasedCatalogId = catalogId;
            PurchasedQuantity = quantity;
        }
    }

    public void RecordLogMessage(uint logMessageId)
    {
        if (PurchaseMessages.IsSuccessCandidate(logMessageId))
        {
            SuccessLogSeen = true;
            return;
        }

        if (!PurchaseMessages.IsPurchaseFailure(logMessageId))
        {
            return;
        }

        FailureLogCount++;
        if (FailureLogCount == 1)
        {
            FailureLogId = logMessageId;
        }
    }

    public PurchaseDecision Decide(long nowMs)
    {
        var contradiction = FindContradiction();
        if (contradiction != PurchaseUnknownCause.None)
        {
            return PurchaseDecision.Unknown(contradiction);
        }

        if (ResponseCount == 0)
        {
            return nowMs - SentAtMs >= DeadlineMs ? PurchaseDecision.Unknown(PurchaseUnknownCause.Timeout) : PurchaseDecision.Pending;
        }

        var sinceResponse = nowMs - ResponseAtMs;
        if (ResponseErrorId != 0)
        {
            return sinceResponse >= SettleMs ? PurchaseDecision.Rejected(Rejection()) : PurchaseDecision.Pending;
        }

        if (RequestCount == 0)
        {
            return sinceResponse >= SettleMs ? PurchaseDecision.Unknown(PurchaseUnknownCause.MissingRequest) : PurchaseDecision.Pending;
        }

        if (PurchasedCount == 0)
        {
            return sinceResponse >= AgreementMs ? PurchaseDecision.Unknown(PurchaseUnknownCause.MissingAgreement) : PurchaseDecision.Pending;
        }

        return sinceResponse >= SettleMs ? PurchaseDecision.Confirmed : PurchaseDecision.Pending;
    }

    public string Describe()
    {
        var builder = new StringBuilder();
        builder.Append(ResponseCount == 0 ? "no response" : $"response item {ResponseItemId} code {ResponseErrorId} x{ResponseCount}");
        builder.Append(PurchasedCount == 0 ? "; no purchase event" : $"; purchase event item {PurchasedCatalogId} quantity {PurchasedQuantity} x{PurchasedCount}");
        builder.Append(RequestCount == 0 ? "; no request event" : $"; request event x{RequestCount} difference {RequestDifference}");
        builder.Append(SuccessLogSeen ? "; success line seen" : string.Empty);
        builder.Append(FailureLogCount == 0 ? string.Empty : $"; failure line {FailureLogId} x{FailureLogCount}");
        return builder.ToString();
    }

    private PurchaseUnknownCause FindContradiction()
    {
        if (RequestCount > 0 && RequestDifference != ListingField.None)
        {
            return PurchaseUnknownCause.RequestMismatch;
        }

        if (RequestCount > 1 || PurchasedCount > 1 || ResponsesDisagree)
        {
            return PurchaseUnknownCause.Contradiction;
        }

        if (ResponseCount == 0)
        {
            return PurchaseUnknownCause.None;
        }

        var purchaseMatches = PurchasedCount == 1
            && MarketItemId.Matches(PurchasedCatalogId, ExpectedItemId, Expected.Listing.IsHq)
            && PurchasedQuantity == Expected.Listing.Quantity;
        if (ResponseErrorId == 0)
        {
            var successHolds = MarketItemId.Matches(ResponseItemId, ExpectedItemId, Expected.Listing.IsHq)
                && FailureLogCount == 0
                && (PurchasedCount == 0 || purchaseMatches);
            return successHolds ? PurchaseUnknownCause.None : PurchaseUnknownCause.Contradiction;
        }

        var refusalHolds = (ResponseItemId == 0 || MarketItemId.Matches(ResponseItemId, ExpectedItemId, Expected.Listing.IsHq))
            && !purchaseMatches
            && !SuccessLogSeen;
        return refusalHolds ? PurchaseUnknownCause.None : PurchaseUnknownCause.Contradiction;
    }

    private PurchaseRejection Rejection()
    {
        var rejection = PurchaseMessages.ClassifyResponse(ResponseErrorId);
        if (rejection == PurchaseRejection.Unclassified && FailureLogCount > 0)
        {
            return PurchaseMessages.Classify(FailureLogId);
        }

        return rejection;
    }
}
