using AutoShopper.Core.External;
using AutoShopper.Core.Ipc;
using AutoShopper.Core.Travel;
using ECommons.DalamudServices;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using Automation = clib.Services.Automation;

namespace AutoShopper.Core.Tasks;

internal sealed class AutoGoto(uint territoryId, Vector3 destination) : AutoCommon
{
    private const float ArriveWithinMeters = 3f;
    private const string StopArgument = "stop";
    private const string HomeArgument = "home";
    private const char InstancePrefix = 'i';
    private const int InstanceTokenLength = 2;
    private const int CoordinateFormTokens = 4;
    private const string Usage = AshopConstants.LogPrefix + " Usage: /ashop goto <territoryId> <x> <y> <z>, /ashop goto <world> [<aetheryte>] [i<n>], /ashop goto home, or /ashop goto stop.";

    private static readonly char[] CoordinateSeparators = [' ', ','];
    private static readonly char[] WordSeparators = [' '];

    public static void HandleCommand(string arguments, bool runActive)
    {
        var automation = clib.Services.Svc.Automation;
        if (arguments.Equals(StopArgument, StringComparison.OrdinalIgnoreCase))
        {
            Stop(automation);
            return;
        }

        if (arguments.Length == 0)
        {
            Svc.Chat.PrintError(Usage);
            return;
        }

        if (automation.CurrentTask is AutoGoto or AutoJourney)
        {
            Svc.Chat.PrintError($"{AshopConstants.LogPrefix} A goto is already running. /ashop goto stop cancels it.");
            return;
        }

        if (runActive)
        {
            Svc.Chat.PrintError($"{AshopConstants.LogPrefix} Stop the run before using goto.");
            return;
        }

        if (!ExternalPlugins.IsInstalled(ExternalPlugin.Vnavmesh))
        {
            Svc.Chat.PrintError($"{AshopConstants.LogPrefix} Goto needs the pathfinding plugin listed on the Plugins page.");
            return;
        }

        if (arguments.Equals(HomeArgument, StringComparison.OrdinalIgnoreCase))
        {
            StartHomeTrip();
            return;
        }

        if (char.IsAsciiDigit(arguments[0]))
        {
            StartPointTrip(arguments, automation);
            return;
        }

        StartWorldTrip(arguments);
    }

    protected override async Task Execute()
    {
        var zoneName = TerritoryNames.Of(territoryId);
        Diag($"Goto: travelling to {zoneName} ({territoryId}) at {destination}.");
        var arrived = await TravelTo(territoryId, destination, ArriveWithinMeters);
        if (CancelToken.IsCancellationRequested)
        {
            Diag("Goto: cancelled.");
            return;
        }

        if (!arrived)
        {
            Svc.Chat.PrintError($"{AshopConstants.LogPrefix} Goto: could not reach the spot in {zoneName}. The log has the details.");
            return;
        }

        Status = "Arrived";
        Svc.Chat.Print($"{AshopConstants.LogPrefix} Goto: arrived in {zoneName}.");
    }

    private static void Stop(Automation automation)
    {
        if (automation.CurrentTask is not (AutoGoto or AutoJourney))
        {
            Svc.Chat.Print($"{AshopConstants.LogPrefix} No goto is running.");
            return;
        }

        var journey = automation.CurrentTask is AutoJourney;
        automation.Stop();
        if (!journey)
        {
            Svc.Chat.Print($"{AshopConstants.LogPrefix} Goto stopped.");
            return;
        }

        // The player's stop ends the journey for good, so a later login must not pick it up again.
        Plugin.Instance.Configuration.ClearPendingJourney();
        var lifestream = LifestreamIPC.Instance;
        if (!lifestream.IsBusy())
        {
            Svc.Chat.Print($"{AshopConstants.LogPrefix} Goto stopped.");
            return;
        }

        lifestream.Abort();
        Svc.Chat.Print($"{AshopConstants.LogPrefix} Goto stopped; Lifestream's travel was aborted with it.");
    }

    private static void StartPointTrip(string arguments, Automation automation)
    {
        if (!TryParsePoint(arguments, out var territoryId, out var destination))
        {
            Svc.Chat.PrintError(Usage);
            return;
        }

        Svc.Chat.Print($"{AshopConstants.LogPrefix} Goto: heading to {TerritoryNames.Of(territoryId)} ({territoryId}) at {destination.X:F1}, {destination.Y:F1}, {destination.Z:F1}.");
        automation.Start(new AutoGoto(territoryId, destination));
    }

    private static void StartHomeTrip()
    {
        if (!Worlds.TryHome(out var home))
        {
            Svc.Chat.PrintError($"{AshopConstants.LogPrefix} The home world could not be read; is a character logged in?");
            return;
        }

        if (Worlds.TryCurrent(out var current) && current.Id == home.Id)
        {
            Svc.Chat.Print($"{AshopConstants.LogPrefix} Goto: already on the home world, {home.Name}.");
            return;
        }

        Svc.Chat.Print($"{AshopConstants.LogPrefix} Goto: heading home to {home.Name} ({home.DataCenterName}).");
        AutoJourney.Start(JourneyPlan.Home(home.Name));
    }

    // The first word is the world, a trailing i<n> is the instance, and whatever sits between is the aetheryte's name.
    private static void StartWorldTrip(string arguments)
    {
        var words = arguments.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (!Worlds.TryFind(words[0], out var world))
        {
            Svc.Chat.PrintError($"{AshopConstants.LogPrefix} No world matches '{words[0]}'.");
            return;
        }

        var instance = 0;
        var nameEnd = words.Length;
        if (words.Length > 1 && TryParseInstance(words[^1], out instance))
        {
            nameEnd--;
        }

        var aetheryteId = 0u;
        var territoryId = 0u;
        var stop = string.Empty;
        if (nameEnd > 1)
        {
            var name = string.Join(' ', words, 1, nameEnd - 1);
            if (!ZoneAetherytes.TryFindByName(name, out territoryId, out var aetheryte))
            {
                Svc.Chat.PrintError($"{AshopConstants.LogPrefix} No single aetheryte matches '{name}'.");
                return;
            }

            aetheryteId = aetheryte.Id;
            stop = $", {aetheryte.Name} in {TerritoryNames.Of(territoryId)}";
        }

        var instanceText = instance > 0 ? $", instance {instance}" : string.Empty;
        Svc.Chat.Print($"{AshopConstants.LogPrefix} Goto: heading to {world.Name} ({world.DataCenterName}){stop}{instanceText}.");
        AutoJourney.Start(JourneyPlan.ToWorld(world.Name, aetheryteId, territoryId, instance));
    }

    internal static bool TryParseInstance(string token, out int instance)
    {
        instance = 0;
        if (token.Length != InstanceTokenLength || char.ToLowerInvariant(token[0]) != InstancePrefix || !char.IsAsciiDigit(token[1]))
        {
            return false;
        }

        instance = token[1] - '0';
        return instance > 0;
    }

    private static bool TryParsePoint(string arguments, out uint territoryId, out Vector3 destination)
    {
        territoryId = 0;
        destination = default;
        var parts = arguments.Split(CoordinateSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != CoordinateFormTokens)
        {
            return false;
        }

        if (!uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out territoryId))
        {
            return false;
        }

        if (!TryParseCoordinate(parts[1], out var x) || !TryParseCoordinate(parts[2], out var y) || !TryParseCoordinate(parts[3], out var z))
        {
            return false;
        }

        destination = new Vector3(x, y, z);
        return true;
    }

    private static bool TryParseCoordinate(string text, out float value)
        => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && float.IsFinite(value);
}
