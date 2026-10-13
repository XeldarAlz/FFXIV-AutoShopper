using AutoShopper.Core.Execution.Model;

namespace AutoShopper.Core.Market.Model;

internal enum ListingField : byte
{
    None = 0,
    ListingId = 1,
    RetainerId = 2,
    ItemId = 3,
    Quantity = 4,
    UnitPrice = 5,
    TotalTax = 6,
    Quality = 7,
    Mannequin = 8,
}

// The fields a purchase is identified and charged by, in the game's own widths, so the guarded listing, the live
// listing and the pending purchase can be compared field for field.
internal readonly record struct ListingFields(ulong ListingId, ulong RetainerId, uint ItemId, uint Quantity, uint UnitPrice, uint TotalTax, bool IsHq)
{
    public static bool TryFrom(in ObservedListing listing, out ListingFields fields)
    {
        if (listing.Quantity <= 0
            || listing.UnitPrice < 0 || listing.UnitPrice > uint.MaxValue
            || listing.TotalTax < 0 || listing.TotalTax > uint.MaxValue)
        {
            fields = default;
            return false;
        }

        fields = new ListingFields(listing.ListingId, listing.RetainerId, listing.ItemId, (uint)listing.Quantity, (uint)listing.UnitPrice, (uint)listing.TotalTax, listing.IsHq);
        return true;
    }

    public ListingField FirstDifference(in ListingFields other)
    {
        if (ListingId != other.ListingId)
        {
            return ListingField.ListingId;
        }

        if (RetainerId != other.RetainerId)
        {
            return ListingField.RetainerId;
        }

        if (ItemId != other.ItemId)
        {
            return ListingField.ItemId;
        }

        if (Quantity != other.Quantity)
        {
            return ListingField.Quantity;
        }

        if (UnitPrice != other.UnitPrice)
        {
            return ListingField.UnitPrice;
        }

        if (TotalTax != other.TotalTax)
        {
            return ListingField.TotalTax;
        }

        return IsHq != other.IsHq ? ListingField.Quality : ListingField.None;
    }

    public override string ToString()
        => Describe(ListingId, RetainerId, ItemId, IsHq, Quantity, UnitPrice, TotalTax, false);

    public static string Describe(ulong listingId, ulong retainerId, uint itemId, bool isHq, long quantity, long unitPrice, long totalTax, bool onMannequin)
        => $"listing {listingId}, retainer {retainerId}, item {itemId}{(isHq ? " HQ" : string.Empty)}, {quantity} x {unitPrice} + tax {totalTax}{(onMannequin ? ", mannequin" : string.Empty)}";
}
