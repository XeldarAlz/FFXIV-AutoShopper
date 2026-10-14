using AutoShopper.Core;
using AutoShopper.Core.Market;
using AutoShopper.Core.Market.Model;
using Newtonsoft.Json;

namespace AutoShopper;

public sealed partial class Configuration
{
    public const float MinimumSearchIntervalSeconds = ListingRequestMachine.MinimumIntervalMs / (float)TimeUnits.MillisecondsPerSecond;
    public const float MaximumSearchIntervalSeconds = 10f;
    public const float MinimumPurchaseIntervalSeconds = PurchaseSender.MinimumIntervalMs / (float)TimeUnits.MillisecondsPerSecond;
    public const float MaximumPurchaseIntervalSeconds = 10f;
    public const int MinimumResultTimeoutSeconds = PurchaseEvidence.MinimumDeadlineMs / TimeUnits.MillisecondsPerSecond;
    public const int MaximumResultTimeoutSeconds = PurchaseEvidence.MaximumDeadlineMs / TimeUnits.MillisecondsPerSecond;

    public MarketCity PreferredMarketCity { get; set; } = MarketCityChoice.DefaultPreferred;

    public float SearchIntervalSeconds { get; set; } = MinimumSearchIntervalSeconds;

    public float PurchaseIntervalSeconds { get; set; } = PurchaseSender.DefaultIntervalMs / (float)TimeUnits.MillisecondsPerSecond;

    public int ResultTimeoutSeconds { get; set; } = PurchaseEvidence.DeadlineMs / TimeUnits.MillisecondsPerSecond;

    [JsonIgnore]
    public int SearchIntervalMs => ToMilliseconds(Math.Clamp(SearchIntervalSeconds, MinimumSearchIntervalSeconds, MaximumSearchIntervalSeconds));

    [JsonIgnore]
    public int PurchaseIntervalMs => ToMilliseconds(Math.Clamp(PurchaseIntervalSeconds, MinimumPurchaseIntervalSeconds, MaximumPurchaseIntervalSeconds));

    [JsonIgnore]
    public int ResultTimeoutMs => Math.Clamp(ResultTimeoutSeconds, MinimumResultTimeoutSeconds, MaximumResultTimeoutSeconds) * TimeUnits.MillisecondsPerSecond;

    private bool RepairMarketSettings()
    {
        var changed = false;
        if (!MarketBoardCatalog.Contains(PreferredMarketCity))
        {
            PreferredMarketCity = MarketCityChoice.DefaultPreferred;
            changed = true;
        }

        changed |= ClampSeconds(SearchIntervalSeconds, MinimumSearchIntervalSeconds, MaximumSearchIntervalSeconds, value => SearchIntervalSeconds = value);
        changed |= ClampSeconds(PurchaseIntervalSeconds, MinimumPurchaseIntervalSeconds, MaximumPurchaseIntervalSeconds, value => PurchaseIntervalSeconds = value);
        changed |= Clamp(ResultTimeoutSeconds, MinimumResultTimeoutSeconds, MaximumResultTimeoutSeconds, value => ResultTimeoutSeconds = (int)value);
        return changed;
    }

    private static bool ClampSeconds(float value, float minimum, float maximum, Action<float> assign)
    {
        var clamped = float.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : minimum;
        if (clamped.Equals(value))
        {
            return false;
        }

        assign(clamped);
        return true;
    }

    private static int ToMilliseconds(float seconds) => (int)MathF.Round(seconds * TimeUnits.MillisecondsPerSecond);
}
