using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AutoShopper.Core.Api.Model;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Preview;
using AutoShopper.Core.Pricing;
using AutoShopper.Core.Pricing.Model;
using AutoShopper.Core.Tasks;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;

namespace AutoShopper.Core.Api;

internal sealed class ApiQuotes : IDisposable
{
    private static readonly Task<QuotePricing?> NotPriced = Task.FromResult<QuotePricing?>(null);

    private readonly PriceService prices;
    private readonly PlanPreview preview;
    private readonly AutoShopController controller;
    private readonly RecentStore<Quote> quotes = new(ApiContract.QuoteCapacity, ApiContract.QuoteLifetime);
    private readonly QuoteQueue waiting = new();
    private readonly List<Guid> dropped = [];
    private readonly IFramework.OnUpdateDelegate tick;
    private bool hooked;

    public ApiQuotes(PriceService prices, PlanPreview preview, AutoShopController controller)
    {
        this.prices = prices;
        this.preview = preview;
        this.controller = controller;
        tick = _ => StartWaiting();
    }

    public QuoteStartResponse Start(QuoteRequest request, string callerDisplayName)
    {
        if (preview.Scope is not { } scope)
        {
            return QuoteStartResponse.Refuse(ApiRefusal.LoggedOut, Loc.T(L.Api.QuoteRefused, Loc.T(L.Shop.ReasonLoggedOut)));
        }

        var caller = request.Caller!;
        if (!waiting.HasRoomFor(caller))
        {
            RunLog.Info($"Buying API: a quote from {callerDisplayName} was refused: {ApiContract.MaximumWaitingQuotesPerCaller} of its quotes already wait.");
            return QuoteStartResponse.Refuse(ApiRefusal.Busy, Loc.T(L.Api.QuoteRefused, Loc.T(L.Api.ReasonQuotesWaiting, ApiContract.MaximumWaitingQuotesPerCaller)));
        }

        var quote = new Quote(Guid.NewGuid(), request.Items!, scope.Allowed);
        quotes.Add(quote.Id, quote, DateTime.UtcNow);
        var started = waiting.Count == 0 && MayStart() && TryFetch(quote);
        if (!started)
        {
            waiting.Add(quote.Id, caller);
            Hook();
        }

        RunLog.Info($"Buying API: quote {quote.Id} for {callerDisplayName}, {quote.Items.Length} item(s) on {quote.Worlds.Length} world(s){(started ? string.Empty : ", waiting for its turn at the price fetch")}.");
        return QuoteStartResponse.Accept(quote.Id);
    }

    public QuoteResultResponse Result(Guid quoteId)
    {
        var id = ApiIds.Format(quoteId);
        if (!quotes.TryGet(quoteId, DateTime.UtcNow, out var quote))
        {
            return QuoteResultResponse.Unknown(id);
        }

        if (quote.Pricing is not { IsCompleted: true } pricing)
        {
            return new QuoteResultResponse { QuoteId = id, State = QuoteState.Pending };
        }

        if (!pricing.IsCompletedSuccessfully || pricing.Result is not { } priced)
        {
            return new QuoteResultResponse { QuoteId = id, State = QuoteState.Failed, Refusal = ApiRefusal.PricesUnavailable };
        }

        return new QuoteResultResponse
        {
            QuoteId = id,
            State = QuoteState.Ready,
            FetchedAtUtc = WholeSeconds(priced.Snapshot.FetchedAtUtc),
            Items = priced.Items,
        };
    }

    public int Drop(string caller)
    {
        dropped.Clear();
        waiting.RemoveCaller(caller, dropped);
        var now = DateTime.UtcNow;
        for (var index = 0; index < dropped.Count; index++)
        {
            if (quotes.TryGet(dropped[index], now, out var quote))
            {
                quote.Pricing = NotPriced;
            }
        }

        return dropped.Count;
    }

    public PriceSnapshot? NewestSnapshot(Func<PriceSnapshot, bool> usable)
        => quotes.NewestWhere(quote => quote.Snapshot is { } snapshot && usable(snapshot), DateTime.UtcNow)?.Snapshot;

    public void Dispose() => Unhook();

    private bool MayStart()
        => QuoteQueue.MayStart(prices.SinceCooldownEnded, prices.Fetching, controller.ShopRun is { State: RunState.FetchingPrices });

    private bool TryFetch(Quote quote)
    {
        var started = prices.TryStartDetached(quote.Worlds, ItemIdsOf(quote.Items), CancellationToken.None, out var fetch);
        if (started == PriceStartResult.CoolingDown)
        {
            return false;
        }

        if (started != PriceStartResult.Started)
        {
            RunLog.Info($"Buying API: the prices of quote {quote.Id} could not be fetched ({started}).");
        }

        quote.Pricing = started == PriceStartResult.Started ? Price(quote.Id, fetch, quote.Items) : NotPriced;
        return true;
    }

    private void StartWaiting()
    {
        var now = DateTime.UtcNow;
        Quote? first = null;
        while (waiting.Count > 0 && !quotes.TryGet(waiting.First, now, out first))
        {
            waiting.RemoveFirst();
        }

        if (first is null)
        {
            Unhook();
            return;
        }

        if (!MayStart() || !TryFetch(first))
        {
            return;
        }

        waiting.RemoveFirst();
    }

    private static async Task<QuotePricing?> Price(Guid quoteId, Task<PriceSnapshot?> fetch, QuoteRequestItem[] items)
    {
        var snapshot = await fetch.ConfigureAwait(false);
        if (snapshot is null || snapshot.FailedRequestCount > 0)
        {
            RunLog.Info($"Buying API: the prices of quote {quoteId} came back incomplete.");
            return null;
        }

        try
        {
            return new QuotePricing(snapshot, QuoteCovers.Compute(items, snapshot.Offers, snapshot.FetchedAtUtc));
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, $"Buying API: pricing quote {quoteId} failed");
            return null;
        }
    }

    private static ImmutableArray<uint> ItemIdsOf(QuoteRequestItem[] items)
    {
        var itemIds = new uint[items.Length];
        for (var index = 0; index < items.Length; index++)
        {
            itemIds[index] = items[index].ItemId;
        }

        Array.Sort(itemIds);
        return ImmutableCollectionsMarshal.AsImmutableArray(itemIds);
    }

    private static DateTime WholeSeconds(DateTime value)
        => new(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);

    private void Hook()
    {
        if (hooked)
        {
            return;
        }

        Svc.Framework.Update += tick;
        hooked = true;
    }

    private void Unhook()
    {
        if (!hooked)
        {
            return;
        }

        Svc.Framework.Update -= tick;
        hooked = false;
    }

    private sealed record QuotePricing(PriceSnapshot Snapshot, QuoteItemResult[] Items);

    private sealed class Quote(Guid id, QuoteRequestItem[] items, ImmutableArray<ScopeWorld> worlds)
    {
        public Guid Id { get; } = id;

        public QuoteRequestItem[] Items { get; } = items;

        public ImmutableArray<ScopeWorld> Worlds { get; } = worlds;

        public Task<QuotePricing?>? Pricing { get; set; }

        public PriceSnapshot? Snapshot => Pricing is { IsCompletedSuccessfully: true, Result: { } priced } ? priced.Snapshot : null;
    }
}
