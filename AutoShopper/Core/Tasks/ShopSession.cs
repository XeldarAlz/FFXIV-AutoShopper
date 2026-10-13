using AutoShopper.Core.Stats;
using System.Threading;

namespace AutoShopper.Core.Tasks;

// One run of the controller: the clock, where it ran, how it ended, and what the run learned that has to outlive a
// pause, since every resume builds the run task afresh.
public sealed class ShopSession
{
    // Null until the run decides how it ended.
    internal RunOutcome? Outcome;

    internal bool Recorded;
    internal bool AfterActionDispatched;

    // The history row of a run that ended on its own, kept so its way home can report back into it.
    internal RunRecord? Record;

    private long pausedMs;
    private long pauseStartedAtMs;
    private DateTime? endedAt;

    public DateTime StartedAt { get; } = DateTime.UtcNow;

    // Filled in by the run once one exists.
    public string WorldName { get; internal set; } = string.Empty;

    public string DataCenterName { get; internal set; } = string.Empty;

    public bool CrossedDataCenter { get; internal set; }

    // Set once the run has done something worth a history row, so a run stopped before that leaves none.
    public bool DidWork { get; internal set; }

    public bool CompletedByStopCondition { get; internal set; }

    public string ListName { get; internal set; } = string.Empty;

    public string Caller { get; internal set; } = string.Empty;

    public Guid RunId { get; internal set; }

    public bool DryRun { get; internal set; }

    // For a dry run, what it would have spent and bought.
    public long Spent { get; internal set; }

    public int Units { get; internal set; }

    public int Purchases { get; internal set; }

    public ReturnHomeResult ReturnHome { get; internal set; }

    // A run cut off by a logout or an unload is not over: it leaves no history row, and its journal offers to resume.
    public bool Interrupted { get; internal set; }

    public bool EndedWithFault { get; private set; }

    public TimeSpan Elapsed => (endedAt ?? DateTime.UtcNow) - StartedAt - TimeSpan.FromMilliseconds(PausedTotalMs);

    private long PausedTotalMs
        => pausedMs + (pauseStartedAtMs == 0 ? 0 : Environment.TickCount64 - pauseStartedAtMs);

    // A Stop unwinds the run loop through the same catch as a genuine fault, and only a genuine fault may resume the run.
    public void RecordFault(Exception exception, CancellationToken cancelToken)
    {
        if (cancelToken.IsCancellationRequested || exception is OperationCanceledException)
        {
            return;
        }

        EndedWithFault = true;
        // clib's task runner writes the same exception to dalamud.log when the task unwinds.
        RunLog.Record(RunLogLevel.Error, exception, "The shop run task ended with an unexpected error");
    }

    internal void ClearFault() => EndedWithFault = false;

    public void BeginPause()
    {
        if (pauseStartedAtMs != 0)
        {
            return;
        }

        pauseStartedAtMs = Environment.TickCount64;
    }

    public void EndPause()
    {
        if (pauseStartedAtMs == 0)
        {
            return;
        }

        pausedMs += Environment.TickCount64 - pauseStartedAtMs;
        pauseStartedAtMs = 0;
    }

    // Freezes the clock, so a finished run left on screen during its after-run action stops counting.
    internal void End()
    {
        if (endedAt is not null)
        {
            return;
        }

        EndPause();
        endedAt = DateTime.UtcNow;
    }
}
