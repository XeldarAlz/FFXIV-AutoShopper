using AutoShopper.Core.Execution.Model;

namespace AutoShopper.Tests;

public sealed class AethernetRideWatchTests
{
    private const long AbandonedIdleMs = 5_000;

    [Fact]
    public void TheStopInReachWithTheScreenUpIsAnArrival()
    {
        var watch = new AethernetRideWatch();

        Assert.Equal(RideProgress.Underway, watch.Observe(0, lifestreamBusy: true, settled: true, atStop: false, AbandonedIdleMs));
        Assert.Equal(RideProgress.Arrived, watch.Observe(3_000, lifestreamBusy: false, settled: true, atStop: true, AbandonedIdleMs));
    }

    [Fact]
    public void TheLoadAfterLifestreamGoesIdleIsNoIdleTime()
    {
        var watch = new AethernetRideWatch();

        watch.Observe(0, lifestreamBusy: true, settled: true, atStop: false, AbandonedIdleMs);
        Assert.Equal(RideProgress.Underway, watch.Observe(900, lifestreamBusy: false, settled: false, atStop: false, AbandonedIdleMs));
        Assert.Equal(RideProgress.Underway, watch.Observe(5_500, lifestreamBusy: false, settled: false, atStop: false, AbandonedIdleMs));
        Assert.Equal(RideProgress.Arrived, watch.Observe(6_000, lifestreamBusy: false, settled: true, atStop: true, AbandonedIdleMs));
    }

    [Fact]
    public void IdleTimeCountsOnlyFromTheSettledMoment()
    {
        var watch = new AethernetRideWatch();

        watch.Observe(0, lifestreamBusy: true, settled: true, atStop: false, AbandonedIdleMs);
        watch.Observe(900, lifestreamBusy: false, settled: false, atStop: false, AbandonedIdleMs);
        Assert.Equal(RideProgress.Underway, watch.Observe(6_000, lifestreamBusy: false, settled: true, atStop: false, AbandonedIdleMs));
        Assert.Equal(RideProgress.Underway, watch.Observe(10_999, lifestreamBusy: false, settled: true, atStop: false, AbandonedIdleMs));
        Assert.Equal(RideProgress.Abandoned, watch.Observe(11_000, lifestreamBusy: false, settled: true, atStop: false, AbandonedIdleMs));
    }

    [Fact]
    public void BusyAgainStartsTheIdleClockOver()
    {
        var watch = new AethernetRideWatch();

        watch.Observe(0, lifestreamBusy: true, settled: true, atStop: false, AbandonedIdleMs);
        watch.Observe(1_000, lifestreamBusy: false, settled: true, atStop: false, AbandonedIdleMs);
        watch.Observe(4_000, lifestreamBusy: true, settled: true, atStop: false, AbandonedIdleMs);
        Assert.Equal(RideProgress.Underway, watch.Observe(8_000, lifestreamBusy: false, settled: true, atStop: false, AbandonedIdleMs));
        Assert.Equal(RideProgress.Abandoned, watch.Observe(13_000, lifestreamBusy: false, settled: true, atStop: false, AbandonedIdleMs));
    }

    [Fact]
    public void ARideThatNeverShowedBusyIsNeverCalledAbandoned()
    {
        var watch = new AethernetRideWatch();

        Assert.Equal(RideProgress.Underway, watch.Observe(0, lifestreamBusy: false, settled: true, atStop: false, AbandonedIdleMs));
        Assert.Equal(RideProgress.Underway, watch.Observe(60_000, lifestreamBusy: false, settled: true, atStop: false, AbandonedIdleMs));
        Assert.Equal(RideProgress.Arrived, watch.Observe(61_000, lifestreamBusy: false, settled: true, atStop: true, AbandonedIdleMs));
    }
}
