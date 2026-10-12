using AutoShopper.Core.Game.Ops;
using System.Threading.Tasks;

namespace AutoShopper.Core.Tasks;

public abstract partial class AutoCommon
{
    // Leaving takes a confirmation click and a server round trip; a party still listed after this did not let go.
    private const int PartyLeaveWaitMs = 8_000;
    private const int PartyLeavePollFrames = 10;

    // Only a yes/no prompt that opened after the leave command is answered, so one left open by something else is
    // never clicked. True once the character is out of any party.
    protected async Task<bool> LeaveParty(string scope)
    {
        if (!PartyOps.InParty())
        {
            return true;
        }

        var promptBefore = PartyOps.OpenPromptId();
        Diag($"{scope}: leaving the party ({ConditionTag()})");
        Status = "Leaving the party";
        if (!PartyOps.SendLeave())
        {
            return false;
        }

        var deadline = Environment.TickCount64 + PartyLeaveWaitMs;
        while (Environment.TickCount64 < deadline && !CancelToken.IsCancellationRequested)
        {
            if (!PartyOps.InParty())
            {
                Diag($"{scope}: left the party");
                return true;
            }

            if (PartyOps.ConfirmPromptOpenedSince(promptBefore))
            {
                Diag($"{scope}: confirmed the leave prompt");
            }

            await NextFrame(PartyLeavePollFrames);
        }

        if (!PartyOps.InParty())
        {
            return true;
        }

        Warn($"{scope}: still in a party {PartyLeaveWaitMs / TimeUnits.MillisecondsPerSecond}s after asking to leave ({ConditionTag()})");
        return false;
    }
}
