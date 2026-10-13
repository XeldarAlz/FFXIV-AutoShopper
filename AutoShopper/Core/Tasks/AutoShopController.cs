using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.External;
using AutoShopper.Core.Ipc;
using AutoShopper.Core.Ledger;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Stats;
using AutoShopper.Core.Travel;
using clib.Services;

namespace AutoShopper.Core.Tasks;

internal sealed partial class AutoShopController
{
    private readonly RunProgress progress = new();

    private ShopSession? session;
    private Func<ShopSession, AutoCommon>? runTaskFactory;
    private AutoCommon? currentTask;
    private ShopRunState? shopRun;
    private bool stopping;

    public bool Running => Svc.Automation.Running || Paused || stopping;

    public bool Stopping => stopping;

    public string Status => stopping ? Loc.T(L.Buyer.StatusSettling) : PauseReason switch
    {
        PauseReason.InContent => "Paused while you are in content",
        PauseReason.Manual    => "Paused",
        _                     => Svc.Automation.CurrentTask?.Status ?? "Idle",
    };

    public RunPhase Phase
    {
        get
        {
            if (stopping)
            {
                return RunPhase.Stopping;
            }

            if (shopRun is { } run && progress.Phase is not (RunPhase.Idle or RunPhase.Paused or RunPhase.Finishing))
            {
                return PhaseOf(run.State);
            }

            return progress.Phase;
        }
    }

    public ShopSession? SessionSnapshot => session;

    internal ShopRunState? ShopRun => shopRun;

    private static void Diag(string message)
        => RunLog.Info(message);

    // A dry run writes its journal to memory, so nothing it did can count as spending after a restart.
    public void Start()
    {
        if (Running)
        {
            Diag("Start ignored: a run is already running.");
            return;
        }

        var plugin = Plugin.Instance;
        var readiness = ShopReadiness.Evaluate(plugin);
        if (readiness != Readiness.Ready)
        {
            Diag($"Start refused: {readiness}.");
            ECommons.DalamudServices.Svc.Chat.PrintError($"{AshopConstants.LogPrefix} {Loc.T(L.Buyer.ChatRefused, ShopReadiness.Reason(readiness))}");
            return;
        }

        var configuration = plugin.Configuration;
        var listName = plugin.Lists.Selected?.Name ?? string.Empty;
        IRunJournal journal = configuration.DryRun ? new MemoryJournal() : plugin.Ledger.Journal;
        BeginShopRun(ShopRunState.Fresh(plugin.Market, journal, configuration.DryRun, listName), configuration.DryRun ? "dry run" : "run");
    }

    // A run the journal says was interrupted by a logout or an unload comes back only on this click.
    public void ResumeInterrupted()
    {
        if (Running)
        {
            Diag("Resume of the interrupted run ignored: a run is already running.");
            return;
        }

        var plugin = Plugin.Instance;
        if (plugin.Ledger.Review.Interrupted is not { Authorization: not null } interrupted || !RequiredPluginsReady())
        {
            return;
        }

        if (interrupted.Authorization.CharacterContentId != ECommons.DalamudServices.Svc.PlayerState.ContentId)
        {
            ECommons.DalamudServices.Svc.Chat.PrintError($"{AshopConstants.LogPrefix} {Loc.T(L.Resume.OtherCharacter)}");
            return;
        }

        // A run read back from the journal buys for real, so it never resumes while Dry run says nothing is bought.
        if (plugin.Configuration.DryRun || interrupted.Authorization.DryRun)
        {
            Diag($"Resume of run {interrupted.RunId} refused: Dry run is on (setting {plugin.Configuration.DryRun}, recorded {interrupted.Authorization.DryRun}).");
            ECommons.DalamudServices.Svc.Chat.PrintError($"{AshopConstants.LogPrefix} {Loc.T(L.Resume.DryRunOn)}");
            return;
        }

        BeginShopRun(ShopRunState.FromJournal(plugin.Market, plugin.Ledger.Journal, interrupted), "resume of an interrupted run");
    }

    public void DiscardInterrupted()
    {
        if (Running)
        {
            return;
        }

        var discarded = Plugin.Instance.Ledger.Discard(DateTime.UtcNow);
        Diag(discarded ? "The interrupted run was discarded." : "Discarding the interrupted run could not be written to the journal.");
    }

