namespace AutoShopper.Core.Market.Model;

internal static class MarketItemId
{
    // Market packets can mark a high quality item by adding one million to its id.
    public const uint HighQualityOffset = 1_000_000;

    public static uint Normalize(uint rawItemId)
        => rawItemId >= HighQualityOffset ? rawItemId - HighQualityOffset : rawItemId;

    public static bool HasHighQualityOffset(uint rawItemId) => rawItemId >= HighQualityOffset;

    // A high quality item may arrive with or without the offset; a normal quality item never carries it.
    public static bool Matches(uint rawItemId, uint expectedItemId, bool expectedHighQuality)
    {
        if (Normalize(rawItemId) != expectedItemId)
        {
            return false;
        }

        return expectedHighQuality || !HasHighQualityOffset(rawItemId);
    }
}
