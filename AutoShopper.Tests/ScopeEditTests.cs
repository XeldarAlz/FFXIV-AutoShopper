using System.Collections.Immutable;
using AutoShopper.Core.Pricing.Model;
using static AutoShopper.Tests.PricingFixtures;

namespace AutoShopper.Tests;

public sealed class ScopeEditTests
{
    private static readonly ImmutableArray<ScopeWorld> Candidates = [Jenova, Faerie, Siren, Exodus, Famfrit, Mateus];

    [Fact]
    public void ARemovedWorldStaysOutWhenThePresetNoLongerHoldsIt()
    {
        List<uint> added = [];
        List<uint> excluded = [];

        Assert.True(ScopeResolver.SetIncluded(added, excluded, Exodus.WorldId, inPreset: false, included: true));
        Assert.True(ScopeResolver.SetIncluded(added, excluded, Exodus.WorldId, inPreset: true, included: false));

        Assert.Empty(added);
        Assert.Equal([Exodus.WorldId], excluded);
        var resolved = ScopeResolver.Resolve(ScopePreset.CurrentWorld, Faerie, Faerie, Candidates.AsSpan(), added, excluded, allowJapanese: false);
        Assert.DoesNotContain(Exodus, resolved);
    }

    [Fact]
    public void AnAddedWorldStaysInWhenThePresetLaterHoldsIt()
    {
        List<uint> added = [];
        List<uint> excluded = [];

        Assert.True(ScopeResolver.SetIncluded(added, excluded, Jenova.WorldId, inPreset: true, included: false));
        Assert.True(ScopeResolver.SetIncluded(added, excluded, Jenova.WorldId, inPreset: false, included: true));

        Assert.Equal([Jenova.WorldId], added);
        Assert.Empty(excluded);
        var resolved = ScopeResolver.Resolve(ScopePreset.HomeDataCenter, Faerie, Faerie, Candidates.AsSpan(), added, excluded, allowJapanese: false);
        Assert.Contains(Jenova, resolved);
    }

    [Fact]
    public void TakingAWorldOffAListLeavesTheOtherList()
    {
        List<uint> added = [Siren.WorldId];
        List<uint> excluded = [Mateus.WorldId];

        Assert.True(ScopeResolver.SetIncluded(added, excluded, Mateus.WorldId, inPreset: true, included: true));
        Assert.True(ScopeResolver.SetIncluded(added, excluded, Siren.WorldId, inPreset: false, included: false));

        Assert.Empty(added);
        Assert.Empty(excluded);
    }

    [Fact]
    public void AClickThatChangesNothingReportsNoChange()
    {
        List<uint> added = [Exodus.WorldId];
        List<uint> excluded = [];

        Assert.False(ScopeResolver.SetIncluded(added, excluded, Exodus.WorldId, inPreset: false, included: true));
        Assert.False(ScopeResolver.SetIncluded(added, excluded, Faerie.WorldId, inPreset: true, included: true));
        Assert.Equal([Exodus.WorldId], added);
        Assert.Empty(excluded);
    }

    [Fact]
    public void DuplicatesFromAnEditedFileAreAllRemoved()
    {
        List<uint> added = [Exodus.WorldId, Exodus.WorldId];
        List<uint> excluded = [];

        Assert.True(ScopeResolver.SetIncluded(added, excluded, Exodus.WorldId, inPreset: true, included: false));

        Assert.Empty(added);
        Assert.Equal([Exodus.WorldId], excluded);
    }
}
