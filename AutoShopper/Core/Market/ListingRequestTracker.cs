using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Market.Model;
using Dalamud.Game.Network.Structures;
using Dalamud.Hooking;
using ECommons;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using AddonRow = Lumina.Excel.Sheets.Addon;

namespace AutoShopper.Core.Market;

internal readonly record struct ListingRequestState(
    ListingRequestStatus Status,
    uint ItemId,
    int Generation,
    ListingRequestFailure Failure,
    ListingSnapshot? Snapshot);

// Direct market searches through the search proxy, without the result window. The summary hook marks the start of
// a search, Dalamud's offerings pages fill it, and the EndRequest slot hook marks its end; a quiet window covers a
// missing signal. Framework thread only, and no proxy pointer is kept past the frame that read it.
internal sealed unsafe class ListingRequestTracker : IDisposable
{
    // Addon rows that read "Please wait and try your search again." in the client language.
    private const uint ThrottleNoticeRow = 1_997;
    private const uint ThrottleNoticeAlternateRow = 1_998;
    private const int ThrottleNoticeCheckMs = 250;

    private readonly ListingRequestMachine machine = new();
    private readonly OfferedListing[] pageBuffer = new OfferedListing[ListingRequestMachine.MaximumListings];
    private readonly ObservedListing[] captureBuffer = new ObservedListing[ListingRequestMachine.MaximumListings];
    private readonly Hook<InfoProxyItemSearch.Delegates.ProcessRequestResult>? requestResultHook;
    private readonly Hook<InfoProxyItemSearch.Delegates.EndRequest>? endRequestHook;
    private ListingSnapshot? snapshot;
    private string[]? throttleNotices;
    private bool issuingRequest;
    private long nextThrottleCheckMs;
    private int reportedFailures;
    private bool offeringsThreadReported;
    private bool requestResultThreadReported;

    private readonly Configuration configuration;

    public ListingRequestTracker(Configuration configuration)
    {
        this.configuration = configuration;
        requestResultHook = MarketHooks.FromAddress<InfoProxyItemSearch.Delegates.ProcessRequestResult>(
            InfoProxyItemSearch.Addresses.ProcessRequestResult.Value,
            OnProcessRequestResult,
            "ProcessRequestResult");
        endRequestHook = MarketHooks.FromSlot<InfoProxyItemSearch.Delegates.EndRequest>(MarketCompatibility.EndRequestSlot, OnEndRequest, "EndRequest");
        MarketHooks.Enable(requestResultHook);
        MarketHooks.Enable(endRequestHook);
        Svc.MarketBoard.OfferingsReceived += OnOfferingsReceived;
    }

    public bool HasRequestResultHook => requestResultHook is not null;

    public bool HasEndRequestHook => endRequestHook is not null;

    public int Generation => machine.Generation;

    public bool IsBusy => machine.Status == ListingRequestStatus.Waiting;

    // The last copy, only while the proxy still holds the search it was taken from.
    public ListingSnapshot? Snapshot => snapshot is not null && snapshot.Generation == machine.Generation ? snapshot : null;

    public ListingRequestState State
    {
        get
        {
            var status = machine.Status;
            if (status == ListingRequestStatus.Complete && Snapshot is null)
            {
                return new ListingRequestState(ListingRequestStatus.Failed, machine.ItemId, machine.Generation, ListingRequestFailure.Superseded, null);
            }

            var current = status == ListingRequestStatus.Complete ? Snapshot : null;
            return new ListingRequestState(status, machine.ItemId, machine.Generation, machine.Failure, current);
        }
    }

    public void Dispose()
    {
        Svc.MarketBoard.OfferingsReceived -= OnOfferingsReceived;
        requestResultHook?.Dispose();
        endRequestHook?.Dispose();
    }

    public ListingRequestState Request(uint itemId)
    {
        snapshot = null;
        reportedFailures = 0;
        machine.IntervalMs = configuration.SearchIntervalMs;
        machine.Begin(itemId);
        var wait = Math.Max(0, machine.NextSendAtMs - Environment.TickCount64);
        RunLog.Info($"Market: search for item {itemId} queued; it goes out in {wait} ms.");
        return Poll();
    }

    public void Cancel()
    {
        if (machine.Status != ListingRequestStatus.Waiting)
        {
            return;
        }

        machine.Cancel();
        RunLog.Info($"Market: search for item {machine.ItemId} cancelled.");
    }

