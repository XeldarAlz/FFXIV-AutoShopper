using AutoShopper.Core.Stats;
using AutoShopper.Core.Travel;

namespace AutoShopper.Core.Tasks;

internal sealed partial class AutoShopController
{
    private const int MaxFaultResumes = 3;
    private const int MaxFaultResumesPerRun = 6;
    private const long FaultResumeWindowMs = 5 * TimeUnits.MillisecondsPerMinute;

    private int faultResumeCount;
    private int runFaultResumeCount;
    private long faultWindowStartedAtMs;

    private void OnRunEnded(ShopSession owningSession)
    {
        var faulted = ReferenceEquals(session, owningSession) && owningSession.EndedWithFault;
        if (faulted && !owningSession.CompletedByStopCondition && TryAutoResumeAfterFault(owningSession))
        {
            return;
        }

        if (faulted)
        {
            ECommons.DalamudServices.Svc.Chat.PrintError($"{AshopConstants.LogPrefix} The run stopped on an unexpected error. The log has the details.");
        }

        EndRun(owningSession);
    }

    private void EndRun(ShopSession owningSession)
    {
        FinalizeRun(owningSession);
        if (!ReferenceEquals(session, owningSession))
        {
            Diag("A run that is no longer live ended; it was recorded and the current run is left alone.");
            return;
        }

        if (!TryRunAfterAction(owningSession))
        {
            ClearRun();
        }
    }

    // One ordered chain once a run ends on its own: the way home, then the after-run action. A Stop or a fault runs
    // neither. The finished run stays on screen while the chain runs, and is cleared once it ends.
    private bool TryRunAfterAction(ShopSession ending)
    {
        if (ending.AfterActionDispatched)
        {
            return false;
        }

        if (!ending.CompletedByStopCondition || ending.EndedWithFault)
        {
            Diag($"Run ended without meeting its stop condition (fault {ending.EndedWithFault}); no way home and no after-run action.");
            return false;
        }

        ending.AfterActionDispatched = true;
        if (!ECommons.DalamudServices.Svc.ClientState.IsLoggedIn)
        {
            Diag("Run ended while logged out; no way home and no after-run action.");
            return false;
        }

        var action = AfterActionFor(ending);
        var wayHome = WayHomeDue();
        if (!wayHome && action is null)
        {
            return false;
        }

        progress.SetPhase(RunPhase.Finishing);
        RunChain(wayHome, action);
        return true;
    }

    private void RunChain(bool wayHome, AfterRunAction? action)
    {
        if (wayHome)
        {
            Diag(action is { } then ? $"Run completed; taking the way home, then after-run action {then}." : "Run completed; taking the way home.");
            RunTask(new AutoWayHome(), () =>
            {
                Diag("The way home finished.");
                RunChain(wayHome: false, action);
            });
            return;
        }

        if (action is { } next)
        {
            RunAfterAction(next);
            return;
        }

        ClearRun();
    }

    private static AfterRunAction? AfterActionFor(ShopSession ending)
    {
        var action = Plugin.Instance.Configuration.AfterRun;
        if (action == AfterRunAction.StayLoggedIn)
        {
            Diag("Run completed by its stop condition; the after-run action is StayLoggedIn, nothing to do.");
            return null;
        }

        if (!ending.DidWork)
        {
            Diag($"Run ended by its stop condition without doing any work; skipping after-run action {action}.");
            return null;
        }

        return action;
    }

    private void RunAfterAction(AfterRunAction action)
    {
        Diag($"Starting after-run action {action}.");
        AutoCommon task = action == AfterRunAction.ReturnToInn ? new AutoReturnToInn() : new AutoAfterRun(action);
        RunTask(task, () =>
        {
            Diag($"After-run action {action} finished.");
            ClearRun();
        });
    }

