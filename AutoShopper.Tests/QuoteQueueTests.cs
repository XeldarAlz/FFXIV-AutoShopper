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
