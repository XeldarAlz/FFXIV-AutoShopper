namespace AutoShopper.Core.Planning;

internal static class WorldMasks
{
    // One bit per candidate world in a UInt128; more worlds than this exist in no travel scope.
    public const int Capacity = 128;

    public static UInt128 Bit(int index)
    {
        return UInt128.One << index;
    }

    public static bool Contains(UInt128 mask, int index)
    {
        return (mask & (UInt128.One << index)) != UInt128.Zero;
    }

    public static int Count(UInt128 mask)
    {
        return (int)UInt128.PopCount(mask);
    }

    public static UInt128 All(int count)
    {
        if (count >= Capacity)
        {
            return UInt128.MaxValue;
        }

        return (UInt128.One << count) - UInt128.One;
    }
}
