using System.Collections.Immutable;
using AutoShopper.Core.Pricing.Model;
using static AutoShopper.Tests.PricingFixtures;

namespace AutoShopper.Tests;

public sealed class ScopeResolverTests
{
    private const uint ElementalId = 1;
    private const uint GaiaId = 2;
    private const byte Japan = ScopeResolver.JapanRegion;

    private static readonly ImmutableArray<ScopeWorld> Candidates = [Jenova, Faerie, Siren, Exodus, Famfrit, Mateus];
    private static readonly ScopeWorld Alexander = new(43, "Alexander", GaiaId, "Gaia", Japan);
    private static readonly ScopeWorld Bahamut = new(69, "Bahamut", GaiaId, "Gaia", Japan);
    private static readonly ScopeWorld Aegis = new(90, "Aegis", ElementalId, "Elemental", Japan);
    private static readonly ScopeWorld Atomos = new(68, "Atomos", ElementalId, "Elemental", Japan);

    [Theory]
    [InlineData(ScopePreset.CurrentWorld, new uint[] { 53 })]
    [InlineData(ScopePreset.HomeDataCenter, new uint[] { 40, 54, 57 })]
    [InlineData(ScopePreset.CurrentDataCenter, new uint[] { 53, 35 })]
    [InlineData(ScopePreset.Region, new uint[] { 40, 54, 57, 53, 35, 37 })]
    public void PresetNamesItsWorlds(ScopePreset preset, uint[] expected)
    {
        var resolved = ScopeResolver.Resolve(preset, Exodus, Faerie, Candidates.AsSpan(), [], [], allowJapanese: false);

        Assert.Equal(expected, Ids(resolved));
    }

    [Fact]
    public void ExclusionsRemovePresetWorldsAndAdditionsAddOthers()
    {
        var resolved = ScopeResolver.Resolve(ScopePreset.HomeDataCenter, Faerie, Faerie, Candidates.AsSpan(), [Mateus.WorldId], [Siren.WorldId], allowJapanese: false);

        Assert.Equal([40u, 54u, 37u], Ids(resolved));
    }

    [Fact]
    public void AnExclusionOutsideThePresetChangesNothing()
    {
        var resolved = ScopeResolver.Resolve(ScopePreset.CurrentWorld, Faerie, Faerie, Candidates.AsSpan(), [], [Exodus.WorldId], allowJapanese: false);

        Assert.Equal([54u], Ids(resolved));
    }

    [Fact]
    public void IncludedReflectsTheResolvedScope()
    {
        Assert.True(ScopeResolver.IsIncluded(ScopePreset.HomeDataCenter, Jenova, Faerie, Faerie, [], []));
        Assert.False(ScopeResolver.IsIncluded(ScopePreset.HomeDataCenter, Jenova, Faerie, Faerie, [], [Jenova.WorldId]));
        Assert.False(ScopeResolver.IsIncluded(ScopePreset.HomeDataCenter, Exodus, Faerie, Faerie, [], []));
        Assert.True(ScopeResolver.IsIncluded(ScopePreset.HomeDataCenter, Exodus, Faerie, Faerie, [Exodus.WorldId], []));
    }

    [Fact]
    public void JapaneseWorldsOutsideTheCurrentDataCenterStayOutUntilAllowed()
    {
        ImmutableArray<ScopeWorld> japanese = [Alexander, Bahamut, Aegis, Atomos];

        var off = ScopeResolver.Resolve(ScopePreset.Region, Alexander, Alexander, japanese.AsSpan(), [], [], allowJapanese: false);
        var on = ScopeResolver.Resolve(ScopePreset.Region, Alexander, Alexander, japanese.AsSpan(), [], [], allowJapanese: true);

        Assert.Equal([Alexander.WorldId, Bahamut.WorldId], Ids(off));
        Assert.Equal([Alexander.WorldId, Bahamut.WorldId, Aegis.WorldId, Atomos.WorldId], Ids(on));
        Assert.Equal(2, ScopeResolver.CountLeftOut(ScopePreset.Region, Alexander, Alexander, japanese.AsSpan(), [], [], allowJapanese: false));
        Assert.Equal(0, ScopeResolver.CountLeftOut(ScopePreset.Region, Alexander, Alexander, japanese.AsSpan(), [], [], allowJapanese: true));
    }

    [Fact]
    public void TheJapaneseSettingLeavesOtherRegionsAlone()
    {
        var resolved = ScopeResolver.Resolve(ScopePreset.Region, Exodus, Faerie, Candidates.AsSpan(), [], [], allowJapanese: false);

        Assert.Equal([40u, 54u, 57u, 53u, 35u, 37u], Ids(resolved));
        Assert.False(ScopeResolver.IsLeftOut(Mateus, Exodus, allowJapanese: false));
    }

    [Fact]
    public void AJapaneseWorldThePickerLeftOutIsNotCountedAsLeftOutBySetting()
    {
        ImmutableArray<ScopeWorld> japanese = [Alexander, Aegis];

        var count = ScopeResolver.CountLeftOut(ScopePreset.Region, Alexander, Alexander, japanese.AsSpan(), [], [Aegis.WorldId], allowJapanese: false);

        Assert.Equal(0, count);
    }

    private static uint[] Ids(ImmutableArray<ScopeWorld> worlds)
    {
        var ids = new uint[worlds.Length];
        for (var index = 0; index < ids.Length; index++)
        {
            ids[index] = worlds[index].WorldId;
        }

        return ids;
    }
}
