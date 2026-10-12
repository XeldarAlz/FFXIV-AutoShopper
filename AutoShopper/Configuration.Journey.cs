using AutoShopper.Core.Travel;

namespace AutoShopper;

public sealed partial class Configuration
{
    public bool ReturnHomeAfterRun { get; set; } = true;

    // Written before a data center transfer and cleared when the journey ends, so a login can pick the journey up when
    // the task that started it did not live through the relog.
    public JourneyPlan? PendingJourney { get; set; }

    public void SetPendingJourney(JourneyPlan plan)
    {
        PendingJourney = plan;
        SaveDebounced();
    }

    public void ClearPendingJourney()
    {
        if (PendingJourney is null)
        {
            return;
        }

        PendingJourney = null;
        SaveDebounced();
    }
}
