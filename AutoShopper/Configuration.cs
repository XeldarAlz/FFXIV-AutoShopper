using AutoShopper.Core.Changelog;
using Dalamud.Configuration;
using ECommons.Throttlers;
using Newtonsoft.Json;

namespace AutoShopper;

[Serializable]
public sealed partial class Configuration : IPluginConfiguration
{
    // 1: the first version of these settings. 2: adds the budget, scope and planning settings, and a line's
    // MaxUnitPrice now reads as the price of one unit including the 5% market fee. A later layout change bumps it and
    // adds its step to Migrate.
    public const int CurrentVersion = 2;

    [JsonIgnore]
    private bool savePending;

    public int Version { get; set; }

    public bool AutoShowOnLogin { get; set; } = false;

    public string Language { get; set; } = "";

    // Never fires on a manual Stop or a fault.
    public AfterRunAction AfterRun { get; set; } = AfterRunAction.StayLoggedIn;

    public bool AutoPauseInContent { get; set; } = true;

    public string LastSeenChangelogVersion { get; set; } = string.Empty;

    [JsonIgnore]
    public bool HasUnseenChangelog => !string.Equals(LastSeenChangelogVersion, ChangelogData.LatestVersion, StringComparison.Ordinal);

    public static Configuration CreateFresh() => new()
    {
        Version = CurrentVersion,
    };

    // True when the loaded config was older or needed a repair, so the caller saves it once.
    public bool Migrate()
    {
        var changed = RepairShoppingSettings();
        changed |= RepairMarketSettings();
        changed |= RepairRunSettings();
        changed |= RepairSafetySettings();
        if (Version >= CurrentVersion)
        {
            return changed;
        }

        Version = CurrentVersion;
        return true;
    }

    public void MarkChangelogSeen()
    {
        if (!HasUnseenChangelog)
        {
            return;
        }

        LastSeenChangelogVersion = ChangelogData.LatestVersion;
        Save();
    }

    public void Save()
    {
        savePending = false;
        Plugin.PluginInterface.SavePluginConfig(this);
    }

    // A slider or a text box changes on every frame it is edited, so the throttle drops most calls; the change stays
    // pending, and FlushPendingSave writes the last one once the throttle window has passed.
    public void SaveDebounced()
    {
        savePending = true;
        FlushPendingSave();
    }

    public void FlushPendingSave()
    {
        if (savePending && EzThrottler.Throttle(Core.AshopConstants.ThrottleKeys.Save, Core.AshopConstants.SaveThrottleMs))
        {
            Save();
        }
    }

    public void SaveIfPending()
    {
        if (savePending)
        {
            Save();
        }
    }
}
