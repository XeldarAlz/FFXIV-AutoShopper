using AutoShopper.Core.Api.Model;

namespace AutoShopper.Tests;

public sealed class QuoteQueueTests
{
    private const string Caller = "AutoGcSupply";
    private const string OtherCaller = "OtherPlugin";

    private static readonly TimeSpan JustOver = TimeSpan.FromMilliseconds(1);

    [Fact]
    public void AQuoteWaitsOutTheWindowLeftToThePlayerAfterTheCooldown()
    {
        Assert.False(QuoteQueue.MayStart(TimeSpan.Zero, pricesFetching: false, runFetchingPrices: false));
        Assert.False(QuoteQueue.MayStart(ApiContract.QuoteYieldWindow - JustOver, pricesFetching: false, runFetchingPrices: false));
        Assert.True(QuoteQueue.MayStart(ApiContract.QuoteYieldWindow, pricesFetching: false, runFetchingPrices: false));
    }

    [Fact]
    public void AQuoteStartsAtOnceWhenNothingWasFetchedYet()
    {
        Assert.True(QuoteQueue.MayStart(TimeSpan.MaxValue, pricesFetching: false, runFetchingPrices: false));
    }

    [Fact]
    public void AQuoteNeverStartsWhileThePlayersPricesAreComing()
    {
        Assert.False(QuoteQueue.MayStart(TimeSpan.MaxValue, pricesFetching: true, runFetchingPrices: false));
    }

    [Fact]
    public void AQuoteNeverStartsWhileARunIsGettingItsPrices()
    {
        Assert.False(QuoteQueue.MayStart(TimeSpan.MaxValue, pricesFetching: false, runFetchingPrices: true));
    }

    [Fact]
    public void ACallerHasAtMostTwoQuotesWaiting()
    {
        var queue = new QuoteQueue();

        for (var count = 0; count < ApiContract.MaximumWaitingQuotesPerCaller; count++)
        {
            Assert.True(queue.HasRoomFor(Caller));
            queue.Add(Guid.NewGuid(), Caller);
        }

        Assert.False(queue.HasRoomFor(Caller));
        Assert.True(queue.HasRoomFor(OtherCaller));
    }

    [Fact]
    public void AQuoteThatStartedMakesRoomForAnother()
    {
        var queue = new QuoteQueue();
        queue.Add(Guid.NewGuid(), Caller);
        queue.Add(Guid.NewGuid(), Caller);

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
        queue.Add(mine, Caller);
        queue.Add(other, OtherCaller);
        queue.Add(alsoMine, Caller);
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
        queue.Add(other, OtherCaller);
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
        queue.Add(first, Caller);
        queue.Add(second, OtherCaller);
        queue.Add(third, Caller);

        Assert.Equal(first, queue.First);
        queue.RemoveFirst();
        Assert.Equal(second, queue.First);
        queue.RemoveFirst();
        Assert.Equal(third, queue.First);
        queue.RemoveFirst();
        Assert.Equal(0, queue.Count);
    }
}
