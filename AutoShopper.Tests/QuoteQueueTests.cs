using AutoShopper.Core.Api.Model;

namespace AutoShopper.Tests;

public sealed class QuoteQueueTests
{
    private const string Caller = "AutoGcSupply";
    private const string OtherCaller = "OtherPlugin";

    private static readonly TimeSpan JustOver = TimeSpan.FromMilliseconds(1);
    private static readonly DateTime Start = new(2026, 10, 13, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AQuoteWaitsOutTheWindowLeftToThePlayerAfterTheCooldown()
    {
        Assert.False(QuoteQueue.MayStart(TimeSpan.Zero, pricesFetching: false, pricesRequested: false, runFetchingPrices: false));
        Assert.False(QuoteQueue.MayStart(ApiContract.QuoteYieldWindow - JustOver, pricesFetching: false, pricesRequested: false, runFetchingPrices: false));
        Assert.True(QuoteQueue.MayStart(ApiContract.QuoteYieldWindow, pricesFetching: false, pricesRequested: false, runFetchingPrices: false));
    }

    [Fact]
    public void AQuoteStartsAtOnceWhenNothingWasFetchedYet()
    {
        Assert.True(QuoteQueue.MayStart(TimeSpan.MaxValue, pricesFetching: false, pricesRequested: false, runFetchingPrices: false));
    }

    [Fact]
    public void AQuoteNeverStartsWhileThePlayersPricesAreComing()
    {
        Assert.False(QuoteQueue.MayStart(TimeSpan.MaxValue, pricesFetching: true, pricesRequested: false, runFetchingPrices: false));
    }

    [Fact]
    public void AQuoteNeverTakesTheFetchThePlayerJustAskedFor()
    {
        Assert.False(QuoteQueue.MayStart(TimeSpan.MaxValue, pricesFetching: false, pricesRequested: true, runFetchingPrices: false));
    }

    [Fact]
    public void AQuoteNeverStartsWhileARunIsGettingItsPrices()
    {
        Assert.False(QuoteQueue.MayStart(TimeSpan.MaxValue, pricesFetching: false, pricesRequested: false, runFetchingPrices: true));
    }

    [Fact]
    public void ACallerHasAtMostTwoQuotesWaiting()
    {
        var queue = new QuoteQueue();

        for (var count = 0; count < ApiContract.MaximumWaitingQuotesPerCaller; count++)
        {
            Assert.True(queue.HasRoomFor(Caller));
            queue.Add(Guid.NewGuid(), Caller, Start);
        }

        Assert.False(queue.HasRoomFor(Caller));
        Assert.True(queue.HasRoomFor(OtherCaller));
    }

    [Fact]
    public void AQuoteThatStartedMakesRoomForAnother()
    {
        var queue = new QuoteQueue();
        queue.Add(Guid.NewGuid(), Caller, Start);
        queue.Add(Guid.NewGuid(), Caller, Start);

        queue.RemoveFirst();

        Assert.True(queue.HasRoomFor(Caller));
    }

    [Fact]
    public void ACallerThatMayNoLongerBuyLosesOnlyItsOwnWaitingQuotes()
    {
        var queue = new QuoteQueue();
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        var alsoMine = Guid.NewGuid();
        queue.Add(mine, Caller, Start);
        queue.Add(other, OtherCaller, Start);
        queue.Add(alsoMine, Caller, Start);
        var removed = new List<Guid>();

        queue.RemoveCaller(Caller, removed);

        Assert.Equal(2, removed.Count);
        Assert.Contains(mine, removed);
        Assert.Contains(alsoMine, removed);
        Assert.Equal(1, queue.Count);
        Assert.Equal(other, queue.First);
        Assert.True(queue.HasRoomFor(Caller));
    }

    [Fact]
    public void ACallerWithNothingWaitingLeavesTheQueueAsItWas()
    {
        var queue = new QuoteQueue();
        var other = Guid.NewGuid();
        queue.Add(other, OtherCaller, Start);
        var removed = new List<Guid>();

        queue.RemoveCaller(Caller, removed);

        Assert.Empty(removed);
        Assert.Equal(other, queue.First);
    }

    [Fact]
    public void WaitingQuotesStartInTheOrderTheyWereAskedFor()
    {
        var queue = new QuoteQueue();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        queue.Add(first, Caller, Start);
        queue.Add(second, OtherCaller, Start);
        queue.Add(third, Caller, Start);

        Assert.Equal(first, queue.First);
        queue.RemoveFirst();
        Assert.Equal(second, queue.First);
        queue.RemoveFirst();
        Assert.Equal(third, queue.First);
        queue.RemoveFirst();
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void AWaitingQuoteNobodyAsksAboutForAMinuteIsDropped()
    {
        var store = new RecentStore<string>(ApiContract.QuoteCapacity, ApiContract.QuoteLifetime);
        var queue = new QuoteQueue();
        var abandoned = Guid.NewGuid();
        store.Add(abandoned, "abandoned", Start);
        queue.Add(abandoned, Caller, Start);
        var removed = new List<Guid>();

        queue.RemoveAbandoned(store, Start + ApiContract.UnpolledQuoteLifetime, removed);
        Assert.Empty(removed);
        Assert.Equal(1, queue.Count);

        queue.RemoveAbandoned(store, Start + ApiContract.UnpolledQuoteLifetime + JustOver, removed);
        Assert.Equal([abandoned], removed);
        Assert.Equal(0, queue.Count);
        Assert.True(queue.HasRoomFor(Caller));
    }

    [Fact]
    public void AskingForAWaitingQuoteKeepsItInLine()
    {
        var store = new RecentStore<string>(ApiContract.QuoteCapacity, ApiContract.QuoteLifetime);
        var queue = new QuoteQueue();
        var polled = Guid.NewGuid();
        var silent = Guid.NewGuid();
        store.Add(polled, "polled", Start);
        store.Add(silent, "silent", Start);
        queue.Add(polled, Caller, Start);
        queue.Add(silent, OtherCaller, Start);
        var later = Start + TimeSpan.FromSeconds(50);
        var removed = new List<Guid>();

        queue.NotePolled(polled, later);
        queue.RemoveAbandoned(store, Start + ApiContract.UnpolledQuoteLifetime + JustOver, removed);

        Assert.Equal([silent], removed);
        Assert.Equal(polled, queue.First);

        removed.Clear();
        queue.RemoveAbandoned(store, later + ApiContract.UnpolledQuoteLifetime + JustOver, removed);
        Assert.Equal([polled], removed);
    }

    [Fact]
    public void AQuoteTheStoreNoLongerHoldsLeavesTheLineAndTheCallersCap()
    {
        var store = new RecentStore<string>(ApiContract.QuoteCapacity, ApiContract.QuoteLifetime);
        var queue = new QuoteQueue();
        var pushedOut = Guid.NewGuid();
        var kept = Guid.NewGuid();
        store.Add(pushedOut, "pushed out", Start);
        queue.Add(pushedOut, Caller, Start);
        for (var count = 0; count < ApiContract.QuoteCapacity - 1; count++)
        {
            store.Add(Guid.NewGuid(), "newer", Start);
        }

        store.Add(kept, "kept", Start);
        queue.Add(kept, Caller, Start);
        Assert.False(queue.HasRoomFor(Caller));
        var removed = new List<Guid>();

        queue.RemoveAbandoned(store, Start, removed);

        Assert.Equal([pushedOut], removed);
        Assert.Equal(kept, queue.First);
        Assert.True(queue.HasRoomFor(Caller));
    }
}
