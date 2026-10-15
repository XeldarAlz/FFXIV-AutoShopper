namespace AutoShopper.Core.Localization;

internal static partial class L
{
    internal static class Buyer
    {
        public static readonly LocString StatusPreflight = new("buyer.status.preflight", "Checking the run");
        public static readonly LocString StatusPrices = new("buyer.status.prices", "Getting fresh prices");
        public static readonly LocString StatusPlanning = new("buyer.status.planning", "Planning the run");
        public static readonly LocString StatusSearching = new("buyer.status.searching", "Searching the listings of {0}");
        public static readonly LocString StatusChoosing = new("buyer.status.choosing", "Choosing listings of {0}");
        public static readonly LocString StatusGuarding = new("buyer.status.guarding", "Checking a listing of {0}");
        public static readonly LocString StatusBuying = new("buyer.status.buying", "Buying {0} x {1}");
        public static readonly LocString StatusAwaiting = new("buyer.status.awaiting", "Waiting for the answer on {0} x {1}");
        public static readonly LocString StatusBackOff = new("buyer.status.backOff", "The board asked to wait; searching again shortly");
        public static readonly LocString StatusSettling = new("buyer.status.settling", "Waiting for the answer on the purchase in flight");
        public static readonly LocString StatusGoingToVendor = new("buyer.status.goingToVendor", "Going to {0} in {1}");
        public static readonly LocString StatusBuyingVendor = new("buyer.status.buyingVendor", "Buying {0} x {1} from {2}");

        public static readonly LocString ChatStarted = new("buyer.chat.started", "Run started for {0}: spending up to {1}.");
        public static readonly LocString ChatResumed = new("buyer.chat.resumed", "Resuming the interrupted run.");
        public static readonly LocString ChatRefused = new("buyer.chat.refused", "Cannot start: {0}.");
        public static readonly LocString ChatEnded = new("buyer.chat.ended", "Run ended ({0}): {1} spent on {2} units.");
        public static readonly LocString ChatStopping = new("buyer.chat.stopping", "Stopping once the purchase in flight is answered.");
        public static readonly LocString ChatUncertain = new("buyer.chat.uncertain", "A purchase got no clear answer. Buying stays off until you say on the Shop page whether it went through.");
        public static readonly LocString ChatInterrupted = new("buyer.chat.interrupted", "The run was interrupted. The Shop page offers to resume it.");
        public static readonly LocString ChatTaskFailed = new("buyer.chat.taskFailed", "A task could not be started. The log has the details.");

        public static readonly LocString RefusePrices = new("buyer.refuse.prices", "fresh prices could not be fetched");
        public static readonly LocString RefusePlan = new("buyer.refuse.plan", "the plan could not be made");
        public static readonly LocString RefuseNothing = new("buyer.refuse.nothing", "nothing on this list needs buying");
        public static readonly LocString RefuseOwned = new("buyer.refuse.owned", "the game has not reported what you own yet");
        public static readonly LocString RefuseCharacter = new("buyer.refuse.character", "the run belongs to another character");
        public static readonly LocString RefuseAuthorization = new("buyer.refuse.authorization", "the run could not be authorized ({0})");
        public static readonly LocString RefuseJournal = new("buyer.refuse.journal", "the purchase journal could not be written");
        public static readonly LocString RefuseNoVendor = new("buyer.refuse.noVendor", "no NPC vendor you can reach sells anything on this list");
    }