    // A logout, a character change or an unload stops the run where it stands and leaves its journal as it is, so
    // the Shop page offers to resume it later; it never resumes on its own. Lifestream's travel is left alone: a
    // transfer under way is picked up as a bare journey at the next login. A run that already ended only loses what
    // was left of its way home and after-run action. A dry run's journal lives in memory and cannot be resumed, so
    // it ends here as stopped and keeps its history row.
    public void Interrupt()
    {
        if (shopRun is not { } run)
        {
            return;
        }

        var ending = session;
        var finished = run.FinalState is not null;
        var wayHome = currentTask is AutoWayHome;
        var taskWasLive = Svc.Automation.Running;
        stopping = false;
        currentTask = null;
        PauseReason = PauseReason.None;
        Svc.Automation.Stop();
        ReleaseHelpers(taskWasLive);
        TravelOwnership.Release(run);
        run.EndTransfer();
        Plugin.Instance.Configuration.ClearPendingContinuation();
        if (run.IsAuthorized && !finished)
        {
            run.Checkpoint(DateTime.UtcNow);
        }

        if (ending is not null && wayHome)
        {
            SetReturnHome(ending, ReturnHomeResult.Failed);
        }

        var dryEnded = false;
        if (ending is not null && !finished)
        {
            dryEnded = run.DryRun;
            if (dryEnded)
            {
                EndShopRun(ending, RunState.Stopped, "the dry run was interrupted");
            }
            else
            {
                ending.Interrupted = true;
            }
        }

        FinalizeRun(ending);
        ClearRun();
        Plugin.Instance.Ledger.Invalidate();
        if (finished)
        {
            Diag($"Run {run.RunId} had ended; what was left of its way home and after-run action is dropped.");
            return;
        }

        if (dryEnded)
        {
            Diag($"Dry run {run.RunId} interrupted; it ends as stopped, since a dry run cannot be resumed.");
            return;
        }

        Diag($"Run {run.RunId} interrupted; its journal is kept for a resume.");
        if (run.IsAuthorized && !run.DryRun)
        {
            ECommons.DalamudServices.Svc.Chat.Print($"{AshopConstants.LogPrefix} {Loc.T(L.Buyer.ChatInterrupted)}");
        }
    }

    // The logout a data center transfer brings belongs to the trip the run, or its way home, asked for, so the task
    // lives through it and checks the login that follows. Any other logout interrupts the run.
    public void OnLogout()
    {
        if (!InPlannedTransfer())
        {
            Interrupt();
            return;
        }

        var journey = Plugin.Instance.Configuration.PendingJourney;
        shopRun?.NoteTransferLogout(journey is { } pending ? new TransferSnapshot(pending.World, pending.StartedAtUtc) : null);
        Diag("Logged out for the data center transfer the run asked for; the run waits for the login that ends it.");
    }

    // The run checks the login once the character stands on the planned world; another character back is interrupted
    // at once, when the game already tells who logged in, the way home of a finished run included.
    public void OnLogin()
    {
        if (shopRun is not { TransferLoggedOut: true } run)
        {
            return;
        }

        var contentId = ECommons.DalamudServices.Svc.PlayerState.ContentId;
        Diag(run.FinalState is null ? "Logged back in during the run's data center transfer." : "Logged back in during the data center transfer of the run's way home.");
        if (contentId != 0 && run.Authorization is { } authorization && contentId != authorization.CharacterContentId)
        {
            Diag("Another character logged in after the transfer; the run is interrupted.");
            Interrupt();
        }
    }

    private bool InPlannedTransfer()
    {
        if (Paused || stopping || currentTask is null || shopRun is not { } run)
        {
            return false;
        }

        var configuration = Plugin.Instance.Configuration;
        if (configuration.PendingJourney is not { } journey)
        {
            return false;
        }

        if (currentTask is AutoWayHome home)
        {
            return journey.ReturnTrip && journey.StartedAtUtc == home.JourneyStartedAtUtc;
        }

        return currentTask is AutoShopRun
            && run.TransferArmed
            && configuration.PendingContinuation is { } continuation
            && continuation.RunId == run.RunId
            && continuation.JourneyStartedAtUtc == journey.StartedAtUtc;
    }

    public void Shutdown()
    {
        if (shopRun is not null)
        {
            Interrupt();
            return;
        }

        Stop();
    }

