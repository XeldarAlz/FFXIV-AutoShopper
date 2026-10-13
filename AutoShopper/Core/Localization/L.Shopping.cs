namespace AutoShopper.Core.Localization;

internal static partial class L
{
    internal static class Shopping
    {
        public static readonly LocString Tab = new("shopping.tab", "Shopping");
        public static readonly LocString TabSub = new("shopping.tabSub", "Spending limits, how plans are chosen, and the worlds a run may shop on.");

        public static readonly LocString Budget = new("shopping.budget", "Budget");
        public static readonly LocString SpendCap = new("shopping.spendCap", "Run spend cap");
        public static readonly LocString SpendCapHelp = new("shopping.spendCapHelp", "The most one run may spend on purchases, fees included. There is no unlimited setting: Start refuses while no cap is set. The Plan page compares its estimate against this cap.");
        public static readonly LocString SpendCapHint = new("shopping.spendCapHint", "not set");
        public static readonly LocString SpendCapMissing = new("shopping.spendCapMissing", "No spend cap is set yet. Runs will refuse to start until you set one; plans still work.");
        public static readonly LocString Reserve = new("shopping.reserve", "Gil to keep");
        public static readonly LocString ReserveHelp = new("shopping.reserveHelp", "Gil a run always leaves in your purse. A purchase that would take you below it is skipped.");

        public static readonly LocString Planning = new("shopping.planning", "Planning");
        public static readonly LocString Mode = new("shopping.mode", "Plan for");
        public static readonly LocString ModeHelp = new("shopping.modeHelp", "How a plan trades the estimated total against the number of worlds it visits. The Plan page compares all three on the same prices.");
        public static readonly LocString ModeCheapestName = new("shopping.mode.cheapest.name", "Cheapest");
        public static readonly LocString ModeCheapestDetail = new("shopping.mode.cheapest.detail", "The lowest estimated total. An extra stop only loses a tie.");
        public static readonly LocString ModeBalancedName = new("shopping.mode.balanced.name", "Balanced");
        public static readonly LocString ModeBalancedDetail = new("shopping.mode.balanced.detail", "Weighs the total against the stop and data center penalties below.");
        public static readonly LocString ModeFewestName = new("shopping.mode.fewest.name", "Fewest stops");
        public static readonly LocString ModeFewestDetail = new("shopping.mode.fewest.detail", "The fewest worlds, costing at most the premium below over the cheapest plan.");
        public static readonly LocString StopPenalty = new("shopping.stopPenalty", "Stop penalty");
        public static readonly LocString StopPenaltyHelp = new("shopping.stopPenaltyHelp", "What one more world visit is worth to you in gil. Balanced adds it for every stop away from your world. It only steers the choice; nobody is charged.");
        public static readonly LocString CrossPenalty = new("shopping.crossPenalty", "Data center change penalty");
        public static readonly LocString CrossPenaltyHelp = new("shopping.crossPenaltyHelp", "Added on top of the stop penalty for every data center change in Balanced. It only steers the choice; nobody is charged.");
        public static readonly LocString Premium = new("shopping.premium", "Fewest stops premium");
        public static readonly LocString PremiumHelp = new("shopping.premiumHelp", "How much more than the cheapest plan Fewest stops may cost, in percent.");
        public static readonly LocString PercentFormat = new("shopping.percentFormat", "%d %%");
        public static readonly LocString MaxStops = new("shopping.maxStops", "Most stops");
        public static readonly LocString MaxStopsHelp = new("shopping.maxStopsHelp", "The most worlds one plan may visit. 0 means no limit.");
        public static readonly LocString StopsFormat = new("shopping.stopsFormat", "%d");
        public static readonly LocString StaleAfter = new("shopping.staleAfter", "Prices count as old after");
        public static readonly LocString StaleAfterHelp = new("shopping.staleAfterHelp", "Prices uploaded longer ago than this are flagged as old on the Plan page. Get prices again before you trust them.");
        public static readonly LocString HoursFormat = new("shopping.hoursFormat", "%d h");

        public static readonly LocString Scope = new("shopping.scope", "Worlds to shop on");
        public static readonly LocString Preset = new("shopping.preset", "Start from");
        public static readonly LocString PresetHelp = new("shopping.presetHelp", "The worlds a plan may use before your own changes below.");
        public static readonly LocString PresetCurrentWorldName = new("shopping.preset.currentWorld.name", "Current world");
        public static readonly LocString PresetCurrentWorldDetail = new("shopping.preset.currentWorld.detail", "Only the world you are standing on.");
        public static readonly LocString PresetHomeName = new("shopping.preset.home.name", "Home data center");
        public static readonly LocString PresetHomeDetail = new("shopping.preset.home.detail", "Every world of your home data center.");
        public static readonly LocString PresetCurrentName = new("shopping.preset.current.name", "Current data center");
        public static readonly LocString PresetCurrentDetail = new("shopping.preset.current.detail", "Every world of the data center you are on.");
        public static readonly LocString PresetRegionName = new("shopping.preset.region.name", "Whole region");
        public static readonly LocString PresetRegionDetail = new("shopping.preset.region.detail", "Every world of your home region. Data center changes take longer and need travel.");
        public static readonly LocString Worlds = new("shopping.worlds", "Worlds");
        public static readonly LocString WorldsHelp = new("shopping.worldsHelp", "Click a world to add it or leave it out. Prices and plans use only the lit worlds.");
        public static readonly LocPlural WorldCount = new("shopping.worldCount", "{0} world selected", "{0} worlds selected");
        public static readonly LocString ResetWorlds = new("shopping.resetWorlds", "Back to the preset");
        public static readonly LocString HomeWorld = new("shopping.homeWorld", "Your home world");
        public static readonly LocString CurrentWorld = new("shopping.currentWorld", "You are here");
        public static readonly LocString LogInForWorlds = new("shopping.logInForWorlds", "Log in to see and pick the worlds.");
        public static readonly LocString NoWorlds = new("shopping.noWorlds", "No world is selected. Prices and plans need at least one.");
        public static readonly LocString JapanNote = new("shopping.japanNote", "Japanese data centers: World Visit between them changed on 13 October 2026. Prices and plans cover them, but runs that travel between Japanese data centers stay off until that route has been tested.");
    }
}
