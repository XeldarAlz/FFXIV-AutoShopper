using AutoShopper.Core.Travel;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoShopper.Core.Tasks;

// One journey on its own: for the goto command, and for a login that picks up a plan the data center transfer interrupted.
internal sealed class AutoJourney(JourneyPlan plan, bool resumed) : AutoCommon
{
    public static void Start(JourneyPlan plan) => TaskStarter.TryStart(() => new AutoJourney(plan, resumed: false));

    // A journey that cannot be picked up is over, so its plan does not wait for the next login.
    public static void Resume(JourneyPlan plan)
    {
        if (!TaskStarter.TryStart(() => new AutoJourney(plan, resumed: true)))
        {
            Plugin.Instance.Configuration.ClearPendingJourney();
        }
    }

    // The player's stop ends the journey for good: a later login must not pick its plan up again, and Lifestream's
    // world change is stopped only when this journey started it. True when Lifestream was told to stop.
    public static bool EndStopped(AutoJourney journey)
    {
        Plugin.Instance.Configuration.ClearPendingJourney();
        return TravelOwnership.AbortIfOwnedBy(journey, "the journey was stopped");
    }

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
