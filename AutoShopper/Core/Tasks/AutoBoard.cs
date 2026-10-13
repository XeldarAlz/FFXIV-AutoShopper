using AutoShopper.Core.External;
using AutoShopper.Core.Market;
using AutoShopper.Core.Market.Model;
using AutoShopper.Core.Travel;
using ECommons.DalamudServices;
using System.Threading.Tasks;
using Automation = clib.Services.Automation;

namespace AutoShopper.Core.Tasks;

// /ashop board [city] goes to a market board, opens it and stops there; /ashop board stop cancels the trip. It never
// searches or buys.
internal sealed class AutoBoard(MarketCity city) : AutoCommon
{
    private const string StopArgument = "stop";
    private const string Scope = "Board";
    private const string Usage = AshopConstants.LogPrefix + " Usage: /ashop board [limsa|gridania|uldah|ishgard|kugane|crystarium|sharlayan|tuliyollal], or /ashop board stop.";

    public static void HandleCommand(string arguments, bool runActive)
    {
        var automation = clib.Services.Svc.Automation;
        if (arguments.Equals(StopArgument, StringComparison.OrdinalIgnoreCase))
        {
            Stop(automation);
            return;
        }

        if (automation.CurrentTask is AutoBoard)
        {
            MarketDebugCommands.PrintError("Board: a trip to a market board is already running. /ashop board stop cancels it.");
            return;
        }

        if (runActive || automation.Running)
        {
            MarketDebugCommands.PrintError("Board: a task is running; stop it before using board.");
            return;
        }

        if (!ExternalPlugins.IsInstalled(ExternalPlugin.Vnavmesh))
        {
            MarketDebugCommands.PrintError("Board: walking to a board needs the pathfinding plugin listed on the Plugins page.");
            return;
        }

        if (!TryPickCity(arguments, out var city))
        {
            return;
        }

        MarketDebugCommands.Print($"Board: heading for a market board in {MarketBoardCatalog.Of(city).Name}.");
        automation.Start(new AutoBoard(city));
    }

    protected override async Task Execute()
    {
        var arrival = await GoToMarketBoard(city, Scope);
        if (CancelToken.IsCancellationRequested || arrival.Failure == BoardArrivalFailure.Cancelled)
        {
            Diag("Board: cancelled.");
            return;
        }

        if (!arrival.Opened)
        {
            MarketDebugCommands.PrintError($"Board: no market board opened ({arrival.Failure}). The log has the details.");
            return;
        }

        Status = "Market board open";
        MarketDebugCommands.Print(arrival.Board.GameObjectId == 0
            ? "Board: the market board is open."
            : $"Board: the market board is open, {arrival.Board.Distance:F1}m away.");
    }

    private static void Stop(Automation automation)
    {
        if (automation.CurrentTask is not AutoBoard)
        {
            MarketDebugCommands.Print("Board: no trip to a market board is running.");
            return;
        }

        // The trip aborts a Lifestream ride of its own as it unwinds, and leaves anyone else's alone.
        automation.Stop();
        MarketDebugCommands.Print("Board: stopped.");
    }

    private static bool TryPickCity(string arguments, out MarketCity city)
    {
        if (arguments.Length > 0)
        {
            if (MarketCityNames.TryParse(arguments, out city))
            {
                return true;
            }

            Svc.Chat.PrintError(Usage);
            return false;
        }

        if (MarketCityChoice.TryChoose(Svc.ClientState.TerritoryType, MarketCityChoice.DefaultPreferred, ZoneAetherytes.IsAttuned, out city))
        {
            return true;
        }

        MarketDebugCommands.PrintError("Board: no market city's aetheryte is attuned on this character.");
        return false;
    }
}
