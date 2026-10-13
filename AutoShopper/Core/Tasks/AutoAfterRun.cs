using AutoShopper.Core.Game.Ops;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation;
using ECommons.DalamudServices;
using System.Threading.Tasks;

namespace AutoShopper.Core.Tasks;

// The action belongs to the run's character; another one logged in by then, after a transfer for one, is left alone.
public sealed class AutoAfterRun(AfterRunAction action, ulong characterContentId) : AutoCommon
{
    private const int ReadyWaitMs = 20_000;
    private const int ConfirmationWaitMs = 6_000;
    // The game drops a chat command issued in the middle of a transition.
    private const int PreCommandSettleMs = 800;
    // The second try covers a command eaten by lag or a window in the way.
    private const int LogoutAttempts = 2;
    private const string LogoutCommand = "/logout";
    private const string CloseGameCommand = "/xlkill";

    private readonly AfterRunAction action = action;

    // A mob still on the character keeps it in combat, where neither the logout nor a clean exit goes through, so the
    // action waits for a safe moment.
    protected override async Task Execute()
    {
        if (!await WaitUntilTimed(IsSafeToFinish, ReadyWaitMs, "after-run-ready"))
        {
            if (!CancelToken.IsCancellationRequested)
            {
                Warn($"After-run {action} skipped: no safe moment (combat, casting, or a zone change) within {ReadyWaitMs / TimeUnits.MillisecondsPerSecond}s.");
            }

            return;
        }

        if (!OwnCharacter())
        {
            return;
        }

        switch (action)
        {
            case AfterRunAction.Logout:
                await LogOut();
                break;
            case AfterRunAction.CloseGame:
                await CloseGame();
                break;
        }
    }

    private async Task LogOut()
    {
        Status = "Logging out";
        for (var attempt = 1; attempt <= LogoutAttempts; attempt++)
        {
            await DelayMs(PreCommandSettleMs);
            if (CancelToken.IsCancellationRequested || !OwnCharacter())
            {
                return;
            }

            Diag($"After-run: sending {LogoutCommand} (attempt {attempt}/{LogoutAttempts}).");
            Chat.ExecuteCommand(LogoutCommand);
            if (!await WaitUntilTimed(DialogDriver.ConfirmationOpen, ConfirmationWaitMs, $"logout-confirmation#{attempt}"))
            {
                continue;
            }

            if (DialogDriver.Confirm())
            {
                Diag("After-run: logout confirmation accepted.");
                return;
            }
        }

        if (!CancelToken.IsCancellationRequested)
        {
            Warn($"After-run: the logout confirmation was not accepted after {LogoutAttempts} tries; the character may still be logged in.");
        }
    }

    private async Task CloseGame()
    {
        Status = "Closing the game";
        await DelayMs(PreCommandSettleMs);
        if (CancelToken.IsCancellationRequested || !OwnCharacter())
        {
            return;
        }

        Diag($"After-run: closing the game with {CloseGameCommand}.");
        Chat.ExecuteCommand(CloseGameCommand);
    }

    private bool OwnCharacter()
    {
        var contentId = Svc.PlayerState.ContentId;
        if (characterContentId != 0 && contentId == characterContentId)
        {
            return true;
        }

        Warn($"After-run {action} skipped: the character logged in is not the one the run belongs to.");
        return false;
    }

    private static bool IsSafeToFinish()
        => Svc.Objects.LocalPlayer is not null
        && !Svc.Condition[ConditionFlag.InCombat]
        && !Svc.Condition[ConditionFlag.BetweenAreas]
        && !Svc.Condition[ConditionFlag.Casting];
}
