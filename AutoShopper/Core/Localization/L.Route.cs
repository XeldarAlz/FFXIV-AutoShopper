namespace AutoShopper.Core.Localization;

internal static partial class L
{
    internal static class Route
    {
        public static readonly LocString Title = new("route.title", "Route");
        public static readonly LocString StatusDone = new("route.status.done", "Done");
        public static readonly LocString StatusCurrent = new("route.status.current", "Now");
        public static readonly LocString StatusNext = new("route.status.next", "Next");
        public static readonly LocString StatusLater = new("route.status.later", "Later");
        public static readonly LocString StatusSkipped = new("route.status.skipped", "Skipped");
        public static readonly LocString LabelValue = new("route.labelValue", "{0}: {1}");
        public static readonly LocString SkipNotNeeded = new("route.skip.notNeeded", "not needed any more");
        public static readonly LocString SkipStopLimit = new("route.skip.stopLimit", "the most stops setting was reached");
        public static readonly LocString SkipHalted = new("route.skip.halted", "travel stopped for this run");
        public static readonly LocString FailRefused = new("route.fail.refused", "Lifestream cannot travel there");
        public static readonly LocString FailBusy = new("route.fail.busy", "Lifestream was busy with other work");
        public static readonly LocString FailDataCenterOff = new("route.fail.dataCenterOff", "data center travel is off in Lifestream");
        public static readonly LocString FailCongested = new("route.fail.congested", "the world is congested");
        public static readonly LocString FailTimedOut = new("route.fail.timedOut", "the trip took too long");
        public static readonly LocString FailAbandoned = new("route.fail.abandoned", "Lifestream gave the trip up");
        public static readonly LocString FailUnconfirmed = new("route.fail.unconfirmed", "the arrival could not be confirmed");
        public static readonly LocString FailBlocked = new("route.fail.blocked", "a party, a duty or an open window was in the way");

        public static readonly LocString PhaseChangingWorld = new("route.phase.changingWorld", "Changing world to {0}");
        public static readonly LocString PhaseTransfer = new("route.phase.transfer", "Data center transfer to {0}");
        public static readonly LocString PhaseLoggedOut = new("route.phase.loggedOut", "Logged out for the transfer to {0}");
        public static readonly LocString PhaseSettling = new("route.phase.settling", "Arriving on {0}");
        public static readonly LocString PhaseBoard = new("route.phase.board", "Going to the market board on {0}");
        public static readonly LocString PhaseRetry = new("route.phase.retry", "Trying the trip to {0} again shortly");
        public static readonly LocString PhaseHome = new("route.phase.home", "Travelling home to {0}");
        public static readonly LocString PhaseElapsed = new("route.phase.elapsed", "{0}  ·  {1}");

        public static readonly LocString ReturnHome = new("route.returnHome", "Return home");
        public static readonly LocString HomeOff = new("route.home.off", "Off");
        public static readonly LocString HomePending = new("route.home.pending", "After the last stop");
        public static readonly LocString HomeAlready = new("route.home.already", "Already home");
        public static readonly LocString HomeUnderway = new("route.home.underway", "On the way");
        public static readonly LocString HomeArrived = new("route.home.arrived", "Back home");
        public static readonly LocString HomeFailed = new("route.home.failed", "Did not make it home");
        public static readonly LocString HomeSkipped = new("route.home.skipped", "Kept off by the Japanese data center setting");

        public static readonly LocString ChatTransferContinue = new("route.chat.transferContinue", "Back from the data center transfer on {0}; the run carries on.");
        public static readonly LocString ChatPauseAfterTransfer = new("route.chat.pauseAfterTransfer", "The data center transfer to {0} is under way, so the run pauses once it is over.");
        public static readonly LocString ChatLeftForResume = new("route.chat.leftForResume", "The run is waiting for you: {0}. Resume it from the Shop page.");
        public static readonly LocString ChatHeldPaused = new("route.chat.heldPaused", "The trip to {0} is held up by a party, a duty, a trade or an open window. The run is paused; resume it once that is cleared.");
        public static readonly LocString ChatHomeArrived = new("route.chat.homeArrived", "Back home on {0}.");
        public static readonly LocString ChatHomeFailed = new("route.chat.homeFailed", "The way home did not finish; the character stays on {0}.");
        public static readonly LocString ChatHomeSkippedJapan = new("route.chat.homeSkippedJapan", "The way home needs travel between Japanese data centers, which is off in Settings; staying on {0}.");
        public static readonly LocString ChatHomeOtherRegion = new("route.chat.homeOtherRegion", "The home world is in another region, and travel never leaves the region; staying on {0}.");
        public static readonly LocString ReasonLoggedOut = new("route.reason.loggedOut", "the character logged out");
        public static readonly LocString ReasonOtherCharacter = new("route.reason.otherCharacter", "another character logged in");
        public static readonly LocString ReasonNotOurs = new("route.reason.notOurs", "the login did not complete the planned transfer");
        public static readonly LocString ReasonNotArrived = new("route.reason.notArrived", "the transfer did not reach {0}");
        public static readonly LocString ReasonUnresolved = new("route.reason.unresolved", "a purchase still waits for its answer");
        public static readonly LocString ReasonExpired = new("route.reason.expired", "the transfer took too long");

        public static readonly LocString WarnDataCenterHop = new("route.warn.dataCenterHop", "This plan crosses data centers. Turn on data center travel in Lifestream's settings (/li) before you start: each transfer logs the character out and back in, and the run carries on by itself afterwards.");
        public static readonly LocPlural WarnJapanLeftOut = new("route.warn.japanLeftOut", "{0} Japanese world outside your data center is left out. Settings, Shopping can allow Japanese data centers.", "{0} Japanese worlds outside your data center are left out. Settings, Shopping can allow Japanese data centers.");
        public static readonly LocString ShopDataCenterHop = new("route.shop.dataCenterHop", "Crosses data centers: data center travel must be on in Lifestream.");

        public static readonly LocString AllowJapan = new("route.setting.allowJapan", "Allow Japanese data centers (untested since the 2026-10-13 travel change)");
        public static readonly LocString AllowJapanHelp = new("route.setting.allowJapanHelp", "Off keeps Japanese worlds outside the data center you stand on out of prices, plans and runs. On lets a run travel between Japanese data centers, a route nobody has tested since World Visit between them changed.");
        public static readonly LocString Revisits = new("route.setting.revisits", "Go back to shopped worlds");
        public static readonly LocString RevisitsHelp = new("route.setting.revisitsHelp", "How many times one run may return to a world it already shopped, when the plan for what is left finds stock there. 0 never goes back.");
        public static readonly LocString JapanLeftOutChip = new("route.picker.japanLeftOut", "Left out while Japanese data centers are off");
    }
}
