using System.Collections.Frozen;

namespace AutoShopper.Core.Vendors.Model;

internal sealed class VendorPriceTable
{
    public static readonly VendorPriceTable Empty = new(new Dictionary<uint, long>());

    private readonly FrozenDictionary<uint, long> prices;

    public VendorPriceTable(IReadOnlyDictionary<uint, long> prices)
    {
        this.prices = prices.ToFrozenDictionary();
    }

    public bool IsEmpty => prices.Count == 0;

    public int Count => prices.Count;

    public bool TryGetPrice(uint itemId, out long unitPrice) => prices.TryGetValue(itemId, out unitPrice);

    public bool SamePrices(VendorPriceTable other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (prices.Count != other.prices.Count)
        {
            return false;
        }

        foreach (var (itemId, unitPrice) in prices)
        {
            if (!other.prices.TryGetValue(itemId, out var otherPrice) || otherPrice != unitPrice)
            {
                return false;
            }
        }

        return true;
    }
}
