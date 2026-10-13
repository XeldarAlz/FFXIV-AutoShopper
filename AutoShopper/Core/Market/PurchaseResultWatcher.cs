using System.Text;
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
    string Evidence)
{
    public static PurchaseOutcome None => new(PurchaseOutcomeKind.None, Guid.Empty, 0, 0, PurchaseRejection.None, PurchaseUnknownCause.None, 0, 0, MarketGil.Unavailable, MarketGil.Unavailable, string.Empty);
}

// Listens for the answer to the one purchase in flight: the purchase response hook first, Dalamud's request and
// purchase events second, the chat log lines third. Every raw value goes to the log at Info, attributed or not, so
// the first supervised purchases can pin what the codes mean. Framework thread only.
internal sealed unsafe class PurchaseResultWatcher : IDisposable
{
    private const int LoggedParameters = 3;

    private readonly Hook<InfoProxyItemSearch.Delegates.ProcessPurchaseResponse>? responseHook;
    private PurchaseEvidence? evidence;
    private PurchaseOutcome outcome = PurchaseOutcome.None;
    private long gilBefore = MarketGil.Unavailable;
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

    public void Arm(in PurchaseExpectation expected, long nowMs, long gil)
    {
        evidence = new PurchaseEvidence(expected, nowMs);
        gilBefore = gil;
        outcome = PurchaseOutcome.None with { Kind = PurchaseOutcomeKind.Pending, AttemptId = expected.AttemptId, Quantity = (int)expected.Listing.Quantity, Payable = expected.Payable, GilBefore = gil };
        RunLog.Info($"Market: purchase {expected.AttemptId} armed: {expected.Listing}, payable {expected.Payable}, gil {gil}.");
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

    // Once per frame while a purchase is in flight; the outcome stays the same once decided.
    public PurchaseOutcome Poll()
    {
        if (evidence is null || outcome.Kind != PurchaseOutcomeKind.Pending)
        {
            return outcome;
        }

        var decision = evidence.Decide(Environment.TickCount64);
        if (decision.Kind == PurchaseOutcomeKind.Pending)
        {
            return outcome;
        }

        var gilAfter = MarketGil.Read();
        var logMessageId = evidence.FailureLogCount > 0 ? evidence.FailureLogId : evidence.SuccessLogSeen ? PurchaseMessages.SuccessCandidate : 0u;
        outcome = outcome with
        {
            Kind = decision.Kind,
            Rejection = decision.Rejection,
            UnknownCause = decision.Cause,
            RawErrorId = evidence.ResponseErrorId,
            RawLogMessageId = logMessageId,
            GilAfter = gilAfter,
            Evidence = evidence.Describe(),
        };
        RunLog.Info($"Market: purchase {outcome.AttemptId} {decision.Kind} (rejection {decision.Rejection}, cause {decision.Cause}): {outcome.Evidence}; gil {gilBefore} to {gilAfter}, spent {gilBefore - gilAfter}, payable {outcome.Payable}.");
        return outcome;
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
