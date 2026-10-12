namespace AutoShopper.Core.Shopping.Model;

// Quantity is the purchase count for Buy demand and the owned target for ReachInventory demand.
public readonly record struct NormalizedDemand(
    uint ItemId,
    ItemQuality Quality,
    QuantityMode Mode,
    int Quantity,
    long MaxUnitPrice,
    int ExcessAllowance,
    int LineCount)
{
    public DemandKey Key => new(ItemId, Quality, Mode);
}
