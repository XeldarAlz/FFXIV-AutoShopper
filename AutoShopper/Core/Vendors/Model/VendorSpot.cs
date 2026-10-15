using System.Numerics;

namespace AutoShopper.Core.Vendors.Model;

internal enum VendorZone : byte
{
    Town = 0,
    Field = 1,
    Residential = 2,
}

internal readonly record struct VendorSpot(uint NpcId, uint TerritoryId, Vector3 Position, VendorZone Zone);

internal readonly record struct VendorOffer(int SpotIndex, uint ShopId);

internal interface IVendorSpots
{
    ReadOnlySpan<VendorOffer> OffersFor(uint itemId);

    VendorSpot SpotAt(int index);
}