    // Once per frame while a search is waiting: sends it when pacing allows and copies the proxy once it is complete.
    public ListingRequestState Poll()
    {
        if (machine.Status != ListingRequestStatus.Waiting)
        {
            return State;
        }

        var now = Environment.TickCount64;
        var proxy = InfoProxyItemSearch.Instance();
        if (proxy == null)
        {
            machine.OnUnavailable();
            RunLog.Warning($"Market: search for item {machine.ItemId} failed: the search proxy is not available.");
            return State;
        }

        if (machine.IsSending && ThrottleNoticeShown(now))
        {
            RunLog.Info($"Market: the result window asks to wait before searching item {machine.ItemId} again.");
            machine.OnThrottleNotice(now);
        }

        var listingCount = (int)Math.Min(proxy->ListingCount, int.MaxValue);
        switch (machine.Poll(now, proxy->SearchItemId, listingCount))
        {
            case ListingRequestStep.Send:
                Send(proxy, now);
                break;
            case ListingRequestStep.Capture:
                Capture(proxy, now);
                break;
        }

        ReportFailures();
        return State;
    }

    // The live listing with this id under the current generation, read from the proxy, never by index. A proxy that
    // holds another item has moved on before the generation did, so the copy is stale rather than the listing gone.
    public LiveListingRead ReadLive(ulong listingId, out ObservedListing listing)
    {
        listing = default;
        var proxy = InfoProxyItemSearch.Instance();
        if (Snapshot is null || proxy == null || proxy->SearchItemId != machine.ItemId)
        {
            return LiveListingRead.Stale;
        }

        var index = IndexOfListing(proxy, listingId);
        if (index < 0)
        {
            return LiveListingRead.Gone;
        }

        listing = Observe(proxy->Listings[index], Svc.PlayerState.CurrentWorld.RowId, machine.Generation, DateTime.UtcNow);
        return LiveListingRead.Found;
    }

    public static int IndexOfListing(InfoProxyItemSearch* proxy, ulong listingId)
    {
        var listings = proxy->Listings;
        var count = (int)Math.Min(proxy->ListingCount, (uint)listings.Length);
        for (var index = 0; index < count; index++)
        {
            if (listings[index].ListingId == listingId)
            {
                return index;
            }
        }

        return -1;
    }

    public static ObservedListing Observe(in MarketBoardListing listing, uint worldId, int generation, DateTime observedAtUtc)
    {
        return new ObservedListing(
            listing.ListingId,
            listing.RetainerId,
            listing.ContentId,
            listing.ItemId,
            listing.IsHqItem,
            (int)Math.Min(listing.Quantity, int.MaxValue),
            listing.UnitPrice,
            listing.TotalTax,
            listing.IsMannequin,
            listing.IsSellingAsSet,
            worldId,
            generation,
            observedAtUtc);
    }

    private void Send(InfoProxyItemSearch* proxy, long now)
    {
        bool accepted;
        issuingRequest = true;
        try
        {
            proxy->EndRequest();
            proxy->SearchItemId = machine.ItemId;
            accepted = proxy->RequestData();
        }
        finally
        {
            issuingRequest = false;
        }

        machine.OnSent(now, accepted);
        RunLog.Info($"Market: listings requested for item {machine.ItemId}: generation {machine.Generation}, attempt {machine.Attempts}, accepted {accepted}, request ids next {proxy->NextRequestId} current {proxy->CurrentRequestId}.");
    }

    private void Capture(InfoProxyItemSearch* proxy, long now)
    {
        var listings = proxy->Listings;
        if (proxy->SearchItemId != machine.ItemId || proxy->ListingCount > (uint)listings.Length)
        {
            RunLog.Info($"Market: copy of item {machine.ItemId} refused: the proxy holds item {proxy->SearchItemId} with {proxy->ListingCount} listing(s).");
            machine.OnCaptureFailed(now);
            return;
        }

        var count = (int)proxy->ListingCount;
        var worldId = Svc.PlayerState.CurrentWorld.RowId;
        var capturedAtUtc = DateTime.UtcNow;
        for (var index = 0; index < count; index++)
        {
            captureBuffer[index] = Observe(listings[index], worldId, machine.Generation, capturedAtUtc);
        }

        var captured = captureBuffer.AsSpan(0, count);
        var check = ListingCrossCheck.Compare(captured, machine.Offered);
        if (check != CrossCheckResult.Match)
        {
            RunLog.Info($"Market: copy of item {machine.ItemId} refused ({check}): proxy {count}, pages {machine.Offered.Length}, announced {machine.ExpectedCount}.");
            machine.OnCaptureFailed(now);
            return;
        }

        snapshot = ListingSnapshot.Create(machine.ItemId, worldId, machine.Generation, capturedAtUtc, Svc.PlayerState.ContentId, machine.ExpectedCount, captured);
        machine.OnCaptured();
        var endedBy = machine.EndSeen ? "end of search" : machine.StartSeen ? "announced count" : "quiet window";
        RunLog.Info($"Market: item {machine.ItemId} on world {worldId}: {count} listing(s), announced {machine.ExpectedCount}, generation {machine.Generation}, completed by {endedBy}.");
    }

