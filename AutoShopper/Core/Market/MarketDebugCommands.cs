using System.Globalization;
using System.Threading.Tasks;
using AutoShopper.Core.Items;
using AutoShopper.Core.Market.Model;
using AutoShopper.Core.Shared;
using AutoShopper.Core.Shopping.Import;
using AutoShopper.Core.Tasks;
using AutoShopper.Core.Travel;
using ECommons.DalamudServices;

namespace AutoShopper.Core.Market;

// /ashop look <item name or id> reads the listings of one item at the board in reach and writes them to the log;
// /ashop mbstatus reports whether the market addresses and hooks are in place. Neither ever buys.
internal static class MarketDebugCommands
{
    public const string LookSubcommand = "look";
    public const string StatusSubcommand = "mbstatus";

    private const string LookUsage = AshopConstants.LogPrefix + " Usage: /ashop look <item name or id>, beside a market board.";

    public static void PrintStatus(MarketServices market)
    {
        var compatibility = market.Compatibility;
        Print($"Market: buying {(compatibility.ExecutionAvailable ? "available" : "off")}, reading listings {(compatibility.ReadingAvailable ? "available" : "off")}; {compatibility.Reason}.");
        for (var index = 0; index < compatibility.Checks.Length; index++)
        {
            var check = compatibility.Checks[index];
            Print($"  {check.Name}: {(check.Passed ? "ok" : "FAILED")}");
        }

        var search = market.Tracker.State;
        Print($"  Search: {search.Status} for item {search.ItemId}, generation {search.Generation}, failure {search.Failure}.");
        Print($"  Purchase in flight: {(market.Watcher.HasAttempt ? market.Watcher.AttemptId.ToString() : "none")}.");
    }

    public static void Look(string arguments, MarketServices market, ItemCatalog items, bool runActive)
    {
        if (!TryResolveItem(arguments, items, out var itemId))
        {
            return;
        }

        var automation = clib.Services.Svc.Automation;
        if (runActive || automation.Running)
        {
            PrintError("Market: a task is running; stop it before looking up listings.");
            return;
        }

        if (!market.Compatibility.ReadingAvailable)
        {
            PrintError($"Market: listings cannot be read; {market.Compatibility.Reason}.");
            return;
        }

        if (items.IsReady && !items.IsMarketable(itemId))
        {
            PrintError($"Market: item {itemId} is not sold on the market board.");
            return;
        }

        if (!MarketBoardDriver.IsSearchWindowReady() && !BoardInReach())
        {
            PrintError("Market: no market board in reach; stand beside one or open it first.");
            return;
        }

        Print($"Market: looking up item {itemId}; the log lists every listing.");
        automation.Start(new MarketLookTask(itemId, market, items));
    }

    // An id is taken as it is; a name needs the item data, which loads on first use.
    private static bool TryResolveItem(string arguments, ItemCatalog items, out uint itemId)
    {
        if (arguments.Length == 0)
        {
            itemId = 0;
            Svc.Chat.PrintError(LookUsage);
            return false;
        }

        if (uint.TryParse(arguments, NumberStyles.None, CultureInfo.InvariantCulture, out itemId))
        {
            if (itemId != 0)
            {
                return true;
            }

            Svc.Chat.PrintError(LookUsage);
            return false;
        }

        if (!items.IsReady)
        {
            items.EnsureLoading();
            PrintError("Market: the item data is still loading; try again in a moment, or give the item id.");
            return false;
        }

        var resolution = items.Resolve(arguments);
        switch (resolution.Status)
        {
            case ResolutionStatus.Resolved:
                itemId = resolution.ItemId;
                return true;
            case ResolutionStatus.Ambiguous:
                PrintError($"Market: \"{arguments}\" names {resolution.Candidates.Length} items; give the full name or the item id.");
                return false;
            default:
                PrintError($"Market: no item sold on the market board is named \"{arguments}\".");
                return false;
        }
    }

    private static bool BoardInReach()
        => MarketBoards.TryFindNearestBoard(out var board) && MarketBoardDriver.IsWithinReach(board);

    internal static void Print(string message)
    {
        Svc.Chat.Print($"{AshopConstants.LogPrefix} {message}");
        RunLog.Info(message);
    }

    internal static void PrintError(string message)
    {
        Svc.Chat.PrintError($"{AshopConstants.LogPrefix} {message}");
        RunLog.Info(message);
    }
}

internal sealed class MarketLookTask(uint itemId, MarketServices market, ItemCatalog items) : AutoCommon
{
    // Four attempts with their pacing, backoff and timeouts fit well inside this.
    private const int SearchBudgetMs = 90_000;
    private const int PollFrames = 1;

    protected override async Task Execute()
    {
        var name = items.TryGetName(itemId, out var found) ? found : $"item {itemId}";
        Status = "Opening the market board";
        if (!await OpenBoard())
        {
            return;
        }

        Status = $"Reading the listings of {name}";
        var state = market.Tracker.Request(itemId);
        var deadline = Environment.TickCount64 + SearchBudgetMs;
        while (state.Status == ListingRequestStatus.Waiting)
        {
            if (CancelToken.IsCancellationRequested || Environment.TickCount64 >= deadline)
            {
                market.Tracker.Cancel();
                MarketDebugCommands.PrintError($"Market: the search for {name} was abandoned.");
                return;
            }

            await NextFrame(PollFrames);
            state = market.Tracker.Poll();
        }

        if (state.Snapshot is not { } snapshot)
        {
            MarketDebugCommands.PrintError($"Market: the search for {name} failed ({state.Failure}).");
            return;
        }

        Write(snapshot, name);
        MarketDebugCommands.Print($"Market: {snapshot.Listings.Length} listing(s) of {name}; the log lists each one.");
    }

    private async Task<bool> OpenBoard()
    {
        var state = await OpenMarketBoard(market.Driver);
        if (state.Status == BoardOpenStatus.Open)
        {
            return true;
        }

        if (!CancelToken.IsCancellationRequested)
        {
            MarketDebugCommands.PrintError($"Market: the board did not open ({state.Failure}).");
        }

        return false;
    }

    private static void Write(ListingSnapshot snapshot, string name)
    {
        var world = Worlds.TryFindById(snapshot.WorldId, out var info) ? info.Name : snapshot.WorldId.ToString(CultureInfo.InvariantCulture);
        RunLog.Info($"Look: {name} ({snapshot.ItemId}) on {world}, generation {snapshot.Generation}, {snapshot.Listings.Length} listing(s), announced {snapshot.ExpectedCount}, complete {snapshot.IsComplete}.");
        for (var index = 0; index < snapshot.Listings.Length; index++)
        {
            var listing = snapshot.Listings[index];
            var payable = Gil.TryStackPayable(listing.UnitPrice, listing.Quantity, listing.TotalTax, out var total) ? total.ToString(CultureInfo.InvariantCulture) : "overflow";
            var marks = snapshot.Marks[index] == ListingMarks.None ? string.Empty : $" [refused: {snapshot.Marks[index]}]";
            RunLog.Info($"Look #{index + 1}: listing {listing.ListingId}, retainer {listing.RetainerId}, {(listing.IsHq ? "HQ" : "NQ")} {listing.Quantity} x {listing.UnitPrice} + tax {listing.TotalTax} = {payable} gil{marks}");
        }
    }
}
