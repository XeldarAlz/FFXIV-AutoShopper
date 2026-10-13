namespace AutoShopper.Core.Market.Model;

// One listing as a page of offerings carried it, before the game copied it into its search proxy.
internal readonly record struct OfferedListing(
    ulong ListingId,
    ulong RetainerId,
    uint ItemId,
    bool IsHq,
    uint Quantity,
    uint UnitPrice,
    uint TotalTax,
    bool OnMannequin)
{
    public override string ToString()
        => ListingFields.Describe(ListingId, RetainerId, ItemId, IsHq, Quantity, UnitPrice, TotalTax, OnMannequin);
}
