namespace AutoShopper.Core.Localization;

internal static partial class L
{
    internal static class Plan
    {
        public static readonly LocString Title = new("plan.title", "Plan");
        public static readonly LocString Subtitle = new("plan.subtitle", "{0}  ·  estimates from Universalis. A run reads the live listings before it buys.");
        public static readonly LocString GetPrices = new("plan.getPrices", "Get prices");
        public static readonly LocString StartPlan = new("plan.start", "Start this plan");
        public static readonly LocString GetPricesHelp = new("plan.getPricesHelp", "Reads the current listings of this list's items from Universalis for the worlds chosen in Settings, Shopping. Nothing happens in game.");
        public static readonly LocString GettingPrices = new("plan.gettingPrices", "Getting prices… {0} of {1}");
        public static readonly LocString Cooldown = new("plan.cooldown", "You can get prices again in {0} s.");
        public static readonly LocString Planning = new("plan.planning", "Planning…");
        public static readonly LocString PricesSummary = new("plan.pricesSummary", "Prices fetched {0}  ·  {1} items  ·  {2} worlds");
        public static readonly LocString RequestsFailed = new("plan.requestsFailed", "{0} of {1} requests failed");
        public static readonly LocString NoPricesYet = new("plan.noPricesYet", "No prices yet. Get prices to see a plan for this list.");
        public static readonly LocString NoList = new("plan.noList", "Pick or make a shopping list first.");
        public static readonly LocString LogIn = new("plan.logIn", "Log in to plan: a plan starts from the world you are on.");
        public static readonly LocString NothingToBuy = new("plan.nothingToBuy", "Nothing on this list needs buying right now.");
        public static readonly LocString StartNoWorlds = new("plan.startNoWorlds", "No world is selected in Settings, Shopping.");
        public static readonly LocString StartNothing = new("plan.startNothing", "This list has no items to price.");
        public static readonly LocString Gil = new("plan.gil", "{0} gil");
        public static readonly LocString Percent = new("plan.percent", "{0}%");

        public static readonly LocString WarnStale = new("plan.warn.stale", "Some prices are older than {0} h. Get prices again before you rely on this plan.");
        public static readonly LocString WarnPartial = new("plan.warn.partial", "Some worlds or items came back without prices: a request failed or a world was missing. The plan only uses what came back.");
        public static readonly LocString WarnSearchLimit = new("plan.warn.searchLimit", "The search stopped at its time or size limit, so a cheaper or shorter plan may exist.");
        public static readonly LocString WarnMissingPrices = new("plan.warn.missingPrices", "Some items on the list have no prices yet. Get prices to include them.");
        public static readonly LocString WarnUnknownOwned = new("plan.warn.unknownOwned", "Top up lines are left out until the game reports what you own.");
        public static readonly LocString WarnScopeChanged = new("plan.warn.scopeChanged", "The worlds in Settings changed after the last fetch. Get prices again.");
        public static readonly LocString WarnOverBudget = new("plan.warn.overBudget", "The estimate is over your run spend cap.");
        public static readonly LocString WarnNoCap = new("plan.warn.noCap", "No run spend cap is set. Runs refuse to start until you set one in Settings, Shopping.");
        public static readonly LocString WarnBelowReserve = new("plan.warn.belowReserve", "Buying all of this would take you below the gil you keep.");

        public static readonly LocString Estimates = new("plan.estimates", "Estimates");
        public static readonly LocString EstimatesNote = new("plan.estimatesNote", "Listing price plus the 5% market fee, from Universalis uploads. A run reads the live listings before it buys.");
        public static readonly LocString TileTotal = new("plan.tile.total", "Estimated total");
        public static readonly LocString TileTotalSub = new("plan.tile.totalSub", "fee incl.");
        public static readonly LocString TileFee = new("plan.tile.fee", "Market fee");
        public static readonly LocString TileFeeSub = new("plan.tile.feeSub", "5%");
        public static readonly LocString TileStops = new("plan.tile.stops", "Stops");
        public static readonly LocPlural TileStopsSub = new("plan.tile.stopsSub", "{0} world change", "{0} world changes");
        public static readonly LocString TileDataCenters = new("plan.tile.dataCenters", "Data center changes");
        public static readonly LocPlural Partial = new("plan.partial", "Partial estimate: {0} unit is not covered and not in the total. See Not covered below.", "Partial estimate: {0} units are not covered and not in the total. See Not covered below.");

