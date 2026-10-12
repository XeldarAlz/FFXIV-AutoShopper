using Dalamud.Game.ClientState.Conditions;
using ECommons;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoShopper.Core.Game.Ops;

internal static unsafe class PartyOps
{
    public const ushort NoPrompt = 0;

    private const string LeaveCommand = "/leave";
    private const string SelectYesnoAddonName = "SelectYesno";
    // The game ignores addon clicks repeated faster than this.
    private const int ClickThrottleMs = 500;
    private const string ConfirmThrottleKey = "AutoShopper.Party.ConfirmLeave";

    // The party list holds only a party on this world; a cross-world party shows as a condition instead.
    public static bool InParty()
        => Svc.Party.Length > 1 || Svc.Condition[ConditionFlag.ParticipatingInCrossWorldPartyOrAlliance];

    public static bool SendLeave() => TrySend(LeaveCommand);

    // The yes/no prompt open right now, if any, so a later check can tell a prompt the leave raised from one left open.
    public static ushort OpenPromptId()
        => GenericHelpers.TryGetAddonByName<AtkUnitBase>(SelectYesnoAddonName, out var addon) ? addon->Id : NoPrompt;

    public static bool ConfirmPromptOpenedSince(ushort promptBefore)
    {
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(SelectYesnoAddonName, out var addon)
            || addon->Id == promptBefore
            || !GenericHelpers.IsAddonReady(addon)
            || !EzThrottler.Throttle(ConfirmThrottleKey, ClickThrottleMs))
        {
            return false;
        }

        new AddonMaster.SelectYesno((nint)addon).Yes();
        return true;
    }

    private static bool TrySend(string line)
    {
        try
        {
            Chat.SendMessage(line);
            return true;
        }
        catch (Exception exception)
        {
            RunLog.Warning(exception, $"Party: sending '{line}' failed.");
            return false;
        }
    }
}
