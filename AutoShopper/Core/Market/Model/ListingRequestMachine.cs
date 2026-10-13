namespace AutoShopper.Core.Market.Model;

internal enum ListingRequestStatus : byte
{
    Idle = 0,
    Waiting = 1,
    Complete = 2,
    Failed = 3,
}

internal enum ListingRequestFailure : byte
{
    None = 0,
    ProxyUnavailable = 1,
    RequestRefused = 2,
    Throttled = 3,
    Timeout = 4,
    Superseded = 5,
    SnapshotMismatch = 6,
    Cancelled = 7,
}

internal enum ListingRequestStep : byte
{
    None = 0,
    Send = 1,
    Capture = 2,
}

internal enum PageDisposition : byte
{
    Accepted = 0,
    NotWaiting = 1,
    OtherItem = 2,
    OtherRequest = 3,
    Overflow = 4,
    Empty = 5,
}

// Bookkeeping for one market search at a time, fed with the signals the game raises and the clock, and telling its
// owner when to send and when the proxy is ready to be copied. Every send starts a new generation, and so does any
// search the plugin did not send, so a copy taken earlier can be recognised as stale.
internal sealed class ListingRequestMachine
{
    public const int MaximumListings = 100;
    // Public tools space searches 2 to 4 s apart; faster ones are answered with "please wait and try again".
    public const int MinimumIntervalMs = 2_500;
    public const int TimeoutMs = 10_000;
    public const int QuietWindowMs = 1_000;
    // After the end of a search, how long the proxy may lag behind the pages before the copy is taken anyway.
    public const int SettleMs = 1_000;
    public const int BackoffBaseMs = 1_000;
    public const int BackoffStepMs = 500;
    public const int BackoffMaximumMs = 5_000;
    public const int MaximumAttempts = 4;

    private const int NoRequestId = -1;
    private const long NeverMs = long.MinValue / 2;

    private readonly OfferedListing[] offered = new OfferedListing[MaximumListings];
    private int intervalMs = MinimumIntervalMs;
    private Phase phase;
    private int offeredCount;
    private int failures;
    private long lastSendAtMs = NeverMs;
    private long notBeforeMs = NeverMs;
    private long sentAtMs;
    private long lastPageAtMs;
    private int lastProxyCount;
    private long proxyCountChangedAtMs;
    private bool completionSignaled;
    private long completionAtMs;

    private enum Phase : byte
    {
        Idle = 0,
        Pending = 1,
        Waiting = 2,
        Complete = 3,
        Failed = 4,
    }

    public uint ItemId { get; private set; }

    public int Generation { get; private set; }

    public int Attempts { get; private set; }

    public int Failures => failures;

    public ListingRequestFailure Failure { get; private set; }

    public ListingRequestFailure LastFailure { get; private set; }

    public int ExpectedCount { get; private set; } = -1;

    public int ErrorCode { get; private set; }

    public int RequestId { get; private set; } = NoRequestId;

    public bool StartSeen { get; private set; }

    public bool EndSeen { get; private set; }

    public ListingRequestStatus Status => phase switch
    {
        Phase.Pending or Phase.Waiting => ListingRequestStatus.Waiting,
        Phase.Complete => ListingRequestStatus.Complete,
        Phase.Failed => ListingRequestStatus.Failed,
        _ => ListingRequestStatus.Idle,
    };

    public bool IsSending => phase == Phase.Waiting;

    // A setting may ask for longer pauses between searches, never for shorter ones than the floor.
    public int IntervalMs
    {
        get => intervalMs;
        set => intervalMs = Math.Max(MinimumIntervalMs, value);
    }

    public long NextSendAtMs => Math.Max(lastSendAtMs + intervalMs, notBeforeMs);

    public ReadOnlySpan<OfferedListing> Offered => offered.AsSpan(0, offeredCount);

    public void Begin(uint itemId)
    {
        ItemId = itemId;
        Attempts = 0;
        failures = 0;
        Failure = ListingRequestFailure.None;
        LastFailure = ListingRequestFailure.None;
        ResetSend(0);
        phase = Phase.Pending;
    }

    public void Cancel()
    {
        if (phase is not (Phase.Pending or Phase.Waiting))
        {
            return;
        }

        LastFailure = ListingRequestFailure.Cancelled;
        phase = Phase.Idle;
    }

    public ListingRequestStep Poll(long nowMs, uint proxySearchItemId, int proxyListingCount)
    {
        if (phase == Phase.Pending)
        {
            return nowMs >= NextSendAtMs ? ListingRequestStep.Send : ListingRequestStep.None;
        }

        if (phase != Phase.Waiting)
        {
            return ListingRequestStep.None;
        }

        if (proxyListingCount != lastProxyCount)
        {
            lastProxyCount = proxyListingCount;
            proxyCountChangedAtMs = nowMs;
        }

        if (proxySearchItemId != ItemId)
        {
            RegisterFailure(ListingRequestFailure.Superseded, nowMs);
            return ListingRequestStep.None;
        }

        if (nowMs - sentAtMs >= TimeoutMs)
        {
            RegisterFailure(ListingRequestFailure.Timeout, nowMs);
            return ListingRequestStep.None;
        }

        if (completionSignaled)
        {
            return proxyListingCount == offeredCount || nowMs - completionAtMs >= SettleMs ? ListingRequestStep.Capture : ListingRequestStep.None;
        }

        return QuietWindowElapsed(nowMs, proxyListingCount) ? ListingRequestStep.Capture : ListingRequestStep.None;
    }

