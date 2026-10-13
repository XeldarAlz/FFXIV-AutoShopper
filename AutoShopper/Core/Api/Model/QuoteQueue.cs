namespace AutoShopper.Core.Api.Model;

internal readonly record struct WaitingQuote(Guid QuoteId, string Caller);

internal sealed class QuoteQueue
{
    private readonly List<WaitingQuote> waiting = new(ApiContract.QuoteCapacity);

    public int Count => waiting.Count;

    public Guid First => waiting[0].QuoteId;

    public static bool MayStart(TimeSpan sinceCooldownEnded, bool pricesFetching, bool runFetchingPrices)
        => !pricesFetching && !runFetchingPrices && sinceCooldownEnded >= ApiContract.QuoteYieldWindow;

    public bool HasRoomFor(string caller) => CountOf(caller) < ApiContract.MaximumWaitingQuotesPerCaller;

    public void Add(Guid quoteId, string caller) => waiting.Add(new WaitingQuote(quoteId, caller));

    public void RemoveFirst() => waiting.RemoveAt(0);

    public void RemoveCaller(string caller, List<Guid> removed)
    {
        for (var index = waiting.Count - 1; index >= 0; index--)
        {
            if (!string.Equals(waiting[index].Caller, caller, StringComparison.Ordinal))
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
