using System.Collections.Immutable;
using AutoShopper.Core.Pricing.Model;
using AutoShopper.Core.Pricing.Universalis;
using static AutoShopper.Tests.PricingFixtures;

namespace AutoShopper.Tests;

public sealed class UniversalisScopeTests
{
    [Fact]
    public void OneWorldIsAskedForById()
    {
        var query = Assert.Single(UniversalisScope.Plan([Faerie]));

        Assert.Equal(PriceQueryKind.World, query.Kind);
        Assert.Equal("54", query.Segment);
    }

    [Fact]
    public void WorldsOfOneDataCenterAreAskedForByItsName()
    {
        var query = Assert.Single(UniversalisScope.Plan([Faerie, Jenova, Siren]));

        Assert.Equal(PriceQueryKind.DataCenter, query.Kind);
        Assert.Equal("Aether", query.Segment);
        Assert.Equal(3, query.Worlds.Length);
    }

    [Fact]
    public void WorldsAcrossDataCentersAreAskedForByRegion()
    {
        var query = Assert.Single(UniversalisScope.Plan([Faerie, Exodus, Mateus]));

        Assert.Equal(PriceQueryKind.Region, query.Kind);
        Assert.Equal("North-America", query.Segment);
        Assert.Equal(3, query.Worlds.Length);
    }

    [Fact]
    public void UnknownRegionFallsBackToOneQueryPerDataCenter()
    {
        var first = new ScopeWorld(1, "One", 70, "Seventy", 9);
        var second = new ScopeWorld(2, "Two", 70, "Seventy", 9);
        var third = new ScopeWorld(3, "Three", 71, "SeventyOne", 9);

        var queries = UniversalisScope.Plan([first, second, third]);

        Assert.Equal(2, queries.Length);
        Assert.Equal(PriceQueryKind.DataCenter, queries[0].Kind);
        Assert.Equal("Seventy", queries[0].Segment);
        Assert.Equal(PriceQueryKind.World, queries[1].Kind);
        Assert.Equal("3", queries[1].Segment);
    }

    [Fact]
    public void ItemsAreChunkedAtTheApiLimitDistinctAndSorted()
    {
        var ids = new uint[250];
        for (var index = 0; index < ids.Length; index++)
        {
            ids[index] = (uint)(ids.Length - index);
        }

        var chunks = UniversalisScope.Chunk(UniversalisScope.Plan([Faerie, Jenova]), [.. ids, 7, 0]);

        Assert.Equal(3, chunks.Length);
        Assert.Equal(UniversalisApi.MaximumIdsPerRequest, chunks[0].ItemIds.Length);
        Assert.Equal(UniversalisApi.MaximumIdsPerRequest, chunks[1].ItemIds.Length);
        Assert.Equal(50, chunks[2].ItemIds.Length);
        Assert.Equal(1u, chunks[0].ItemIds[0]);
        Assert.Equal(250u, chunks[2].ItemIds[^1]);
    }

    [Fact]
    public void RegionQueriesTakeSmallerChunks()
    {
        var ids = new uint[60];
        for (var index = 0; index < ids.Length; index++)
        {
            ids[index] = (uint)(index + 1);
        }

        var chunks = UniversalisScope.Chunk(UniversalisScope.Plan([Faerie, Exodus]), ids);

        Assert.Equal(3, chunks.Length);
        Assert.Equal(UniversalisApi.RegionIdsPerRequest, chunks[0].ItemIds.Length);
        Assert.Equal(10, chunks[2].ItemIds.Length);
    }

    [Fact]
    public void PathTrimsFieldsForEachAnswerShape()
    {
        var query = new PriceQuery(PriceQueryKind.DataCenter, "Aether", AetherWorlds);

        var multi = UniversalisApi.CurrentListingsPath(new PriceChunk(query, [5057, 2]));
        var single = UniversalisApi.CurrentListingsPath(new PriceChunk(query, [5057]));

        Assert.StartsWith("api/v2/Aether/5057,2?entries=0&fields=itemIDs,unresolvedItems,items.itemID,", multi);
        Assert.Contains("items.listings.onMannequin", multi);
        Assert.StartsWith("api/v2/Aether/5057?entries=0&fields=itemID,", single);
        Assert.DoesNotContain("items.", single);
    }

    [Fact]
    public void RegionNameIsEscaped()
    {
        var query = new PriceQuery(PriceQueryKind.Region, "North America", [Faerie]);

        var path = UniversalisApi.CurrentListingsPath(new PriceChunk(query, [2]));

        Assert.StartsWith("api/v2/North%20America/2?", path);
    }

    [Fact]
    public void UserAgentNamesThePluginAndItsRepository()
    {
        Assert.Equal("AutoShopper/1.2.3.4 (+https://github.com/XeldarAlz/FFXIV-AutoShopper)", UniversalisApi.UserAgent("1.2.3.4"));
    }

    [Fact]
    public void NoItemsMeansNoRequests()
    {
        Assert.Empty(UniversalisScope.Chunk(UniversalisScope.Plan(AetherWorlds), ImmutableArray<uint>.Empty.AsSpan()));
    }
}
