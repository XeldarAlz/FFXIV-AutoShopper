using AutoShopper.Core.Pricing.Model;
using Newtonsoft.Json;

namespace AutoShopper;

public sealed partial class Configuration
{
    public ScopePreset ScopePreset { get; set; } = ScopePreset.HomeDataCenter;

    // Worlds the preset does not hold that the picker added.
    public List<uint> AllowedWorlds { get; set; } = [];

    // Worlds the preset holds that the picker removed.
    public List<uint> ExcludedWorlds { get; set; } = [];

    // Off keeps Japanese worlds outside the character's data center out of every scope, since World Visit between
    // Japanese data centers changed on 13 October 2026 and that route is untested.
    public bool AllowJapaneseDataCenters { get; set; }

    // Rises with every scope edit, so the resolved world list is rebuilt only then.
    [JsonIgnore]
    public int ScopeRevision { get; private set; }

    public void SetScopePreset(ScopePreset preset)
    {
        if (ScopePreset == preset)
        {
            return;
        }

        ScopePreset = preset;
        ScopeChanged();
    }

    public void SetWorldIncluded(uint worldId, bool inPreset, bool included)
    {
        if (!ScopeResolver.SetIncluded(AllowedWorlds, ExcludedWorlds, worldId, inPreset, included))
        {
            return;
        }

        ScopeChanged();
    }

    public void SetAllowJapaneseDataCenters(bool allow)
    {
        if (AllowJapaneseDataCenters == allow)
        {
            return;
        }

        AllowJapaneseDataCenters = allow;
        ScopeChanged();
    }

    public void ResetWorlds()
    {
        if (AllowedWorlds.Count == 0 && ExcludedWorlds.Count == 0)
        {
            return;
        }

        AllowedWorlds.Clear();
        ExcludedWorlds.Clear();
        ScopeChanged();
    }

    private void ScopeChanged()
    {
        ScopeRevision++;
        SaveDebounced();
    }
}
