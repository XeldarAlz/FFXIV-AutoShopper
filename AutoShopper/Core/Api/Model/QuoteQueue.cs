namespace AutoShopper.Core.Api.Model;

internal readonly record struct WaitingQuote(Guid QuoteId, string Caller, DateTime LastPolledAtUtc);

internal sealed class QuoteQueue
{
    private readonly List<WaitingQuote> waiting = new(ApiContract.QuoteCapacity);

    public int Count => waiting.Count;

    public Guid First => waiting[0].QuoteId;

    public static bool MayStart(TimeSpan sinceCooldownEnded, bool pricesFetching, bool pricesRequested, bool runFetchingPrices)
        => !pricesFetching && !pricesRequested && !runFetchingPrices && sinceCooldownEnded >= ApiContract.QuoteYieldWindow;

    public bool HasRoomFor(string caller) => CountOf(caller) < ApiContract.MaximumWaitingQuotesPerCaller;

    public void Add(Guid quoteId, string caller, DateTime nowUtc) => waiting.Add(new WaitingQuote(quoteId, caller, nowUtc));

    public void RemoveFirst() => waiting.RemoveAt(0);

    public void NotePolled(Guid quoteId, DateTime nowUtc)
    {
        for (var index = 0; index < waiting.Count; index++)
        {
            if (waiting[index].QuoteId != quoteId)
            {
                continue;
            }

            waiting[index] = waiting[index] with { LastPolledAtUtc = nowUtc };
            return;
        }
    }

    public void RemoveCaller(string caller, List<Guid> removed)
        => RemoveWhere(caller, static (quote, owner) => string.Equals(quote.Caller, owner, StringComparison.Ordinal), removed);

    public void RemoveAbandoned<T>(RecentStore<T> stored, DateTime nowUtc, List<Guid> removed)
        where T : class
        => RemoveWhere((stored, nowUtc), static (quote, state) => Abandoned(quote, state.stored, state.nowUtc), removed);

    private static bool Abandoned<T>(WaitingQuote quote, RecentStore<T> stored, DateTime nowUtc)
        where T : class
        => nowUtc - quote.LastPolledAtUtc > ApiContract.UnpolledQuoteLifetime || !stored.TryGet(quote.QuoteId, nowUtc, out _);

    private void RemoveWhere<TState>(TState state, Func<WaitingQuote, TState, bool> matches, List<Guid> removed)
    {
        for (var index = waiting.Count - 1; index >= 0; index--)
        {
            if (!matches(waiting[index], state))
            {
                continue;
            }

            removed.Add(waiting[index].QuoteId);
            waiting.RemoveAt(index);
        }
    }

    private int CountOf(string caller)
    {
        var count = 0;
        for (var index = 0; index < waiting.Count; index++)
        {
            if (string.Equals(waiting[index].Caller, caller, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }
}