    // A GM nearby ends the run as stopped, whatever it was about to become, and nothing runs after it: no way home and
    // no after-run action.
    public void StopForAlert()
    {
        if (session is { Recorded: false } live)
        {
            live.Outcome = RunOutcome.Stopped;
        }

        Stop();
    }

    // A purchase that already left cannot be called back, so a Stop during one waits for its answer and ends the run
    // once it is settled; nothing new starts meanwhile.
    public void Stop()
    {
        var taskWasLive = Svc.Automation.Running;
        if (shopRun is { InFlight: not null } waiting)
        {
            if (stopping)
            {
                return;
            }

            stopping = true;
            currentTask = null;
            deferredPause = PauseReason.None;
            PauseReason = PauseReason.None;
            Svc.Automation.Stop();
            ReleaseHelpers(taskWasLive);
            waiting.Settled = StopAfterSettlement;
            Diag("Stop requested while a purchase is in flight; the run ends once it is answered.");
            ECommons.DalamudServices.Svc.Chat.Print($"{AshopConstants.LogPrefix} {Loc.T(L.Buyer.ChatStopping)}");
            return;
        }

        stopping = false;
        var ending = session;
        var wayHome = currentTask is AutoWayHome;
        var journey = Svc.Automation.CurrentTask as AutoJourney;
        currentTask = null;
        deferredPause = PauseReason.None;
        PauseReason = PauseReason.None;
        Svc.Automation.Stop();
        if (journey is not null)
        {
            AutoJourney.EndStopped(journey);
        }

        if (ending is not null)
        {
            EndShopRun(ending, RunState.Stopped, "stopped by the player");
            ending.Outcome ??= RunOutcome.Stopped;
            if (wayHome)
            {
                SetReturnHome(ending, ReturnHomeResult.Failed);
            }

            ReleaseHelpers(taskWasLive);
            AbandonTravel("the run was stopped");
        }

        FinalizeRun(ending);
        ClearRun();
        if (ending is null)
        {
            return;
        }

        Diag("Stop requested; session cleared.");
        ECommons.DalamudServices.Svc.Chat.Print($"{AshopConstants.LogPrefix} Run stopped.");
    }

    private static bool RequiredPluginsReady()
    {
        if (ExternalPlugins.AllRequiredInstalled())
        {
            return true;
        }

        var missing = ExternalPlugins.MissingRequiredNames();
        Diag($"Start aborted: required plugins missing ({missing}).");
        ECommons.DalamudServices.Svc.Chat.PrintError($"{AshopConstants.LogPrefix} Cannot start: install all required plugins first ({missing}).");
        return false;
    }

    // Every Resume and fault restart builds the run task afresh from the same factory.
    private void BeginRun(ShopSession newSession, Func<ShopSession, AutoCommon> taskFactory, string plan)
    {
        PauseReason = PauseReason.None;
        deferredPause = PauseReason.None;
        ResetFaultBudget();
        session = newSession;
        runTaskFactory = taskFactory;
        Diag($"Run starting: {plan}.");
        LaunchRun(newSession);
    }

    // A run task that cannot be built is undone the way a logout undoes a run: an authorized real run stays in the
    // journal for the Resume card, anything else is cleared.
    private void LaunchRun(ShopSession owningSession)
    {
        progress.Reset();
        progress.SetPhase(RunPhase.Preparing);
        if (RunTask(() => runTaskFactory!(owningSession), () => OnRunEnded(owningSession)))
        {
            return;
        }

        if (shopRun is null)
        {
            PauseReason = PauseReason.None;
            ClearRun();
            return;
        }

        Interrupt();
    }

    private bool CanRestart() => runTaskFactory is not null;

    private void BeginShopRun(ShopRunState state, string plan)
    {
        stopping = false;
        shopRun = state;
        Plugin.Instance.Summaries.Track(state);
        var newSession = new ShopSession();
        state.ApplyTo(newSession);
        BeginRun(newSession, owner => new AutoShopRun(state, owner), plan);
    }

    private void StopAfterSettlement()
    {
        if (!stopping)
        {
            return;
        }

        stopping = false;
        Stop();
    }

