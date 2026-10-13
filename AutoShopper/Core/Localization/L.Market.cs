namespace AutoShopper.Core.Localization;

internal static partial class L
{
    internal static class Market
    {
        public static readonly LocString Tab = new("market.tab", "Market board");
        public static readonly LocString TabSub = new("market.tabSub", "Dry runs, the board a run heads for, and how fast it searches and buys.");

        public static readonly LocString Buying = new("market.buying", "Buying");
        public static readonly LocString DryRun = new("market.dryRun", "Dry run");
        public static readonly LocString DryRunHelp = new("market.dryRunHelp", "The whole run goes ahead, searches and checks included, but no purchase is ever sent: each one is logged as \"would buy\" with its price. Leave it on until you have watched a dry run and checked its numbers against the board.");
        public static readonly LocString DryRunOff = new("market.dryRunOff", "Dry run is off: a run spends real gil, up to your run spend cap.");

        public static readonly LocString Board = new("market.board", "Market board");
        public static readonly LocString City = new("market.city", "Preferred city");
        public static readonly LocString CityHelp = new("market.cityHelp", "Where a run goes when no market board is in reach. A city you already stand in is kept, and a city whose aetheryte is not attuned is skipped.");
        public static readonly LocString SearchInterval = new("market.searchInterval", "Time between searches");
        public static readonly LocString SearchIntervalHelp = new("market.searchIntervalHelp", "The shortest wait between two listing searches. The board asks you to wait below 2.5 seconds, so it never goes lower.");
        public static readonly LocString PurchaseInterval = new("market.purchaseInterval", "Time between purchases");
        public static readonly LocString PurchaseIntervalHelp = new("market.purchaseIntervalHelp", "The shortest wait between two purchases. It never goes below 1.5 seconds.");
        public static readonly LocString ResultTimeout = new("market.resultTimeout", "Wait for a purchase answer");
        public static readonly LocString ResultTimeoutHelp = new("market.resultTimeoutHelp", "How long a purchase may stay unanswered before it counts as uncertain. An uncertain purchase stops all buying until you say whether it went through.");
        public static readonly LocString TenthsFormat = new("market.tenthsFormat", "%.1f s");
        public static readonly LocString SecondsFormat = new("market.secondsFormat", "%d s");

        public static readonly LocString StatusLabel = new("market.status.label", "Market board");
        public static readonly LocString StatusReading = new("market.status.reading", "listings can be read");
        public static readonly LocString StatusReadingOff = new("market.status.readingOff", "listings cannot be read ({0} failed)");
        public static readonly LocString StatusBuying = new("market.status.buying", "buying available");
        public static readonly LocString StatusBuyingOff = new("market.status.buyingOff", "buying off ({0} failed)");
        public static readonly LocString StatusOffHelp = new("market.status.offHelp", "This game version does not match what the plugin expects, so that part of the market board stays off until an update. /ashop mbstatus lists every check.");
        public static readonly LocString DryRunBadge = new("market.dryRunBadge", "DRY RUN");
    }
}
