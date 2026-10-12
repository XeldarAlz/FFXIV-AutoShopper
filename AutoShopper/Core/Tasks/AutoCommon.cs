using AutoShopper.Core.Game;
using AutoShopper.Core.Ipc;
using clib.TaskSystem;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using AutoTask = clib.Services.AutoTask;

namespace AutoShopper.Core.Tasks;

public abstract partial class AutoCommon : TaskBase
{
    private const int DelayPollFrames = 2;
    private const int WaitPollFrames = 30;
    private const int NavmeshPollFrames = 120;
    private const int TypingPollFrames = 10;

    protected void Diag(string message, [CallerFilePath] string callerFile = "") => RunLog.Info(message, callerFile);

    protected void Warn(string message, [CallerFilePath] string callerFile = "") => RunLog.Warning(message, callerFile);

    protected new async Task DelayMs(int milliseconds)
    {
        var deadline = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return;
            }

            await NextFrame(DelayPollFrames);
        }
    }

    // True once the drawn wait ran out; false when the run was cancelled or abortIf tripped first.
    protected async Task<bool> HumanDelay(HumanAction action, string label, Func<bool>? abortIf = null)
    {
        var delayMs = Humanizer.DrawDelayMs(action);
        if (delayMs <= 0)
        {
            return !CancelToken.IsCancellationRequested;
        }

        RunLog.Debug($"{label}: waiting {delayMs} ms before {Humanizer.Describe(action)}");
        var deadline = Environment.TickCount64 + delayMs;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested || (abortIf is not null && abortIf()))
            {
                return false;
            }

            await NextFrame(DelayPollFrames);
        }

        return !CancelToken.IsCancellationRequested;
    }

    // Pinned every frame because the movement library overwrites Status with raw coordinates mid-teleport.
    protected async Task RunWithStatusPinned(string label, Func<Task> work)
    {
        Status = label;
        IFramework.OnUpdateDelegate pin = _ => Status = label;
        Svc.Framework.Update += pin;
        try
        {
            await work();
        }
        finally
        {
            Svc.Framework.Update -= pin;
        }
    }

    // A leg run as its own task reports into its own Status, and the windows only read the run's.
    protected async Task RunWithStatusFrom(AutoTask source, Func<Task> work)
    {
        IFramework.OnUpdateDelegate mirror = _ => Status = source.Status;
        Svc.Framework.Update += mirror;
        try
        {
            await work();
        }
        finally
        {
            Svc.Framework.Update -= mirror;
        }
    }

    // No new move starts while the player types; a move already under way carries on, and the run goes ahead once the
    // text input is closed.
    protected async Task HoldWhileTyping(string what)
    {
        if (!TypingHoldsMovement())
        {
            return;
        }

        var previous = Status;
        Status = "Holding while you type";
        Diag($"A text input has focus; {what} waits until it is closed");
        while (!CancelToken.IsCancellationRequested && TypingHoldsMovement())
        {
            await NextFrame(TypingPollFrames);
        }

        Diag($"The text input closed; {what} goes ahead");
        Status = previous;
    }

    private static bool TypingHoldsMovement()
        => Plugin.Instance.Configuration.PauseWhileTyping && TextInputFocus.Active();

    protected async Task<bool> WaitUntilTimed(Func<bool> condition, int timeoutMs, string scope, int checkFrames = WaitPollFrames)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var threw = false;
        while (Environment.TickCount64 < deadline)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            if (ConditionHolds(condition, scope, ref threw))
            {
                return true;
            }

            await NextFrame(checkFrames);
        }

        Diag($"WAIT TIMEOUT: '{scope}' not satisfied within {timeoutMs / TimeUnits.MillisecondsPerSecond}s");
        return false;
    }

    // After a teleport the destination mesh is still building, and pathfind queries issued now race it and fault.
    protected async Task WaitForNavmeshReady(int timeoutMs, int pollFrames = NavmeshPollFrames)
    {
        var navmesh = NavmeshIPC.Instance;
        if (navmesh.IsReady())
        {
            return;
        }

        var deadline = Environment.TickCount64 + timeoutMs;
        while (!navmesh.IsReady())
        {
            if (CancelToken.IsCancellationRequested)
            {
                return;
            }

            if (Environment.TickCount64 >= deadline)
            {
                Diag($"WAIT TIMEOUT: navmesh not ready within {timeoutMs / TimeUnits.MillisecondsPerSecond}s; proceeding anyway");
                return;
            }

            var progress = navmesh.BuildProgress();
            Status = progress is >= 0f and <= 1f
                ? $"Please wait, the navmesh is loading ({progress * 100f:F0}%)"
                : "Please wait, the navmesh is loading…";
            await NextFrame(pollFrames);
        }
    }

    private bool ConditionHolds(Func<bool> condition, string scope, ref bool threw)
    {
        try
        {
            return condition();
        }
        catch (Exception exception)
        {
            if (!threw)
            {
                Warn($"WaitUntilTimed '{scope}' condition threw (treating as unsatisfied; will retry until timeout): {exception.Message}");
                threw = true;
            }

            return false;
        }
    }
}