        public static readonly LocString Budget = new("plan.budget", "Budget");
        public static readonly LocString BudgetLine = new("plan.budgetLine", "Estimate {0} of the {1} cap");
        public static readonly LocString BudgetOver = new("plan.budgetOver", "Over the cap by {0}");
        public static readonly LocString GilLine = new("plan.gilLine", "You have {0}; keeping {1} leaves {2} to spend.");

        public static readonly LocString Compare = new("plan.compare", "Modes on the same prices");
        public static readonly LocString CompareNote = new("plan.compareNote", "Each mode planned on exactly these prices and worlds. Click a row to use that mode.");
        public static readonly LocString ColumnMode = new("plan.column.mode", "Mode");
        public static readonly LocString ColumnTotal = new("plan.column.total", "Estimated total");
        public static readonly LocString ColumnStops = new("plan.column.stops", "Stops");
        public static readonly LocString ColumnDataCenters = new("plan.column.dataCenters", "DC changes");
        public static readonly LocString ColumnCoverage = new("plan.column.coverage", "Coverage");
        public static readonly LocString Complete = new("plan.complete", "complete");
        public static readonly LocString MissingUnits = new("plan.missingUnits", "{0} missing");
        public static readonly LocString InUse = new("plan.inUse", "In use");
        public static readonly LocString Lowest = new("plan.lowest", "Lowest complete");

        public static readonly LocString Route = new("plan.route", "Route");
        public static readonly LocString RouteNote = new("plan.routeNote", "Stops in visit order, starting from the world you are on. Each line is the item's listings bought at that stop.");
        public static readonly LocString NoStops = new("plan.noStops", "No stop has listings that fit this list.");
        public static readonly LocPlural StopItems = new("plan.stopItems", "{0} item", "{0} items");
        public static readonly LocPlural Listings = new("plan.listings", "{0} listing", "{0} listings");
        public static readonly LocString Quantity = new("plan.quantity", "×{0}");

        public static readonly LocString NotCovered = new("plan.notCovered", "Not covered");
        public static readonly LocString NotCoveredNote = new("plan.notCoveredNote", "These units are not in the total above.");
        public static readonly LocString CoveredLine = new("plan.coveredLine", "{0} of {1} covered, {2} missing");
        public static readonly LocString ReasonInvalid = new("plan.reason.invalid", "The line's values are out of range");
        public static readonly LocString ReasonNoListings = new("plan.reason.noListings", "No listings on the chosen worlds");
        public static readonly LocString ReasonAboveCap = new("plan.reason.aboveCap", "Listings cost more than the line's max per unit (fee incl.)");
        public static readonly LocString ReasonInsufficient = new("plan.reason.insufficient", "Not enough units are listed");
        public static readonly LocString ReasonNoFit = new("plan.reason.noFit", "No mix of whole stacks fits the quantity and the extra allowance");
        public static readonly LocString ReasonStricter = new("plan.reason.stricter", "The listings went to a stricter line for the same item");
        public static readonly LocString ReasonStopLimit = new("plan.reason.stopLimit", "The most stops setting cut it");
        public static readonly LocString ReasonSearchLimit = new("plan.reason.searchLimit", "The search stopped before covering it");
        public static readonly LocString ReasonNotFetched = new("plan.reason.notFetched", "No prices fetched for it yet");
        public static readonly LocString ReasonNotMarketable = new("plan.reason.notMarketable", "Universalis does not list this item");
        public static readonly LocString ReasonRequestFailed = new("plan.reason.requestFailed", "Its price request failed");
        public static readonly LocString ReasonNoMarketData = new("plan.reason.noMarketData", "No market data uploaded for the chosen worlds");
        public static readonly LocString ReasonCapped = new("plan.reason.capped", "Capped: a world sent only its 100 cheapest listings, so more may be on the board");

        public static readonly LocString Freshness = new("plan.freshness", "Price freshness");
        public static readonly LocString FreshnessNote = new("plan.freshnessNote", "When each world's listings were last uploaded to Universalis, counting its oldest item.");
        public static readonly LocString FreshnessItems = new("plan.freshnessItems", "{0} of {1} items");
        public static readonly LocString FreshnessCapped = new("plan.freshnessCapped", "{0} of {1} items, some at the 100 listing cap");
        public static readonly LocString NoUpload = new("plan.noUpload", "no upload");
        public static readonly LocString RequestFailed = new("plan.requestFailed", "request failed");
        public static readonly LocString EmptyList = new("plan.emptyList", "This list has no lines yet. Add items on the Lists page to plan it.");
    }
}
