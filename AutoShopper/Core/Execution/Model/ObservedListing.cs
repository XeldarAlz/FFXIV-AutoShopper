using AutoShopper.Core.Planning;
using AutoShopper.Core.Shared;

namespace AutoShopper.Core.Execution.Model;

internal readonly record struct ObservedListing(
    ulong ListingId,
    ulong RetainerId,
    ulong SellerContentId,
    uint ItemId,
    bool IsHq,
    int Quantity,
    long UnitPrice,
    long TotalTax,
    bool IsMannequin,
    bool IsSellingAsSet,
    uint WorldId,
    int SearchGeneration,
    DateTime ObservedAtUtc)
{
    public bool TryGetPayable(out long payable)
    {
        return Gil.TryStackPayable(UnitPrice, Quantity, TotalTax, out payable);
    }

    public string ToListingKey()
    {
        return ListingKeys.ForGameListing(ListingId);
    }

    public PlanOffer ToPlanOffer(uint dataCenterId)
    {
        return new PlanOffer(WorldId, dataCenterId, ItemId, IsHq, Quantity, UnitPrice, TotalTax, ToListingKey(), ObservedAtUtc, false);
    }
}
