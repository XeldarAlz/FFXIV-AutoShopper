using AutoShopper.Core.Travel;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoShopper.Core.Tasks;

// One journey on its own: for the goto command, and for a login that picks up a plan the data center transfer interrupted.
internal sealed class AutoJourney(JourneyPlan plan, bool resumed) : AutoCommon
{
    public static void Start(JourneyPlan plan) => clib.Services.Svc.Automation.Start(new AutoJourney(plan, resumed: false));

    public static void Resume(JourneyPlan plan) => clib.Services.Svc.Automation.Start(new AutoJourney(plan, resumed: true));

    protected override async Task Execute()
    {
        try
        {
            var outcome = await TravelToWorld(plan, resumed);
            if (CancelToken.IsCancellationRequested)
            {
                Diag("Journey: cancelled.");
                return;
            }

            if (outcome == JourneyOutcome.Arrived)
            {
                Svc.Chat.Print(plan.ReturnTrip
                    ? $"{AshopConstants.LogPrefix} Back home on {plan.World}."
                    : $"{AshopConstants.LogPrefix} Arrived on {plan.World}.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A journey that faults while logged in is over; one that faults mid transfer is still Lifestream's to
            // finish, and the plan stays for the login to pick up.
            if (Svc.ClientState.IsLoggedIn)
            {
                Plugin.Instance.Configuration.ClearPendingJourney();
            }

            throw;
        }
    }
}
