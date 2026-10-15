using System.Threading.Tasks;
using AutoShopper.Core.External;
using AutoShopper.Core.Items;
using AutoShopper.Core.Market;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Shopping.Import;
using AutoShopper.Core.Vendors;
using AutoShopper.Core.Vendors.Model;
using ECommons.DalamudServices;
using Automation = clib.Services.Automation;

namespace AutoShopper.Core.Tasks;

internal sealed class AutoVendor(uint itemId, string itemName, long unitPrice) : AutoCommon
{
    private const string StopArgument = "stop";
    private const string Scope = "Vendor";
    private const string Usage = AshopConstants.LogPrefix + " Usage: /ashop vendor <item name or id>, or /ashop vendor stop.";

    public static void HandleCommand(string arguments, bool runActive, ItemCatalog items, VendorCatalog vendors)
    {
        var automation = clib.Services.Svc.Automation;
        if (arguments.Equals(StopArgument, StringComparison.OrdinalIgnoreCase))
        {
            Stop(automation);
            return;
        }

        if (automation.CurrentTask is AutoVendor)
        {
            MarketDebugCommands.PrintError("Vendor: a trip to a vendor is already running. /ashop vendor stop cancels it.");
            return;
        }

        if (runActive || automation.Running)
        {
            MarketDebugCommands.PrintError("Vendor: a task is running; stop it before using vendor.");
            return;
        }

        if (!ExternalPlugins.IsInstalled(ExternalPlugin.Vnavmesh))
        {
            MarketDebugCommands.PrintError("Vendor: walking to a vendor needs the pathfinding plugin listed on the Plugins page.");
            return;
        }

        if (arguments.Length == 0)
        {
            Svc.Chat.PrintError(Usage);
            return;
        }

        vendors.EnsureLoading();
        if (!items.IsReady || !vendors.IsReady)
        {
            MarketDebugCommands.PrintError("Vendor: the item and vendor data are still loading; try again in a moment.");
            return;
        }

        var resolution = items.Resolve(arguments);
        if (resolution.Status != ResolutionStatus.Resolved || !items.TryGetName(resolution.ItemId, out var name))
        {
            MarketDebugCommands.PrintError($"Vendor: '{arguments}' is not one marketable item.");
            return;
        }

        if (!vendors.Prices.TryGetPrice(resolution.ItemId, out var price))
        {
            MarketDebugCommands.PrintError($"Vendor: no NPC sells {name}.");
            return;
        }

        MarketDebugCommands.Print($"Vendor: heading for an NPC that sells {name} for {price} gil.");
        TaskStarter.TryStart(() => new AutoVendor(resolution.ItemId, name, price));
    }

    protected override async Task Execute()
    {
        var vendors = Plugin.Instance.Vendors;
        var line = new VendorLine(PlanDemand.ForPurchase(itemId, DemandQuality.Any, 1), 1, unitPrice);
        var route = VendorRoute.Plan([line], vendors, Svc.ClientState.TerritoryType, VendorReachability.CanReach);
        if (route.Stops.IsEmpty)
        {
            MarketDebugCommands.PrintError($"Vendor: no NPC that sells {itemName} can be reached from here.");
            return;
        }

        var stop = route.Stops[0];
        var shopId = stop.Purchases[0].ShopId;
        var vendorName = vendors.NpcName(stop.SpotIndex);
        var placeName = vendors.PlaceName(stop.SpotIndex);
        MarketDebugCommands.Print($"Vendor: going to {vendorName} in {placeName}, shop {shopId}.");
        Status = $"Going to {vendorName}";
        var reach = await GoToVendor(stop.Spot, vendors.PositionsOf(stop.Spot.NpcId, stop.Spot.TerritoryId), Scope);
        if (CancelToken.IsCancellationRequested)
        {
            Diag("Vendor: cancelled.");
            return;
        }

        if (reach != VendorReach.Reached)
        {
            MarketDebugCommands.PrintError($"Vendor: {vendorName} was not reached ({reach}). The log has the details.");
            return;
        }

        if (!await OpenVendorShop(stop.Spot.NpcId, shopId, Scope))
        {
            MarketDebugCommands.PrintError($"Vendor: the shop of {vendorName} did not open. The log has the details.");
            return;
        }

        Status = "Vendor shop open";
        MarketDebugCommands.Print(VendorShop.TryGetPrice(shopId, itemId, out var shownPrice)
            ? $"Vendor: the shop of {vendorName} is open and lists {itemName} at {shownPrice} gil (catalog {unitPrice} gil)."
            : $"Vendor: the shop of {vendorName} is open but does not list {itemName} for this character.");
    }

    private static void Stop(Automation automation)
    {
        if (automation.CurrentTask is not AutoVendor)
        {
            MarketDebugCommands.Print("Vendor: no trip to a vendor is running.");
            return;
        }

        automation.Stop();
        MarketDebugCommands.Print("Vendor: stopped.");
    }
}
