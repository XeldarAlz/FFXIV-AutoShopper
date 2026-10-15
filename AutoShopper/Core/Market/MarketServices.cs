using AutoShopper.Core.Vendors;

namespace AutoShopper.Core.Market;

// Owns the market engine for the plugin's lifetime. The hooks stay enabled the whole session: a search or purchase
// the player makes by hand must still mark the run's copy stale and reach the log, and switching a hook off around a
// purchase in flight could lose its answer. Each detour is a few field writes.
internal sealed class MarketServices : IDisposable
{
    public MarketServices(Configuration configuration)
    {
        Tracker = new ListingRequestTracker(configuration);
        Watcher = new PurchaseResultWatcher();
        Compatibility = MarketCompatibility.Evaluate(Tracker.HasRequestResultHook, Tracker.HasEndRequestHook, Watcher.HasResponseHook);
        Sender = new PurchaseSender(Tracker, Watcher, Compatibility, configuration);
        Driver = new MarketBoardDriver();
        Report();
    }

    public MarketCompatibilityReport Compatibility { get; }

    public MarketBoardDriver Driver { get; }

    public ListingRequestTracker Tracker { get; }

    public PurchaseSender Sender { get; }

    public PurchaseResultWatcher Watcher { get; }

    public VendorPurchaseWatcher VendorWatcher { get; } = new();

    public IPurchaseWatcher WatcherFor(PurchaseChannel channel) => channel == PurchaseChannel.Vendor ? VendorWatcher : Watcher;

    public void Dispose()
    {
        Watcher.Dispose();
        Tracker.Dispose();
    }

    private void Report()
    {
        var compatibility = Compatibility;
        if (compatibility.ExecutionAvailable)
        {
            RunLog.Info($"Market: ready; {compatibility.Reason}.");
            return;
        }

        RunLog.Warning($"Market: {compatibility.Reason}.");
        for (var index = 0; index < compatibility.Checks.Length; index++)
        {
            var check = compatibility.Checks[index];
            if (!check.Passed)
            {
                RunLog.Warning($"Market: check failed: {check.Name}.");
            }
        }
    }
}
