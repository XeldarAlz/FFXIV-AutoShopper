using System.Text;
using AutoShopper.Core.Inventory;
using AutoShopper.Core.Market.Model;
using Dalamud.Game.Chat;
using Dalamud.Game.Network.Structures;
using Dalamud.Hooking;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace AutoShopper.Core.Market;

internal readonly record struct PurchaseOutcome(
    PurchaseOutcomeKind Kind,
    Guid AttemptId,
    int Quantity,
    long Payable,
    PurchaseRejection Rejection,
    PurchaseUnknownCause UnknownCause,
    uint RawErrorId,
    uint RawLogMessageId,
    long GilBefore,
    long GilAfter,
    int OwnedBefore,
    int OwnedAfter,
    string Evidence)
{
    public static PurchaseOutcome None => new(PurchaseOutcomeKind.None, Guid.Empty, 0, 0, PurchaseRejection.None, PurchaseUnknownCause.None, 0, 0, MarketGil.Unavailable, MarketGil.Unavailable, -1, -1, string.Empty);
}

// Listens for the answer to the one purchase in flight: the purchase response hook first, Dalamud's request and
// purchase events second, the chat log lines third. An answer counts only once the purse and the bags agree with it.
// Every raw value goes to the log at Info, attributed or not, so the first supervised purchases can pin what the codes
// mean. Framework thread only.
internal sealed unsafe class PurchaseResultWatcher : IDisposable
{
    private const int LoggedParameters = 3;

    private readonly Hook<InfoProxyItemSearch.Delegates.ProcessPurchaseResponse>? responseHook;
    private PurchaseEvidence? evidence;
    private PurchaseOutcome outcome = PurchaseOutcome.None;
    private PurseReading before = PurseReading.Unreadable;
    private long answeredAtMs;
    private bool responseThreadReported;
    private bool requestThreadReported;
    private bool purchaseThreadReported;
    private bool logThreadReported;

    public PurchaseResultWatcher()
    {
        responseHook = MarketHooks.FromAddress<InfoProxyItemSearch.Delegates.ProcessPurchaseResponse>(
            InfoProxyItemSearch.Addresses.ProcessPurchaseResponse.Value,
            OnProcessPurchaseResponse,
            "ProcessPurchaseResponse");
        MarketHooks.Enable(responseHook);
        Svc.MarketBoard.PurchaseRequested += OnPurchaseRequested;
        Svc.MarketBoard.ItemPurchased += OnItemPurchased;
        Svc.Chat.LogMessage += OnLogMessage;
    }

    public bool HasResponseHook => responseHook is not null;

    // An attempt stays here until the run acknowledges its outcome, and no other purchase is sent meanwhile.
    public bool HasAttempt => evidence is not null;

    public Guid AttemptId => evidence?.Expected.AttemptId ?? Guid.Empty;

    public bool RequestObserved => evidence is { RequestCount: > 0 };

    private bool Collecting => evidence is not null && outcome.Kind == PurchaseOutcomeKind.Pending;

    public void Dispose()
    {
        Svc.MarketBoard.PurchaseRequested -= OnPurchaseRequested;
        Svc.MarketBoard.ItemPurchased -= OnItemPurchased;
        Svc.Chat.LogMessage -= OnLogMessage;
        responseHook?.Dispose();
    }

    public void Arm(in PurchaseExpectation expected, long nowMs, int deadlineMs)
    {
        evidence = new PurchaseEvidence(expected, nowMs, deadlineMs);
        before = ReadPurse(expected.Listing.ItemId);
        answeredAtMs = 0;
        outcome = PurchaseOutcome.None with
        {
            Kind = PurchaseOutcomeKind.Pending,
            AttemptId = expected.AttemptId,
            Quantity = (int)expected.Listing.Quantity,
            Payable = expected.Payable,
            GilBefore = before.Gil,
            OwnedBefore = before.Owned,
        };
        RunLog.Info($"Market: purchase {expected.AttemptId} armed: {expected.Listing}, payable {expected.Payable}, gil {before.Gil}, owned {before.Owned}, answer due within {evidence.ResultDeadlineMs} ms.");
    }

    // Only for an attempt whose packet provably never left.
    public void Disarm(Guid attemptId)
    {
        if (evidence is null || evidence.Expected.AttemptId != attemptId)
        {
            return;
        }

        evidence = null;
        outcome = PurchaseOutcome.None;
        RunLog.Info($"Market: purchase {attemptId} disarmed; nothing was sent.");
    }

    // Once per frame while a purchase is in flight; the outcome stays the same once decided. The signals are weighed
    // again on every poll of the settle window, so one that contradicts the answer late still leaves it uncertain.
    public PurchaseOutcome Poll()
    {
        if (evidence is null || outcome.Kind != PurchaseOutcomeKind.Pending)
        {
            return outcome;
        }

        var now = Environment.TickCount64;
        var answer = evidence.Decide(now);
        if (answer.Kind == PurchaseOutcomeKind.Pending)
        {
            return outcome;
        }

        if (answeredAtMs == 0)
        {
            answeredAtMs = now;
        }

        var expected = evidence.Expected;
        var after = ReadPurse(expected.Listing.ItemId);
        var decision = PurchaseReconciliation.Reconcile(answer, expected.Payable, (int)expected.Listing.Quantity, before, after, now - answeredAtMs);
        if (decision.Kind == PurchaseOutcomeKind.Pending)
        {
            return outcome;
        }

        var logMessageId = evidence.FailureLogCount > 0 ? evidence.FailureLogId : evidence.SuccessLogSeen ? PurchaseMessages.SuccessCandidate : 0u;
        outcome = outcome with
        {
            Kind = decision.Kind,
            Rejection = decision.Rejection,
            UnknownCause = decision.Cause,
            RawErrorId = evidence.ResponseErrorId,
            RawLogMessageId = logMessageId,
            GilAfter = after.Gil,
            OwnedAfter = after.Owned,
            Evidence = evidence.Describe(),
        };
        RunLog.Info($"Market: purchase {outcome.AttemptId} {decision.Kind} (answer {answer.Kind}, rejection {decision.Rejection}, cause {decision.Cause}, {now - answeredAtMs} ms after the answer): {outcome.Evidence}; gil {before.Gil} to {after.Gil}, spent {PurchaseReconciliation.Charged(before, after)}, payable {expected.Payable}; owned {before.Owned} to {after.Owned}, quantity {expected.Listing.Quantity}.");
        return outcome;
    }

