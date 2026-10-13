using AutoShopper.Core.Game.Ops;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;

namespace AutoShopper.Core.Travel;

// What keeps a character from a data center transfer: a party, which the game refuses to carry across, or a window
// the transfer would tear down mid use. A reason reads as the end of "the transfer was not started: ...".
internal static class TransferGuard
{
    private static readonly string[] blockingAddons =
    [
        "RetainerList",
        "RetainerSellList",
        "InventoryRetainer",
        "InventoryRetainerLarge",
        "ItemSearch",
        "ItemSearchResult",
        "SelectYesno",
        "Trade",
    ];

    // The game refuses only a data center transfer to a party member, so a world change within one may skip that check.
    public static string? Blocker(bool partyBlocks = true)
    {
        if (partyBlocks && PartyOps.InParty())
        {
            return "the character is in a party";
        }

        if (Svc.Condition[ConditionFlag.BoundByDuty] || Svc.Condition[ConditionFlag.BoundByDuty56] || Svc.Condition[ConditionFlag.BoundByDuty95] || Svc.Condition[ConditionFlag.InDutyQueue])
        {
            return "the character is in a duty or a duty queue";
        }

        if (Svc.Condition[ConditionFlag.TradeOpen])
        {
            return "a trade is open";
        }

        for (var index = 0; index < blockingAddons.Length; index++)
        {
            var addon = Svc.GameGui.GetAddonByName(blockingAddons[index]);
            if (!addon.IsNull && addon.IsVisible)
            {
                return $"the {blockingAddons[index]} window is open";
            }
        }

        return null;
    }
}
