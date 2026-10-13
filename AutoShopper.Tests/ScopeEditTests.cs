using System.Collections.Immutable;
using AutoShopper.Core.Pricing.Model;
using static AutoShopper.Tests.PricingFixtures;

namespace AutoShopper.Tests;

public sealed class ScopeEditTests
{
    private const uint ChaosId = 6;
    private const byte Europe = 3;

    private static readonly ImmutableArray<ScopeWorld> Candidates = [Jenova, Faerie, Siren, Exodus, Famfrit, Mateus];
    private static readonly ScopeWorld Cerberus = new(80, "Cerberus", ChaosId, "Chaos", Europe);
    private static readonly ScopeWorld Omega = new(71, "Omega", ChaosId, "Chaos", Europe);

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

    [Fact]
    public void WorldsOutsideTheCurrentRegionNeverReachTheScope()
    {
        ImmutableArray<ScopeWorld> mixed = [Faerie, Jenova, Cerberus, Omega];

        var atHome = ScopeResolver.Resolve(ScopePreset.Region, Faerie, Faerie, mixed.AsSpan(), [Cerberus.WorldId], [], allowJapanese: true);
        var visiting = ScopeResolver.Resolve(ScopePreset.CurrentDataCenter, Cerberus, Faerie, mixed.AsSpan(), [Faerie.WorldId], [], allowJapanese: true);

        Assert.Equal([Faerie.WorldId, Jenova.WorldId], Ids(atHome));
        Assert.Equal([Cerberus.WorldId, Omega.WorldId], Ids(visiting));
        Assert.True(ScopeResolver.IsOutsideRegion(Faerie, Cerberus));
        Assert.False(ScopeResolver.IsOutsideRegion(Omega, Cerberus));
    }

    [Fact]
    public void AHomeRegionPresetIsEmptyWhileVisitingAnotherRegion()
    {
        ImmutableArray<ScopeWorld> mixed = [Faerie, Jenova, Cerberus, Omega];

        var resolved = ScopeResolver.Resolve(ScopePreset.HomeDataCenter, Cerberus, Faerie, mixed.AsSpan(), [], [], allowJapanese: true);

        Assert.Empty(resolved);
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
