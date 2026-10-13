using AutoShopper.Core.Localization;
using AutoShopper.Core.Stats;
using AutoShopper.Core.Travel;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoShopper.Core.Tasks;

// The way home after a run, the first link of the after-run chain. Its travel belongs to the run, so a Stop aborts it
// like the run's own; a transfer home lives through its relog like the run's transfers do.
internal sealed class AutoWayHome(ShopRunState? run) : AutoCommon
{
    public ReturnHomeResult Result { get; private set; } = ReturnHomeResult.Underway;

    protected override object TravelOwner => (object?)run ?? this;

    protected override async Task Execute()
    {
        Status = "Travelling home";
        var home = Worlds.TryHome(out var world) ? world.Name : string.Empty;
        run?.SetTravel(TravelPhase.ReturningHome, home);
        var outcome = await TravelHome();
        run?.ClearTravel();
        if (CancelToken.IsCancellationRequested)
        {
            Diag("Way home: cancelled.");
            return;
        }

        if (outcome != JourneyOutcome.Arrived)
        {
            Result = ReturnHomeResult.Failed;
            Warn($"Way home: ended with {outcome}; the character stays where it is");
            var here = Worlds.TryCurrent(out var current) ? current.Name : string.Empty;
            Svc.Chat.PrintError($"{AshopConstants.LogPrefix} {Loc.T(L.Route.ChatHomeFailed, here)}");
            return;
        }

        Result = ReturnHomeResult.Arrived;
        Svc.Chat.Print($"{AshopConstants.LogPrefix} {Loc.T(L.Route.ChatHomeArrived, home)}");
    }
}
