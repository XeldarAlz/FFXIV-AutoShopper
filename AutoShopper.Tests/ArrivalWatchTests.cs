using AutoShopper.Core.Execution.Model;

namespace AutoShopper.Tests;

public sealed class ArrivalWatchTests
{
    private const long SettleMs = 3_000;

    [Fact]
    public void AnArrivalCountsOnceTheWorldStaysQuietForTheWholeWindow()
    {
        var watch = new ArrivalWatch();

        Assert.False(watch.Observe(1_000, onTargetWorld: true, betweenAreas: false, lifestreamBusy: false, SettleMs));
        Assert.False(watch.Observe(3_999, onTargetWorld: true, betweenAreas: false, lifestreamBusy: false, SettleMs));
        Assert.True(watch.Observe(4_000, onTargetWorld: true, betweenAreas: false, lifestreamBusy: false, SettleMs));
    }

    [Fact]
    public void LifestreamGoingIdleOnTheWrongWorldIsNoArrival()
    {
        var watch = new ArrivalWatch();

        Assert.False(watch.Observe(0, onTargetWorld: false, betweenAreas: false, lifestreamBusy: false, SettleMs));
        Assert.False(watch.Observe(10_000, onTargetWorld: false, betweenAreas: false, lifestreamBusy: false, SettleMs));
    }

    [Fact]
    public void BusyOrLoadingStartsTheWindowAgain()
    {
        var watch = new ArrivalWatch();

        watch.Observe(0, onTargetWorld: true, betweenAreas: false, lifestreamBusy: false, SettleMs);
        Assert.False(watch.Observe(2_000, onTargetWorld: true, betweenAreas: false, lifestreamBusy: true, SettleMs));
        Assert.False(watch.Observe(2_500, onTargetWorld: true, betweenAreas: false, lifestreamBusy: false, SettleMs));
        Assert.False(watch.Observe(4_000, onTargetWorld: true, betweenAreas: true, lifestreamBusy: false, SettleMs));
        Assert.False(watch.Observe(4_500, onTargetWorld: true, betweenAreas: false, lifestreamBusy: false, SettleMs));
        Assert.True(watch.Observe(7_500, onTargetWorld: true, betweenAreas: false, lifestreamBusy: false, SettleMs));
    }
}
