namespace AutoShopper;

public sealed partial class Configuration
{
    public const int MaximumStopsLimit = 20;
    public const int MaximumPremiumPercent = 100;
    public const int MinimumStaleHours = 1;
    // Three days; the warning exists to flag old data, and data older than that is old under any reading.
    public const int MaximumStaleHours = 72;

    public PlanningMode PlanningMode { get; set; } = PlanningMode.Balanced;

    // Zero means no limit.
    public int MaxStops { get; set; }

    public long StopPenaltyGil { get; set; } = 2_000;

    public long CrossDataCenterPenaltyGil { get; set; } = 10_000;

    public int FewestStopsPremiumPercent { get; set; } = 10;

    public int StaleAfterHours { get; set; } = 3;

    public bool BuyFromVendors { get; set; } = true;

    // A hand-edited file can carry nulls or values out of range; they are put back in range once on load.
    private bool RepairShoppingSettings()
    {
        var changed = false;
        if (AllowedWorlds is null)
        {
            AllowedWorlds = [];
            changed = true;
        }

        if (ExcludedWorlds is null)
        {
            ExcludedWorlds = [];
            changed = true;
        }

        changed |= Clamp(MaxSpend, 0, MaximumGil, value => MaxSpend = value);
        changed |= Clamp(GilReserve, 0, MaximumGil, value => GilReserve = value);
        changed |= Clamp(StopPenaltyGil, 0, MaximumGil, value => StopPenaltyGil = value);
        changed |= Clamp(CrossDataCenterPenaltyGil, 0, MaximumGil, value => CrossDataCenterPenaltyGil = value);
        changed |= Clamp(MaxStops, 0, MaximumStopsLimit, value => MaxStops = (int)value);
        changed |= Clamp(FewestStopsPremiumPercent, 0, MaximumPremiumPercent, value => FewestStopsPremiumPercent = (int)value);
        changed |= Clamp(StaleAfterHours, MinimumStaleHours, MaximumStaleHours, value => StaleAfterHours = (int)value);
        if (!Enum.IsDefined(PlanningMode))
        {
            PlanningMode = PlanningMode.Balanced;
            changed = true;
        }

        if (!Enum.IsDefined(ScopePreset))
        {
            ScopePreset = Core.Pricing.Model.ScopePreset.HomeDataCenter;
            changed = true;
        }

        return changed;
    }

    private static bool Clamp(long value, long minimum, long maximum, Action<long> assign)
    {
        var clamped = Math.Clamp(value, minimum, maximum);
        if (clamped == value)
        {
            return false;
        }

        assign(clamped);
        return true;
    }
}