    // Writes the end of a buyer run that did not end on its own, unless it was interrupted and stays resumable.
    private void EndShopRun(ShopSession ending, RunState requested, string reason)
    {
        if (shopRun is not { } run || ending.Interrupted)
        {
            return;
        }

        var final = run.HasUnknownOutcome ? RunState.NeedsReconciliation : requested;
        if (run.FinalState is null)
        {
            run.Finish(final, reason, DateTime.UtcNow);
        }

        run.ApplyTo(ending);
        var outcome = ShopRunState.OutcomeOf(run.FinalState ?? final);
        // A purchase without a clear answer outweighs whatever the run was about to be recorded as, a GM alert's stop included.
        if (outcome == RunOutcome.NeedsReconciliation && ending.Outcome != RunOutcome.NeedsReconciliation)
        {
            ending.Outcome = RunOutcome.NeedsReconciliation;
            ECommons.DalamudServices.Svc.Chat.PrintError($"{AshopConstants.LogPrefix} {Loc.T(L.Buyer.ChatUncertain)}");
        }
        else
        {
            ending.Outcome ??= outcome;
        }

        if (!run.DryRun)
        {
            Plugin.Instance.Ledger.Invalidate();
        }
    }

    private static RunPhase PhaseOf(RunState state) => state switch
    {
        RunState.Traveling or RunState.GoingToBoard => RunPhase.Travelling,
        RunState.Searching or RunState.Inspecting or RunState.Guarding or RunState.PersistingIntent
            or RunState.Submitting or RunState.AwaitingResult or RunState.Reconciling => RunPhase.Shopping,
        RunState.Paused => RunPhase.Paused,
        RunState.NeedsReconciliation or RunState.Completed or RunState.CompletedPartial or RunState.Stopped or RunState.Failed => RunPhase.Finishing,
        _ => RunPhase.Preparing,
    };

    // A stopped or paused trip must not come back at the next login, and a world change Lifestream still has in hand
    // for this run would otherwise carry the character off after it. Lifestream work the run did not start is left alone.
    private void AbandonTravel(string reason)
    {
        var configuration = Plugin.Instance.Configuration;
        configuration.ClearPendingJourney();
        configuration.ClearPendingContinuation();
        if (shopRun is not { } run)
        {
            return;
        }

        run.EndTransfer();
        run.ClearTravel();
        if (!TravelOwnership.AbortIfOwnedBy(run, reason) && LifestreamIPC.Instance.IsBusy())
        {
            Diag($"{reason}; Lifestream is busy with work this run did not start, so it is left alone.");
        }
    }

    // The movement library fires OnCompleted off the game thread: its await of the task does not return to the framework
    // scheduler, and the runtime moves the continuation to the thread pool. Recording a run reads the object table, which
    // Dalamud allows only on the game thread, so the hand-off is moved back there. Building a task hooks game functions
    // and throws when one is missing; false then, once the log and the chat say so, and the caller undoes its setup.
    private bool RunTask(Func<AutoCommon> build, Action onCompleted)
    {
        try
        {
            var task = build();
            currentTask = task;
            Svc.Automation.Start(task, OnCompleted: () => _ = ECommons.DalamudServices.Svc.Framework.RunOnFrameworkThread(() => HandOff(task, onCompleted)));
            return true;
        }
        catch (Exception exception)
        {
            currentTask = null;
            RunLog.Error(exception, "A task could not be started");
            ECommons.DalamudServices.Svc.Chat.PrintError($"{AshopConstants.LogPrefix} {Loc.T(L.Buyer.ChatTaskFailed)}");
            return false;
        }
    }

    private void HandOff(AutoCommon task, Action onCompleted)
    {
        if (!ReferenceEquals(currentTask, task))
        {
            Diag($"{task.GetType().Name} finished but is no longer the current task (stopped, paused, or superseded); skipping hand-off.");
            return;
        }

        currentTask = null;
        onCompleted();
    }

    private void ClearRun()
    {
        session = null;
        runTaskFactory = null;
        progress.Reset();
        stopping = false;
        deferredPause = PauseReason.None;
        shopRun?.Dispose();
        shopRun = null;
    }

    // A stopped task unwinds on a later frame, and after an unload that frame may never come, so the pathfinder is
    // released here as well; only when a task of this plugin was live, so pathing the player started is left alone.
    private static void ReleaseHelpers(bool taskWasLive)
    {
        if (!taskWasLive)
        {
            return;
        }

        NavmeshIPC.Instance.Stop();
    }
}
