using AutoShopper.Core.Localization;
using ECommons.DalamudServices;

namespace AutoShopper.Core.Tasks;

// Starts a task of its own, outside a run. Building a task hooks game functions and throws when one is missing; that
// must not reach a command handler or a window's draw, so it ends in the log and the chat instead.
internal static class TaskStarter
{
    public static bool TryStart(Func<AutoCommon> build)
    {
        try
        {
            clib.Services.Svc.Automation.Start(build());
            return true;
        }
        catch (Exception exception)
        {
            RunLog.Error(exception, "A task could not be started");
            Svc.Chat.PrintError($"{AshopConstants.LogPrefix} {Loc.T(L.Buyer.ChatTaskFailed)}");
            return false;
        }
    }
}
