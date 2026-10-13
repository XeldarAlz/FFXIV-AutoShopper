using AutoShopper.Core.Localization;
using clib.Services;

namespace AutoShopper.Core.Tasks;

internal sealed partial class AutoShopController
{
    public PauseReason PauseReason { get; private set; } = PauseReason.None;

    // A pause asked for while a data center transfer of the run is armed; it waits for the transfer to end, because
    // cutting one short can strand the character mid transfer.
    private PauseReason deferredPause = PauseReason.None;

    public bool Paused => PauseReason != PauseReason.None;

    public bool CanPause => session is not null && Phase is not (RunPhase.Idle or RunPhase.Paused or RunPhase.Finishing or RunPhase.Stopping);

    public void Pause(PauseReason reason)
    {
        if (reason == PauseReason.None)
        {
            return;
        }

        if (Paused)
        {
            if (reason == PauseReason.Manual && PauseReason == PauseReason.InContent)
            {
                PauseReason = PauseReason.Manual;
                Diag("Auto-pause promoted to a manual pause; leaving content will no longer resume.");
            }

            return;
        }

        if (!CanPause)
        {
            if (reason == PauseReason.Manual)
            {
                ECommons.DalamudServices.Svc.Chat.Print($"{AshopConstants.LogPrefix} Nothing to pause.");
            }

            return;
        }

        if (currentTask is AutoShopRun && shopRun is { TransferArmed: true } transferring)
        {
            DeferPause(reason, transferring.TravelTarget);
            return;
        }

        var pausing = session!;
        var taskWasLive = Svc.Automation.Running;
        PauseReason = reason;
        progress.SetPhase(RunPhase.Paused);
        shopRun?.SetState(Execution.Model.RunState.Paused);
        pausing.BeginPause();
        currentTask = null;
        Svc.Automation.Stop();
        ReleaseHelpers(taskWasLive);
        AbandonTravel("the run was paused");

        Diag($"Run paused ({reason}); session kept.");
        ECommons.DalamudServices.Svc.Chat.Print(reason == PauseReason.InContent
            ? $"{AshopConstants.LogPrefix} Paused: you are in instanced content. The run resumes once you are back outside."
            : $"{AshopConstants.LogPrefix} Paused. Your run and session stats are kept until you resume or stop.");
    }

    // The run calls this once the transfer is over; true when the deferred pause was applied.
    internal bool ApplyDeferredPause()
    {
        var reason = deferredPause;
        deferredPause = PauseReason.None;
        if (reason == PauseReason.None)
        {
            return false;
        }

        Diag($"The data center transfer is over; applying the {reason} pause asked for during it.");
        Pause(reason);
        return Paused;
    }

    private void DropDeferred()
    {
        deferredPause = PauseReason.None;
        deferredStop = false;
    }

    private void DeferPause(PauseReason reason, string world)
    {
        var first = deferredPause == PauseReason.None;
        if (first || reason == PauseReason.Manual)
        {
            deferredPause = reason;
        }

        if (!first)
        {
            return;
        }

        Diag($"Pause ({reason}) asked for during the data center transfer to {world}; it waits for the transfer to end.");
        ECommons.DalamudServices.Svc.Chat.Print($"{AshopConstants.LogPrefix} {Loc.T(L.Route.ChatPauseAfterTransfer, world)}");
    }

    public void Resume()
    {
        if (!Paused)
        {
            return;
        }

        var resuming = session;
        if (resuming is null || !CanRestart())
        {
            Diag("Resume requested with no session or nothing to resume; stopping instead.");
            Stop();
            return;
        }

        PauseReason = PauseReason.None;
        resuming.EndPause();
        Diag("Resuming the run.");
        ECommons.DalamudServices.Svc.Chat.Print($"{AshopConstants.LogPrefix} Resuming the run.");
        LaunchRun(resuming);
    }

    public void TogglePause()
    {
        if (Paused)
        {
            Resume();
            return;
        }

        Pause(PauseReason.Manual);
    }
}