    private static bool WayHomeDue()
    {
        if (!Plugin.Instance.Configuration.ReturnHomeAfterRun)
        {
            return false;
        }

        if (!Worlds.TryHome(out var home) || !Worlds.TryCurrent(out var current))
        {
            Diag("The way home is on, but the home or current world could not be read; staying.");
            return false;
        }

        if (home.Id == current.Id)
        {
            Diag($"Already on the home world {home.Name}; no way home needed.");
            return false;
        }

        return true;
    }

    // Idempotent through Recorded, so an explicit Stop and a finished task can both call it.
    private void FinalizeRun(ShopSession? ending)
    {
        if (ending is null || ending.Recorded)
        {
            return;
        }

        ending.Recorded = true;
        ending.End();
        try
        {
            var outcome = ending.Outcome ?? (ending.EndedWithFault ? RunOutcome.Failed : RunOutcome.Stopped);
            // A run stopped before it did anything is a misclick more often than a run; any other end is worth a row.
            if (!ending.DidWork && outcome == RunOutcome.Stopped)
            {
                Diag("Run was stopped before doing any work; nothing recorded to history.");
                return;
            }

            var record = new RunRecord
            {
                StartedAtUtc = ending.StartedAt,
                EndedAtUtc = DateTime.UtcNow,
                DurationSeconds = ending.Elapsed.TotalSeconds,
                WorldName = ending.WorldName,
                DataCenterName = ending.DataCenterName,
                CrossedDataCenter = ending.CrossedDataCenter,
                Outcome = outcome,
            };
            Plugin.Instance.History.Append(record);
            Diag($"Run recorded to history ({outcome}): {record.Duration} on {record.WorldName} ({record.DataCenterName}){(record.CrossedDataCenter ? ", across data centers" : string.Empty)}.");
        }
        catch (Exception exception)
        {
            Diag($"FinalizeRun failed to record history: {exception.Message}");
        }
    }

    private void ResetFaultBudget()
    {
        faultResumeCount = 0;
        runFaultResumeCount = 0;
        faultWindowStartedAtMs = 0;
    }

    // The window restarts once it lapses, so sparse faults over a long run each get a fresh budget; only a burst, a wedge
    // that faults again straight away, spends it. The per-run cap still ends a run whose faults keep coming, however far
    // apart they are.
    private bool TryAutoResumeAfterFault(ShopSession owningSession)
    {
        if (!Plugin.Instance.Configuration.AutoResumeOnFault)
        {
            Diag("Run task faulted and auto-resume on fault is off; the run ends.");
            return false;
        }

        if (!CanRestart())
        {
            Diag("Run task faulted with nothing to resume; the run ends.");
            return false;
        }

        if (runFaultResumeCount >= MaxFaultResumesPerRun)
        {
            Diag($"Run task faulted after {runFaultResumeCount} restarts in this run; not resuming. The run ends.");
            return false;
        }

        var now = Environment.TickCount64;
        if (now - faultWindowStartedAtMs > FaultResumeWindowMs)
        {
            faultResumeCount = 0;
            faultWindowStartedAtMs = now;
        }

        if (faultResumeCount >= MaxFaultResumes)
        {
            Diag($"Run task faulted {faultResumeCount} times within {FaultResumeWindowMs / TimeUnits.MillisecondsPerMinute} minutes; not resuming. The run ends.");
            return false;
        }

        faultResumeCount++;
        runFaultResumeCount++;
        owningSession.ClearFault();
        Diag($"Run task ended on an unexpected fault; auto-resuming (resume {faultResumeCount}/{MaxFaultResumes} in this {FaultResumeWindowMs / TimeUnits.MillisecondsPerMinute} minute window, {runFaultResumeCount}/{MaxFaultResumesPerRun} in this run).");
        ECommons.DalamudServices.Svc.Chat.Print($"{AshopConstants.LogPrefix} The run stopped on an unexpected error; restarting it ({faultResumeCount}/{MaxFaultResumes}).");
        LaunchRun(owningSession);
        return true;
    }
}
