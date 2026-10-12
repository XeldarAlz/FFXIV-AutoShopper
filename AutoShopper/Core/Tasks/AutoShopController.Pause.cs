using clib.Services;

namespace AutoShopper.Core.Tasks;

internal enum PauseReason { None, Manual, InContent }

internal sealed partial class AutoShopController
{
    public PauseReason PauseReason { get; private set; } = PauseReason.None;

    public bool Paused => PauseReason != PauseReason.None;

    public bool CanPause => session is not null && Phase is not (RunPhase.Idle or RunPhase.Paused or RunPhase.Finishing);

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

        var pausing = session!;
        PauseReason = reason;
        progress.SetPhase(RunPhase.Paused);
        pausing.BeginPause();
        currentTask = null;
        Svc.Automation.Stop();
        ReleaseHelpers();

        Diag($"Run paused ({reason}); session kept.");
        ECommons.DalamudServices.Svc.Chat.Print(reason == PauseReason.InContent
            ? $"{AshopConstants.LogPrefix} Paused: you are in instanced content. The run resumes once you are back outside."
            : $"{AshopConstants.LogPrefix} Paused. Your run and session stats are kept until you resume or stop.");
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
