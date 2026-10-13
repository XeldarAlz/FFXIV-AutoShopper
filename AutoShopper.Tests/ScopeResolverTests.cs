using System.Collections.Immutable;
using AutoShopper.Core.Pricing.Model;
using static AutoShopper.Tests.PricingFixtures;

namespace AutoShopper.Tests;

public sealed class ScopeResolverTests
{
    private static readonly ImmutableArray<ScopeWorld> Candidates = [Jenova, Faerie, Siren, Exodus, Famfrit, Mateus];

    [Theory]
    [InlineData(ScopePreset.CurrentWorld, new uint[] { 53 })]
    [InlineData(ScopePreset.HomeDataCenter, new uint[] { 40, 54, 57 })]
    [InlineData(ScopePreset.CurrentDataCenter, new uint[] { 53, 35 })]
    [InlineData(ScopePreset.Region, new uint[] { 40, 54, 57, 53, 35, 37 })]
    public void PresetNamesItsWorlds(ScopePreset preset, uint[] expected)
    {
        var resolved = ScopeResolver.Resolve(preset, Exodus, Faerie, Candidates.AsSpan(), [], []);

        Assert.Equal(expected, Ids(resolved));
    }

    [Fact]
    public void ExclusionsRemovePresetWorldsAndAdditionsAddOthers()
    {
        var resolved = ScopeResolver.Resolve(ScopePreset.HomeDataCenter, Faerie, Faerie, Candidates.AsSpan(), [Mateus.WorldId], [Siren.WorldId]);

        Assert.Equal([40u, 54u, 37u], Ids(resolved));
    }

    [Fact]
    public void AnExclusionOutsideThePresetChangesNothing()
    {
        var resolved = ScopeResolver.Resolve(ScopePreset.CurrentWorld, Faerie, Faerie, Candidates.AsSpan(), [], [Exodus.WorldId]);

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
