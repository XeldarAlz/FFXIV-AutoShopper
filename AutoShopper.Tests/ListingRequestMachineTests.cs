using AutoShopper.Core.Market.Model;

namespace AutoShopper.Tests;

public sealed class ListingRequestMachineTests
{
    private const uint Item = 5_000;
    private const uint OtherItem = 6_000;
    private const int RequestId = 7;

    [Fact]
    public void FirstSearchIsSentAtOnceAndTheNextIsPaced()
    {
        var machine = new ListingRequestMachine();
        machine.Begin(Item);

        Assert.Equal(ListingRequestStep.Send, machine.Poll(0, 0, 0));
        machine.OnSent(0, true);
        machine.OnRequestResult(0, 0, 100);
        Assert.Equal(ListingRequestStep.Capture, machine.Poll(150, Item, 0));
        machine.OnCaptured();
        Assert.Equal(ListingRequestStatus.Complete, machine.Status);

        machine.Begin(OtherItem);
        Assert.Equal(ListingRequestStep.None, machine.Poll(ListingRequestMachine.MinimumIntervalMs - 1, Item, 0));
        Assert.Equal(ListingRequestStep.Send, machine.Poll(ListingRequestMachine.MinimumIntervalMs, Item, 0));
    }

    [Fact]
    public void SearchCompletesWhenTheAnnouncedListingsArrive()
    {
        var machine = Sent();

        Assert.True(machine.OnRequestResult(12, 0, 100));
        Assert.Equal(PageDisposition.Accepted, machine.OnPage(RequestId, Page(Item, 1, 10), 150));
        Assert.Equal(ListingRequestStep.None, machine.Poll(155, Item, 10));
        Assert.Equal(PageDisposition.Accepted, machine.OnPage(RequestId, Page(Item, 11, 2), 160));

        Assert.Equal(ListingRequestStep.Capture, machine.Poll(170, Item, 12));
        Assert.Equal(12, machine.Offered.Length);
        Assert.Equal(12, machine.ExpectedCount);
    }

    [Fact]
    public void EndOfTheSearchCompletesIt()
    {
        var machine = Sent();
        machine.OnRequestResult(15, 0, 100);
        machine.OnPage(RequestId, Page(Item, 1, 10), 150);

        Assert.True(machine.OnEndRequest(200));
        Assert.True(machine.EndSeen);
        Assert.Equal(ListingRequestStep.Capture, machine.Poll(210, Item, 10));
    }

    [Fact]
    public void ProxyLaggingBehindTheEndIsGivenTheSettleWindow()
    {
        var machine = Sent();
        machine.OnRequestResult(10, 0, 100);
        machine.OnPage(RequestId, Page(Item, 1, 10), 150);

        Assert.Equal(ListingRequestStep.None, machine.Poll(150 + ListingRequestMachine.SettleMs - 1, Item, 9));
        Assert.Equal(ListingRequestStep.Capture, machine.Poll(150 + ListingRequestMachine.SettleMs, Item, 9));
    }

    [Fact]
    public void EndBeforeAnySignOfTheSearchIsNotItsEnd()
    {
        var machine = Sent();

        Assert.False(machine.OnEndRequest(50));
        Assert.Equal(ListingRequestStep.None, machine.Poll(60, Item, 0));
    }

    [Fact]
    public void QuietWindowCompletesASearchWithoutSummaryOrEnd()
    {
        var machine = Sent();
        machine.OnPage(RequestId, Page(Item, 1, 3), 100);

        Assert.Equal(ListingRequestStep.None, machine.Poll(500, Item, 3));
        Assert.Equal(ListingRequestStep.None, machine.Poll(1_100, Item, 3));
        Assert.Equal(ListingRequestStep.Capture, machine.Poll(1_500, Item, 3));
    }

    [Fact]
    public void ThrottledSearchBacksOffAndIsSentAgainUnderANewGeneration()
    {
        var machine = Sent();

        machine.OnRequestResult(0, 1, 100);

        Assert.Equal(ListingRequestStatus.Waiting, machine.Status);
        Assert.Equal(ListingRequestFailure.Throttled, machine.LastFailure);
        Assert.Equal(ListingRequestStep.None, machine.Poll(ListingRequestMachine.MinimumIntervalMs - 1, Item, 0));
        Assert.Equal(ListingRequestStep.Send, machine.Poll(ListingRequestMachine.MinimumIntervalMs, Item, 0));
        machine.OnSent(ListingRequestMachine.MinimumIntervalMs, true);
        Assert.Equal(2, machine.Generation);
        Assert.Equal(2, machine.Attempts);
    }

    [Fact]
    public void BackoffGrowsWithEachFailureAndIsBounded()
    {
        var machine = Sent();
        var now = 0L;
        for (var failure = 1; failure < ListingRequestMachine.MaximumAttempts; failure++)
        {
            machine.OnThrottleNotice(now);
            var expected = Math.Min(ListingRequestMachine.BackoffBaseMs + (ListingRequestMachine.BackoffStepMs * failure), ListingRequestMachine.BackoffMaximumMs);
            Assert.True(machine.NextSendAtMs >= now + expected);
            now = machine.NextSendAtMs;
            Assert.Equal(ListingRequestStep.Send, machine.Poll(now, Item, 0));
            machine.OnSent(now, true);
        }

        Assert.True(machine.NextSendAtMs - now <= ListingRequestMachine.BackoffMaximumMs);
    }

