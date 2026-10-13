using ECommons;
using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoShopper.Core.Travel;

// The aetheryte's world list stays open after the game turns a congested world away, and an open list blocks the next
// trip, so the run closes it the way its own cancel button does. Framework thread only.
internal static unsafe class WorldTravelWindow
{
    private const string AddonName = "WorldTravelSelect";
    private const int CancelCallback = -1;

    public static bool CloseIfOpen()
    {
        if (!GenericHelpers.TryGetAddonByName<AtkUnitBase>(AddonName, out var addon) || !GenericHelpers.IsAddonReady(addon))
        {
            return false;
        }

        Callback.Fire(addon, true, CancelCallback);
        RunLog.Debug($"Travel: closed {AddonName}.");
        return true;
    }
}
