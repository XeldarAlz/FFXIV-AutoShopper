namespace AutoShopper;

public sealed partial class Configuration
{
    // A character holds at most 999,999,999 gil, so no budget or penalty above it means anything.
    public const long MaximumGil = 999_999_999;

    // Zero means no cap is set yet; a run refuses to start until one is. There is no unlimited setting.
    public long MaxSpend { get; set; }

    public long GilReserve { get; set; }
}