    [Fact]
    public void SearchGivesUpAfterTheLastAttempt()
    {
        var machine = Sent();
        var now = 0L;
        for (var attempt = 1; attempt < ListingRequestMachine.MaximumAttempts; attempt++)
        {
            machine.OnRequestResult(0, 1, now);
            now = machine.NextSendAtMs;
            machine.Poll(now, Item, 0);
            machine.OnSent(now, true);
        }

        machine.OnRequestResult(0, 1, now);

        Assert.Equal(ListingRequestStatus.Failed, machine.Status);
        Assert.Equal(ListingRequestFailure.Throttled, machine.Failure);
        Assert.Equal(ListingRequestStep.None, machine.Poll(now + 60_000, Item, 0));
    }

    [Fact]
    public void SilentSearchTimesOutAndIsRetried()
    {
        var machine = Sent();

        Assert.Equal(ListingRequestStep.None, machine.Poll(ListingRequestMachine.TimeoutMs, Item, 0));

        Assert.Equal(ListingRequestFailure.Timeout, machine.LastFailure);
        Assert.Equal(ListingRequestStatus.Waiting, machine.Status);
    }

    [Fact]
    public void RefusedSendCountsAsAFailedAttempt()
    {
        var machine = new ListingRequestMachine();
        machine.Begin(Item);
        machine.Poll(0, 0, 0);

        machine.OnSent(0, false);

        Assert.Equal(ListingRequestFailure.RequestRefused, machine.LastFailure);
        Assert.Equal(1, machine.Attempts);
        Assert.Equal(ListingRequestStep.None, machine.Poll(100, Item, 0));
    }

    [Fact]
    public void ChangedSearchItemSupersedesTheSearch()
    {
        var machine = Sent();

        machine.Poll(100, OtherItem, 0);

        Assert.Equal(ListingRequestFailure.Superseded, machine.LastFailure);
    }

    [Fact]
    public void SecondSummaryDuringTheSearchSupersedesIt()
    {
        var machine = Sent();
        machine.OnRequestResult(5, 0, 100);

        Assert.True(machine.OnRequestResult(0, 0, 120));

        Assert.Equal(ListingRequestFailure.Superseded, machine.LastFailure);
    }

    [Fact]
    public void PagesOfAnotherItemOrRequestAreLeftOut()
    {
        var machine = Sent();

        Assert.Equal(PageDisposition.OtherItem, machine.OnPage(RequestId, Page(OtherItem, 1, 10), 100));
        Assert.Equal(PageDisposition.Accepted, machine.OnPage(RequestId, Page(Item, 1, 10), 110));
        Assert.Equal(PageDisposition.OtherRequest, machine.OnPage(RequestId + 1, Page(Item, 11, 10), 120));
        Assert.Equal(PageDisposition.Empty, machine.OnPage(RequestId, [], 130));

        Assert.Equal(RequestId, machine.RequestId);
        Assert.Equal(10, machine.Offered.Length);
    }

    [Fact]
    public void HighQualityPagesMatchTheBaseItem()
    {
        var machine = Sent();

        Assert.Equal(PageDisposition.Accepted, machine.OnPage(RequestId, Page(Item + MarketItemId.HighQualityOffset, 1, 2), 100));
    }

    [Fact]
    public void ForeignSearchMakesEarlierCopiesStaleAndCountsTowardsPacing()
    {
        var machine = Sent();
        machine.OnRequestResult(0, 0, 100);
        machine.Poll(110, Item, 0);
        machine.OnCaptured();
        var generation = machine.Generation;

        Assert.False(machine.OnRequestResult(3, 0, 5_000));
        Assert.Equal(PageDisposition.NotWaiting, machine.OnPage(9, Page(OtherItem, 1, 3), 5_010));

        Assert.True(machine.Generation > generation);
        machine.Begin(Item);
        Assert.Equal(ListingRequestStep.None, machine.Poll(5_010 + ListingRequestMachine.MinimumIntervalMs - 1, Item, 3));
        Assert.Equal(ListingRequestStep.Send, machine.Poll(5_010 + ListingRequestMachine.MinimumIntervalMs, Item, 3));
    }

    [Fact]
    public void FailedCaptureRetriesTheSearch()
    {
        var machine = Sent();
        machine.OnRequestResult(0, 0, 100);

        machine.OnCaptureFailed(110);

        Assert.Equal(ListingRequestFailure.SnapshotMismatch, machine.LastFailure);
        Assert.Equal(ListingRequestStatus.Waiting, machine.Status);
    }

    [Fact]
    public void CancelledSearchIsIdle()
    {
        var machine = Sent();

        machine.Cancel();

        Assert.Equal(ListingRequestStatus.Idle, machine.Status);
        Assert.Equal(ListingRequestFailure.Cancelled, machine.LastFailure);
        Assert.False(machine.OnEndRequest(10));
    }

    private static ListingRequestMachine Sent()
    {
        var machine = new ListingRequestMachine();
        machine.Begin(Item);
        Assert.Equal(ListingRequestStep.Send, machine.Poll(0, 0, 0));
        machine.OnSent(0, true);
        Assert.Equal(1, machine.Generation);
        return machine;
    }

    private static OfferedListing[] Page(uint itemId, ulong firstListingId, int count)
    {
        var page = new OfferedListing[count];
        for (var index = 0; index < count; index++)
        {
            page[index] = new OfferedListing(firstListingId + (ulong)index, 500, itemId, false, 5, 1_000, 50, false);
        }

        return page;
    }
}
