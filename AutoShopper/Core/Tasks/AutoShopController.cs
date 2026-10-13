using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.External;
using AutoShopper.Core.Ipc;
using AutoShopper.Core.Ledger;
using AutoShopper.Core.Localization;
using AutoShopper.Core.Stats;
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
    // the Shop page offers to resume it later; it never resumes on its own.
    public void Interrupt()
    {
        if (shopRun is not { } run)
        {
            return;
        }

        var ending = session;
        stopping = false;
        currentTask = null;
        PauseReason = PauseReason.None;
        Svc.Automation.Stop();
        ReleaseHelpers();
        if (run.IsAuthorized && run.FinalState is null)
        {
            run.Checkpoint(DateTime.UtcNow);
        }

        if (ending is not null)
        {
            ending.Interrupted = true;
        }

        FinalizeRun(ending);
        ClearRun();
        Plugin.Instance.Ledger.Invalidate();
        Diag($"Run {run.RunId} interrupted; its journal is kept for a resume.");
        if (run.IsAuthorized && !run.DryRun)
        {
            ECommons.DalamudServices.Svc.Chat.Print($"{AshopConstants.LogPrefix} {Loc.T(L.Buyer.ChatInterrupted)}");
        }
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
        if (shopRun is { InFlight: not null } waiting)
        {
            if (stopping)
            {
                return;
            }

            stopping = true;
            currentTask = null;
            PauseReason = PauseReason.None;
            Svc.Automation.Stop();
            ReleaseHelpers();
            waiting.Settled = StopAfterSettlement;
            Diag("Stop requested while a purchase is in flight; the run ends once it is answered.");
            ECommons.DalamudServices.Svc.Chat.Print($"{AshopConstants.LogPrefix} {Loc.T(L.Buyer.ChatStopping)}");
            return;
        }

        stopping = false;
        var ending = session;
        currentTask = null;
        PauseReason = PauseReason.None;
        Svc.Automation.Stop();
        if (ending is not null)
        {
            EndShopRun(ending, RunState.Stopped, "stopped by the player");
            ending.Outcome ??= RunOutcome.Stopped;
            ReleaseHelpers();
            if (shopRun is null)
            {
                AbandonTravel();
            }
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
        ResetFaultBudget();
        session = newSession;
        runTaskFactory = taskFactory;
        Diag($"Run starting: {plan}.");
        LaunchRun(newSession);
    }

    private void LaunchRun(ShopSession owningSession)
    {
        progress.Reset();
        progress.SetPhase(RunPhase.Preparing);
        RunTask(runTaskFactory!(owningSession), () => OnRunEnded(owningSession));
    }

    private bool CanRestart() => runTaskFactory is not null;

    private void BeginShopRun(ShopRunState state, string plan)
    {
        stopping = false;
        shopRun = state;
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
        ending.Outcome ??= ShopRunState.OutcomeOf(run.FinalState ?? final);
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

    // A stopped run is over for good: its journey must not come back at the next login, and a transfer Lifestream
    // still has in hand would otherwise carry the character off after the Stop.
    private static void AbandonTravel()
    {
        Plugin.Instance.Configuration.ClearPendingJourney();
        var lifestream = LifestreamIPC.Instance;
        if (!lifestream.IsBusy())
        {
            return;
        }

        lifestream.Abort();
        Diag("Stop: Lifestream was busy and its travel was aborted with the run.");
    }

    // The movement library fires OnCompleted off the game thread: its await of the task does not return to the framework
    // scheduler, and the runtime moves the continuation to the thread pool. Recording a run reads the object table, which
    // Dalamud allows only on the game thread, so the hand-off is moved back there.
    private void RunTask(AutoCommon task, Action onCompleted)
    {
        currentTask = task;
        Svc.Automation.Start(task, OnCompleted: () => _ = ECommons.DalamudServices.Svc.Framework.RunOnFrameworkThread(() => HandOff(task, onCompleted)));
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
        shopRun?.Dispose();
        shopRun = null;
    }

    // A stopped task unwinds on a later frame, and after an unload that frame may never come, so the pathfinder is
    // released here as well.
    private static void ReleaseHelpers()
    {
        NavmeshIPC.Instance.Stop();
    }
}
