using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Travel;

namespace AutoShopper;

public sealed partial class Configuration
{
    public bool ReturnHomeAfterRun { get; set; } = true;

    // Written before a data center transfer and cleared when the journey ends, so a login can pick the journey up when
    // the task that started it did not live through the relog.
    public JourneyPlan? PendingJourney { get; set; }

    // Written with the pending journey when a run asks for the transfer, and cleared once the run has read it after the
    // login; one left over at load belongs to a run the plugin did not live through, which only Resume brings back.
    public TransferContinuation? PendingContinuation { get; set; }

    public void SetPendingJourney(JourneyPlan plan)
    {
        PendingJourney = plan;
        SaveDebounced();
    }

    // Saved at once rather than debounced, because the transfer logs the character out right after.
    public void SetPendingJourney(JourneyPlan plan, TransferContinuation continuation)
    {
        PendingJourney = plan;
        PendingContinuation = continuation;
        Save();
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

    public void ClearPendingContinuation()
    {
        if (PendingContinuation is null)
        {
            return;
        }

        PendingContinuation = null;
        SaveDebounced();
    }
}