    private void ReportFailures()
    {
        if (machine.Failures <= reportedFailures)
        {
            reportedFailures = machine.Failures;
            return;
        }

        reportedFailures = machine.Failures;
        if (machine.Status == ListingRequestStatus.Failed)
        {
            RunLog.Warning($"Market: search for item {machine.ItemId} gave up after {machine.Attempts} attempt(s): {machine.Failure}.");
            return;
        }

        var wait = Math.Max(0, machine.NextSendAtMs - Environment.TickCount64);
        RunLog.Info($"Market: search for item {machine.ItemId} failed ({machine.LastFailure}, error code {machine.ErrorCode}); trying again in {wait} ms.");
    }

    private bool ThrottleNoticeShown(long now)
    {
        if (now < nextThrottleCheckMs)
        {
            return false;
        }

        nextThrottleCheckMs = now + ThrottleNoticeCheckMs;
        if (!GenericHelpers.TryGetAddonByName<AddonItemSearchResult>(MarketBoardDriver.ItemSearchResultAddon, out var addon)
            || !GenericHelpers.IsAddonReady(&addon->AtkUnitBase))
        {
            return false;
        }

        var error = addon->ErrorMessage;
        var hits = addon->HitsMessage;
        if (error == null || !error->AtkResNode.IsVisible() || (hits != null && hits->AtkResNode.IsVisible()))
        {
            return false;
        }

        var shown = error->NodeText.ToString();
        var notices = throttleNotices ??= LoadThrottleNotices();
        for (var index = 0; index < notices.Length; index++)
        {
            if (notices[index].Length > 0 && shown.Contains(notices[index], StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] LoadThrottleNotices()
    {
        var sheet = Svc.Data.GetExcelSheet<AddonRow>();
        return
        [
            sheet.GetRowOrDefault(ThrottleNoticeRow)?.Text.ExtractText() ?? string.Empty,
            sheet.GetRowOrDefault(ThrottleNoticeAlternateRow)?.Text.ExtractText() ?? string.Empty,
        ];
    }

    private void OnOfferingsReceived(IMarketBoardCurrentOfferings offerings)
    {
        try
        {
            MarketHooks.ReportThreadOnce(ref offeringsThreadReported, "OfferingsReceived");
            var listings = offerings.ItemListings;
            var count = Math.Min(listings.Count, pageBuffer.Length);
            for (var index = 0; index < count; index++)
            {
                var listing = listings[index];
                pageBuffer[index] = new OfferedListing(
                    listing.ListingId,
                    listing.RetainerId,
                    listing.ItemId,
                    listing.IsHq,
                    listing.ItemQuantity,
                    listing.PricePerUnit,
                    listing.TotalTax,
                    listing.OnMannequin);
            }

            var disposition = machine.OnPage(offerings.RequestId, pageBuffer.AsSpan(0, count), Environment.TickCount64);
            RunLog.Debug($"Market: page of {count} listing(s), request {offerings.RequestId}: {disposition}.");
            ReportFailures();
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, "Market: reading a page of offerings failed");
        }
    }

    private void OnProcessRequestResult(InfoProxyItemSearch* proxy, byte resultCount, int errorCode)
    {
        try
        {
            MarketHooks.ReportThreadOnce(ref requestResultThreadReported, "ProcessRequestResult");
            var attributed = machine.OnRequestResult(resultCount, errorCode, Environment.TickCount64);
            RunLog.Info($"Market: search summary for item {proxy->SearchItemId}: {resultCount} listing(s), error code {errorCode}{(attributed ? string.Empty : ", not a search of this plugin")}.");
            ReportFailures();
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, "Market: handling a search summary failed");
        }

        requestResultHook!.Original(proxy, resultCount, errorCode);
    }

    private void OnEndRequest(InfoProxyItemSearch* proxy)
    {
        if (!issuingRequest)
        {
            try
            {
                if (machine.OnEndRequest(Environment.TickCount64))
                {
                    RunLog.Debug($"Market: search for item {machine.ItemId} ended after {machine.Offered.Length} listing(s).");
                }
            }
            catch (Exception exception)
            {
                RunLog.Error(exception, "Market: handling the end of a search failed");
            }
        }

        endRequestHook!.Original(proxy);
    }
}
