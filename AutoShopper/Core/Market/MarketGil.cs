using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoShopper.Core.Market;

internal static unsafe class MarketGil
{
    public const long Unavailable = -1;

    public static long Read()
    {
        var inventory = InventoryManager.Instance();
        return inventory == null ? Unavailable : inventory->GetGil();
    }
}
