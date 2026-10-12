using AutoShopper.Core.Shopping.Model;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace AutoShopper.Core.Inventory;

// Owned counts for the items the window is showing, read on the framework thread about once a second and cached for
// the draw code. The game's count covers the four bags, the crystal pouch, the armory chest and equipped gear;
// saddlebags and retainers are not counted.
internal sealed unsafe class InventoryReader : IDisposable
{
    private const long RefreshIntervalMs = 1000;

    private readonly Dictionary<uint, OwnedCount> counts = [];
    private uint[] watched = [];
    private long lastRefreshMs;
    private bool pruneNeeded;

    public InventoryReader()
    {
        Svc.Framework.Update += OnUpdate;
    }

    public bool HasData { get; private set; }

    // Rises whenever a count, the gil or the free slots change, so text built from them is rebuilt only then.
    public int Revision { get; private set; }

    public uint Gil { get; private set; }

    public int FreeSlots { get; private set; }

    public void Dispose() => Svc.Framework.Update -= OnUpdate;

    public void Watch(ReadOnlySpan<uint> itemIds)
    {
        if (itemIds.SequenceEqual(watched))
        {
            return;
        }

        watched = itemIds.ToArray();
        pruneNeeded = true;
        lastRefreshMs = 0;
    }

    public bool TryGetOwned(uint itemId, out OwnedCount owned)
    {
        if (HasData && counts.TryGetValue(itemId, out owned))
        {
            return true;
        }

        owned = default;
        return false;
    }

    private void OnUpdate(IFramework framework)
    {
        var now = Environment.TickCount64;
        if (now - lastRefreshMs < RefreshIntervalMs)
        {
            return;
        }

        lastRefreshMs = now;
        Refresh();
    }

    private void Refresh()
    {
        var manager = InventoryManager.Instance();
        if (manager == null || !Svc.ClientState.IsLoggedIn)
        {
            if (HasData)
            {
                HasData = false;
                counts.Clear();
                Revision++;
            }

            return;
        }

        var changed = !HasData;
        var gil = manager->GetGil();
        var freeSlots = (int)manager->GetEmptySlotsInBag();
        changed |= gil != Gil || freeSlots != FreeSlots;
        Gil = gil;
        FreeSlots = freeSlots;

        if (pruneNeeded)
        {
            changed |= DropUnwatched();
            pruneNeeded = false;
        }

        var items = watched;
        for (var index = 0; index < items.Length; index++)
        {
            var itemId = items[index];
            var owned = new OwnedCount(
                manager->GetInventoryItemCount(itemId, isHq: false, checkEquipped: true, checkArmory: true),
                manager->GetInventoryItemCount(itemId, isHq: true, checkEquipped: true, checkArmory: true));
            if (counts.TryGetValue(itemId, out var previous) && previous == owned)
            {
                continue;
            }

            counts[itemId] = owned;
            changed = true;
        }

        HasData = true;
        if (changed)
        {
            Revision++;
        }
    }

    private bool DropUnwatched()
    {
        var keep = new HashSet<uint>(watched);
        var dropped = false;
        foreach (var itemId in counts.Keys)
        {
            if (keep.Contains(itemId))
            {
                continue;
            }

            counts.Remove(itemId);
            dropped = true;
        }

        return dropped;
    }
}
