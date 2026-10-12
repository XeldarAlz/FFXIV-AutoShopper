using AutoShopper.Core.Shared;

namespace AutoShopper.Core.Planning;

internal readonly record struct PlanOffer(
    uint WorldId,
    uint DataCenterId,
    uint ItemId,
    bool IsHq,
    int Quantity,
    long UnitPrice,
    long Fee,
    string ListingKey,
    DateTime ObservedAtUtc,
    bool IsEstimate)
{
    // Universalis adds floor(5%) of the stack total as the buyer fee; the live listing's TotalTax replaces it.
    public const int EstimatedFeePercent = 5;

    public bool TryGetPayable(out long payable)
    {
        return Gil.TryStackPayable(UnitPrice, Quantity, Fee, out payable);
    }

    public static bool TryCreateEstimate(
        uint worldId,
        uint dataCenterId,
        uint itemId,
        bool isHq,
        int quantity,
        long unitPrice,
        string listingKey,
        DateTime observedAtUtc,
        out PlanOffer offer)
    {
        if (quantity <= 0
            || unitPrice < 0
            || !Gil.TryMultiply(unitPrice, quantity, out var baseAmount)
            || !Gil.TryPercentFloor(baseAmount, EstimatedFeePercent, out var fee))
        {
            offer = default;
            return false;
        }

        offer = new PlanOffer(worldId, dataCenterId, itemId, isHq, quantity, unitPrice, fee, listingKey, observedAtUtc, true);
        return true;
    }
}