    public void OnSent(long nowMs, bool accepted)
    {
        if (phase != Phase.Pending)
        {
            return;
        }

        Attempts++;
        Generation++;
        lastSendAtMs = nowMs;
        ResetSend(nowMs);
        phase = Phase.Waiting;
        if (!accepted)
        {
            RegisterFailure(ListingRequestFailure.RequestRefused, nowMs);
        }
    }

    public void OnUnavailable()
    {
        LastFailure = ListingRequestFailure.ProxyUnavailable;
        Failure = ListingRequestFailure.ProxyUnavailable;
        phase = Phase.Failed;
    }

    // True when the summary belongs to the search in flight.
    public bool OnRequestResult(byte resultCount, int errorCode, long nowMs)
    {
        if (phase != Phase.Waiting)
        {
            NoteForeignActivity(nowMs);
            return false;
        }

        if (StartSeen)
        {
            RegisterFailure(ListingRequestFailure.Superseded, nowMs);
            return true;
        }

        StartSeen = true;
        ExpectedCount = resultCount;
        ErrorCode = errorCode;
        if (errorCode != 0)
        {
            RegisterFailure(ListingRequestFailure.Throttled, nowMs);
            return true;
        }

        if (offeredCount >= ExpectedCount)
        {
            SignalCompletion(nowMs);
        }

        return true;
    }

    // The first page of the requested item fixes the request id; a page of another item or request is left out.
    public PageDisposition OnPage(int pageRequestId, ReadOnlySpan<OfferedListing> listings, long nowMs)
    {
        if (phase != Phase.Waiting)
        {
            NoteForeignActivity(nowMs);
            return PageDisposition.NotWaiting;
        }

        if (listings.IsEmpty)
        {
            return PageDisposition.Empty;
        }

        if (MarketItemId.Normalize(listings[0].ItemId) != ItemId)
        {
            return PageDisposition.OtherItem;
        }

        if (RequestId == NoRequestId)
        {
            RequestId = pageRequestId;
        }
        else if (pageRequestId != RequestId)
        {
            return PageDisposition.OtherRequest;
        }

        lastPageAtMs = nowMs;
        var copied = Math.Min(MaximumListings - offeredCount, listings.Length);
        listings[..copied].CopyTo(offered.AsSpan(offeredCount));
        offeredCount += copied;
        if (copied < listings.Length)
        {
            RegisterFailure(ListingRequestFailure.SnapshotMismatch, nowMs);
            return PageDisposition.Overflow;
        }

        if (StartSeen && offeredCount >= ExpectedCount)
        {
            SignalCompletion(nowMs);
        }

        return PageDisposition.Accepted;
    }

    // True when the end belongs to the search in flight; an end before any sign of it belongs to something older.
    public bool OnEndRequest(long nowMs)
    {
        if (phase != Phase.Waiting || (!StartSeen && offeredCount == 0))
        {
            return false;
        }

        EndSeen = true;
        SignalCompletion(nowMs);
        return true;
    }

    public void OnThrottleNotice(long nowMs)
    {
        if (phase == Phase.Waiting)
        {
            RegisterFailure(ListingRequestFailure.Throttled, nowMs);
        }
    }

    public void OnCaptured()
    {
        if (phase != Phase.Waiting)
        {
            return;
        }

        failures = 0;
        phase = Phase.Complete;
    }

    public void OnCaptureFailed(long nowMs)
    {
        if (phase == Phase.Waiting)
        {
            RegisterFailure(ListingRequestFailure.SnapshotMismatch, nowMs);
        }
    }

    // A search someone else made also counts against the server's pacing, and it rewrites the proxy under any copy
    // taken before it.
    private void NoteForeignActivity(long nowMs)
    {
        lastSendAtMs = Math.Max(lastSendAtMs, nowMs);
        if (phase != Phase.Pending)
        {
            Generation++;
        }
    }

    private bool QuietWindowElapsed(long nowMs, int proxyListingCount)
    {
        return offeredCount > 0
            && proxyListingCount == offeredCount
            && nowMs - lastPageAtMs >= QuietWindowMs
            && nowMs - proxyCountChangedAtMs >= QuietWindowMs;
    }

    private void SignalCompletion(long nowMs)
    {
        if (completionSignaled)
        {
            return;
        }

        completionSignaled = true;
        completionAtMs = nowMs;
    }

    private void RegisterFailure(ListingRequestFailure reason, long nowMs)
    {
        failures++;
        LastFailure = reason;
        var backoff = Math.Min(BackoffBaseMs + ((long)BackoffStepMs * failures), BackoffMaximumMs);
        notBeforeMs = Math.Max(notBeforeMs, nowMs + backoff);
        if (Attempts >= MaximumAttempts)
        {
            Failure = reason;
            phase = Phase.Failed;
            return;
        }

        phase = Phase.Pending;
    }

    private void ResetSend(long nowMs)
    {
        offeredCount = 0;
        StartSeen = false;
        EndSeen = false;
        completionSignaled = false;
        completionAtMs = 0;
        RequestId = NoRequestId;
        ExpectedCount = -1;
        ErrorCode = 0;
        sentAtMs = nowMs;
        lastPageAtMs = nowMs;
        lastProxyCount = -1;
        proxyCountChangedAtMs = nowMs;
    }
}
