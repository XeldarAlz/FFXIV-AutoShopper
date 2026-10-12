namespace AutoShopper.Core.Planning;

internal readonly record struct DemandKey(uint ItemId, DemandQuality Quality) : IComparable<DemandKey>
{
    public bool IsDefinedQuality => Quality is DemandQuality.Any or DemandQuality.HighQuality or DemandQuality.NormalQuality;

    public bool Accepts(bool isHq)
    {
        return Quality switch
        {
            DemandQuality.HighQuality => isHq,
            DemandQuality.NormalQuality => !isHq,
            _ => true,
        };
    }

    public int CompareTo(DemandKey other)
    {
        var itemOrder = ItemId.CompareTo(other.ItemId);
        if (itemOrder != 0)
        {
            return itemOrder;
        }

        return ((byte)Quality).CompareTo((byte)other.Quality);
    }
}
