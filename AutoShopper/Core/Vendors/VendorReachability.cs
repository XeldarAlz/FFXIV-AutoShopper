using AutoShopper.Core.Ipc;
using AutoShopper.Core.Travel;
using AutoShopper.Core.Vendors.Model;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;

namespace AutoShopper.Core.Vendors;

internal static class VendorReachability
{
    public static bool CanReach(VendorSpot spot)
    {
        if (spot.TerritoryId == Svc.ClientState.TerritoryType)
        {
            return true;
        }

        if (spot.Zone == VendorZone.Residential)
        {
            return LifestreamIPC.Instance.IsAvailable
                && Svc.Data.GetExcelSheet<TerritoryType>().GetRowOrDefault(spot.TerritoryId) is { } territory
                && ZoneAetherytes.IsAttuned(territory.Aetheryte.RowId);
        }

        return ZoneAetherytes.TryFindNearest(spot.TerritoryId, spot.Position, out _)
            || (ZoneAetherytes.TryFindGateway(spot.TerritoryId, out var gateway) && ZoneAetherytes.IsAttuned(gateway.AetheryteId));
    }
}
