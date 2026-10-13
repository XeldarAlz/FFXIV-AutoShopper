using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AutoShopper.Core.Api.Model;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Preview;
using AutoShopper.Core.Pricing;
using AutoShopper.Core.Pricing.Model;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;

namespace AutoShopper.Core.Api;

internal sealed class ApiQuotes : IDisposable
{
    private readonly PriceService prices;
    private readonly PlanPreview preview;
    private readonly RecentStore<Quote> quotes = new(ApiContract.QuoteCapacity, ApiContract.QuoteLifetime);
    private readonly List<Quote> waiting = [];
    private readonly IFramework.OnUpdateDelegate tick;
    private bool hooked;

    public ApiQuotes(PriceService prices, PlanPreview preview)
    {
        this.prices = prices;
        this.preview = preview;
        tick = _ => StartWaiting();
    }

    public QuoteStartResponse Start(QuoteRequest request, string callerDisplayName)
    {
        if (preview.Scope is not { } scope)
        {
            return QuoteStartResponse.Refuse(ApiRefusal.LoggedOut, Loc.T(L.Api.QuoteRefused, Loc.T(L.Shop.ReasonLoggedOut)));
        }

        var quote = new Quote(Guid.NewGuid(), request.Items!, scope.Allowed);
        quotes.Add(quote.Id, quote, DateTime.UtcNow);
        var started = TryFetch(quote);
        if (!started)
        {
            waiting.Add(quote);
            Hook();
        }

        RunLog.Info($"Buying API: quote {quote.Id} for {callerDisplayName}, {quote.Items.Length} item(s) on {quote.Worlds.Length} world(s){(started ? string.Empty : ", waiting for the price fetch cooldown")}.");
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

    public PriceSnapshot? NewestSnapshot(Func<PriceSnapshot, bool> usable)
        => quotes.NewestWhere(quote => quote.Snapshot is { } snapshot && usable(snapshot), DateTime.UtcNow)?.Snapshot;

    public void Dispose() => Unhook();

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

        quote.Pricing = started == PriceStartResult.Started ? Price(quote.Id, fetch, quote.Items) : Task.FromResult<QuotePricing?>(null);
        return true;
    }

    private void StartWaiting()
    {
        var now = DateTime.UtcNow;
        while (waiting.Count > 0 && !quotes.TryGet(waiting[0].Id, now, out _))
        {
            waiting.RemoveAt(0);
        }

        if (waiting.Count == 0)
        {
            Unhook();
            return;
        }

        if (prices.CooldownRemaining > TimeSpan.Zero || !TryFetch(waiting[0]))
        {
            return;
        }

        waiting.RemoveAt(0);
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
