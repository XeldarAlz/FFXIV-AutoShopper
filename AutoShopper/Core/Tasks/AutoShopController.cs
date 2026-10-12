using AutoShopper.Core.External;
using AutoShopper.Core.Ipc;
using AutoShopper.Core.Stats;
using clib.Services;

namespace AutoShopper.Core.Tasks;

internal sealed partial class AutoShopController
{
    private readonly RunProgress progress = new();

    private ShopSession? session;
    private Func<ShopSession, AutoCommon>? runTaskFactory;
    private AutoCommon? currentTask;

    public bool Running => Svc.Automation.Running || Paused;

    public string Status => PauseReason switch
    {
        PauseReason.InContent => "Paused while you are in content",
        PauseReason.Manual    => "Paused",
        _                     => Svc.Automation.CurrentTask?.Status ?? "Idle",
    };

    public RunPhase Phase => progress.Phase;

    public ShopSession? SessionSnapshot => session;

    private static void Diag(string message)
        => RunLog.Info(message);

    // The shop run task lands with the shopping lists; until then there is no run to begin, and the Start button says so.
    public void Start()
    {
        if (Running)
        {
            Diag("Start ignored: a run is already running.");
            return;
        }

        if (!RequiredPluginsReady())
        {
            return;
        }

        Diag("Start refused: shopping runs arrive in a later update, so there is no run to begin.");
        ECommons.DalamudServices.Svc.Chat.PrintError($"{AshopConstants.LogPrefix} Shopping runs arrive in a later update.");
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

    public void Stop()
    {
        var ending = session;
        currentTask = null;
        PauseReason = PauseReason.None;
        Svc.Automation.Stop();
        if (ending is not null)
        {
            ending.Outcome ??= RunOutcome.Stopped;
            ReleaseHelpers();
            AbandonTravel();
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
    }

    // A stopped task unwinds on a later frame, and after an unload that frame may never come, so the pathfinder is
    // released here as well.
    private static void ReleaseHelpers()
    {
        NavmeshIPC.Instance.Stop();
    }
}