    // For an attempt whose run is gone before its answer settled. Its journal holds it without an outcome, which reads
    // as uncertain, so the watcher only lets go of it and the next purchase is not refused for it.
    public void Abandon(Guid attemptId)
    {
        if (evidence is null || evidence.Expected.AttemptId != attemptId)
        {
            return;
        }

        RunLog.Warning($"Market: purchase {attemptId} let go before its answer settled ({evidence.Describe()}); the journal keeps it as uncertain.");
        evidence = null;
        outcome = PurchaseOutcome.None;
    }

    // An attempt still held when no run waits for it has nothing left to report to.
    public void AbandonAny()
    {
        if (evidence is not null)
        {
            Abandon(evidence.Expected.AttemptId);
        }
    }

    public bool Acknowledge(Guid attemptId)
    {
        if (evidence is null || evidence.Expected.AttemptId != attemptId || outcome.Kind == PurchaseOutcomeKind.Pending)
        {
            return false;
        }

        evidence = null;
        outcome = PurchaseOutcome.None;
        return true;
    }

    private static PurseReading ReadPurse(uint listedItemId)
    {
        var inventory = InventoryReader.ReadLive(MarketItemId.Normalize(listedItemId));
        return inventory.Available ? new PurseReading(inventory.Gil, inventory.Owned) : PurseReading.Unreadable;
    }

    private string Attribution()
    {
        if (evidence is null)
        {
            return "no purchase of this plugin in flight";
        }

        return outcome.Kind == PurchaseOutcomeKind.Pending
            ? $"purchase {evidence.Expected.AttemptId}"
            : $"late for purchase {evidence.Expected.AttemptId}, already {outcome.Kind}";
    }

    private void OnProcessPurchaseResponse(InfoProxyItemSearch* proxy, uint itemId, uint errorMessageId)
    {
        try
        {
            MarketHooks.ReportThreadOnce(ref responseThreadReported, "ProcessPurchaseResponse");
            RunLog.Info($"Market: purchase response item {itemId}, code {errorMessageId}, gil {MarketGil.Read()} ({Attribution()}).");
            if (Collecting)
            {
                evidence!.RecordResponse(itemId, errorMessageId, Environment.TickCount64);
            }
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, "Market: handling a purchase response failed");
        }

        responseHook!.Original(proxy, itemId, errorMessageId);
    }

    private void OnPurchaseRequested(IMarketBoardPurchaseHandler request)
    {
        try
        {
            MarketHooks.ReportThreadOnce(ref requestThreadReported, "PurchaseRequested");
            var sent = new ListingFields(request.ListingId, request.RetainerId, request.CatalogId, request.ItemQuantity, request.PricePerUnit, request.TotalTax, request.IsHq);
            RunLog.Info($"Market: purchase request leaving: {sent}, town {request.RetainerCityId} ({Attribution()}).");
            if (Collecting)
            {
                evidence!.RecordRequest(sent);
            }
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, "Market: handling a purchase request failed");
        }
    }

    private void OnItemPurchased(IMarketBoardPurchase purchase)
    {
        try
        {
            MarketHooks.ReportThreadOnce(ref purchaseThreadReported, "ItemPurchased");
            RunLog.Info($"Market: purchase event item {purchase.CatalogId}, quantity {purchase.ItemQuantity}, gil {MarketGil.Read()} ({Attribution()}).");
            if (Collecting)
            {
                evidence!.RecordItemPurchased(purchase.CatalogId, purchase.ItemQuantity);
            }
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, "Market: handling a purchase event failed");
        }
    }

    private void OnLogMessage(ILogMessage message)
    {
        try
        {
            var logMessageId = message.LogMessageId;
            if (!PurchaseMessages.IsMarketMessage(logMessageId))
            {
                return;
            }

            MarketHooks.ReportThreadOnce(ref logThreadReported, "LogMessage");
            RunLog.Info($"Market: log message {logMessageId} ({PurchaseMessages.Classify(logMessageId)}){Parameters(message)}, gil {MarketGil.Read()} ({Attribution()}).");
            if (Collecting)
            {
                evidence!.RecordLogMessage(logMessageId);
            }
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, "Market: handling a log message failed");
        }
    }

    private static string Parameters(ILogMessage message)
    {
        var builder = new StringBuilder();
        var count = Math.Min(message.ParameterCount, LoggedParameters);
        for (var index = 0; index < count; index++)
        {
            if (message.TryGetIntParameter(index, out var value))
            {
                builder.Append($", parameter {index} = {value}");
            }
        }

        return builder.ToString();
    }
}