    internal static class Issues
    {
        public static readonly LocString PriceCapExceeded = new("issue.priceCapExceeded", "Above the line's max per unit");
        public static readonly LocString NoStock = new("issue.noStock", "Not enough listed");
        public static readonly LocString InventoryFull = new("issue.inventoryFull", "Bags full");
        public static readonly LocString InsufficientGil = new("issue.insufficientGil", "Not enough gil");
        public static readonly LocString TravelFailed = new("issue.travelFailed", "Could not travel there");
        public static readonly LocString DependencyBusy = new("issue.dependencyBusy", "A helper plugin was busy");
        public static readonly LocString UncertainPurchase = new("issue.uncertainPurchase", "Purchase without a clear answer");
        public static readonly LocString UnsupportedGameVersion = new("issue.unsupportedGameVersion", "Not supported on this game version");
        public static readonly LocString BudgetReached = new("issue.budgetReached", "Run spend cap reached");
        public static readonly LocString SearchFailed = new("issue.searchFailed", "Listing search failed");
        public static readonly LocString RetriesExhausted = new("issue.retriesExhausted", "Gave up after repeated refusals");
        public static readonly LocString ListingRejected = new("issue.listingRejected", "The board refused a purchase");
        public static readonly LocString UniqueOwned = new("issue.uniqueOwned", "Unique item already owned");
        public static readonly LocString BoardUnreachable = new("issue.boardUnreachable", "Market board not reached");
        public static readonly LocString JournalWriteFailed = new("issue.journalWriteFailed", "The purchase journal could not be written");
        public static readonly LocString CharacterChanged = new("issue.characterChanged", "Character or world changed");
        public static readonly LocString NotMarketable = new("issue.notMarketable", "Not sold on the market board");
        public static readonly LocString CrystalLimit = new("issue.crystalLimit", "More than the crystal pouch holds");
        public static readonly LocString VendorUnreachable = new("issue.vendorUnreachable", "NPC vendor not reached");
        public static readonly LocString VendorRefused = new("issue.vendorRefused", "The NPC vendor refused a purchase");
        public static readonly LocString OnWorld = new("issue.onWorld", "on {0}");
        public static readonly LocPlural Missing = new("issue.missing", "{0} unit missing", "{0} units missing");
    }

    internal static class Reconcile
    {
        public static readonly LocString Title = new("reconcile.title", "A purchase needs checking");
        public static readonly LocString Body = new("reconcile.body", "The game gave no clear answer to this purchase, so buying stays off until you say whether it went through. Your gil, your bags and the chat log tell you.");
        public static readonly LocString Attempt = new("reconcile.attempt", "{0} x {1} for {2}");
        public static readonly LocString When = new("reconcile.when", "On {0}, {1}");
        public static readonly LocString GilEvidence = new("reconcile.gil", "Gil before it {0}, now {1}");
        public static readonly LocString OwnedEvidence = new("reconcile.owned", "Owned before it {0}, now {1}");
        public static readonly LocString Unknown = new("reconcile.unknown", "unknown");
        public static readonly LocString WentThrough = new("reconcile.wentThrough", "It went through");
        public static readonly LocString DidNotGoThrough = new("reconcile.didNotGoThrough", "It did not go through");
        public static readonly LocString WriteFailed = new("reconcile.writeFailed", "The answer could not be written to the purchase journal. The log has the details.");
    }

    internal static class Resume
    {
        public static readonly LocString Title = new("resume.title", "A run was interrupted");
        public static readonly LocString Body = new("resume.body", "Started {0}: {1} of {2} units bought, {3} spent. Resume plans what is left again with fresh prices.");
        public static readonly LocString ResumeRun = new("resume.resume", "Resume real buying");
        public static readonly LocString Discard = new("resume.discard", "Discard");
        public static readonly LocString OtherCharacter = new("resume.otherCharacter", "This run belongs to another character. Log in with that character to resume it, or discard it.");
    }

    internal static class Journal
    {
        public static readonly LocString Title = new("journal.title", "The purchase journal needs attention");
        public static readonly LocString Body = new("journal.body", "Some entries of the purchase journal cannot be trusted, so buying stays off. Setting it aside moves the file next to a fresh journal, where you can still read it.");
        public static readonly LocString ReadFailed = new("journal.readFailed", "The purchase journal could not be read, so buying stays off. The log has the details.");
        public static readonly LocString SetAside = new("journal.setAside", "Set it aside");
        public static readonly LocString SetAsideFailed = new("journal.setAsideFailed", "The journal could not be moved. The log has the details.");
    }
}
