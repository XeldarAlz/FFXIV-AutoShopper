using System.Collections.Immutable;
using AutoShopper.Core.Pricing.Model;
using AutoShopper.Core.Pricing.Universalis;
using static AutoShopper.Tests.PricingFixtures;

namespace AutoShopper.Tests;

public sealed class PriceSnapshotTests
{
    private static readonly ImmutableArray<ScopeWorld> Allowed = [Jenova, Faerie, Siren];

    [Fact]
    public void SnapshotHoldsOffersPerItemAndFreshnessPerWorld()
    {
        var snapshot = Build([Translate("dc-multi-trimmed.json", HighQualityItem, FireShard, Gil, OneGilItem)]);

        Assert.Equal(4, snapshot.Items.Length);
        Assert.True(snapshot.TryGetItem(FireShard, out var fireShard));
        Assert.Equal(Allowed.Length, fireShard.Worlds.Length);
        Assert.Equal(6, fireShard.Offers.Length);
        Assert.Equal(CountOffers(snapshot), snapshot.Offers.Length);
        Assert.Equal(Allowed.Length, snapshot.Freshness.Length);

        var faerie = snapshot.Freshness[1];
        Assert.Equal(Faerie, faerie.World);
        Assert.Equal(3, faerie.ItemsWithData);
        Assert.Equal(1, faerie.ItemsWithoutData);
        Assert.Equal(FromUnixMilliseconds(1791759169759), faerie.OldestUploadUtc);
        Assert.Equal(FromUnixMilliseconds(1791791842650), faerie.NewestUploadUtc);
        Assert.Equal(1, snapshot.RequestCount);
        Assert.Equal(0, snapshot.FailedRequestCount);
    }

    [Fact]
    public void FailedRequestMarksItsItemsAndWorlds()
    {
        var chunk = Chunk(PriceQueryKind.DataCenter, "Aether", Allowed, FireShard);

        var snapshot = Build([ChunkPrices.FailedWith(chunk, PriceFailure.RetriesExhausted)]);

        Assert.True(snapshot.TryGetItem(FireShard, out var item));
        Assert.Equal(PriceCoverage.RequestFailed, item.Coverage);
        Assert.False(item.HasAnyUpload);
        Assert.Equal(1, snapshot.FailedRequestCount);
        Assert.Equal(PriceCoverage.RequestFailed, snapshot.Freshness[0].Coverage);
        Assert.False(snapshot.Freshness[0].HasData);
    }

    [Fact]
    public void AllowedWorldNoQueryCoveredIsMissing()
    {
        var chunk = Chunk(PriceQueryKind.DataCenter, "Aether", [Jenova, Faerie], FireShard);
        var prices = UniversalisTranslator.Translate(Read("dc-multi-trimmed.json"), chunk, FetchedAt);

        var snapshot = PriceSnapshotBuilder.Build(1, FetchedAt, [Jenova, Faerie, Exodus], [prices], null);

        Assert.True(snapshot.TryGetItem(FireShard, out var item));
        Assert.Equal(3, item.Worlds.Length);
        Assert.True((item.Coverage & PriceCoverage.MissingWorld) != 0);
        Assert.Equal(PriceCoverage.MissingWorld, snapshot.Freshness[2].Coverage);
    }

    [Fact]
    public void ChunksOfTwoQueriesMergeIntoOneItem()
    {
        var aether = Chunk(PriceQueryKind.DataCenter, "Aether", [Jenova, Faerie], HighQualityItem);
        var primal = Chunk(PriceQueryKind.World, "53", [Exodus], HighQualityItem);
        var aetherPrices = UniversalisTranslator.Translate(Read("dc-multi-trimmed.json"), aether, FetchedAt);
        var primalPrices = UniversalisTranslator.Translate(Read("region-multi-trimmed.json"), primal with { Query = primal.Query with { Kind = PriceQueryKind.Region } }, FetchedAt);

        var snapshot = PriceSnapshotBuilder.Build(1, FetchedAt, [Jenova, Faerie, Exodus], [aetherPrices, primalPrices], null);

        Assert.True(snapshot.TryGetItem(HighQualityItem, out var item));
        Assert.Equal(5, item.Offers.Length);
        Assert.Equal(PriceCoverage.None, item.Coverage & PriceCoverageGroups.Partial);
    }

    [Fact]
    public void PreviousPricesOverTheSameWorldsAreKept()
    {
        var first = Build([Translate("dc-multi-trimmed.json", HighQualityItem)]);
        var second = PriceSnapshotBuilder.Build(2, FetchedAt.AddMinutes(1), Allowed, [Translate("dc-multi-trimmed.json", FireShard)], first);

        Assert.True(second.TryGetItem(HighQualityItem, out var carried));
        Assert.Same(first.Items[0], carried);
        Assert.True(second.TryGetItem(FireShard, out _));
        Assert.Equal(CountOffers(second), second.Offers.Length);
    }

    [Fact]
    public void PreviousPricesOverOtherWorldsAreDropped()
    {
        var first = Build([Translate("dc-multi-trimmed.json", HighQualityItem)]);
        var chunk = Chunk(PriceQueryKind.World, "54", [Faerie], FireShard);
        var prices = UniversalisTranslator.Translate(Read("dc-multi-trimmed.json"), chunk with { Query = chunk.Query with { Kind = PriceQueryKind.DataCenter } }, FetchedAt);

        var second = PriceSnapshotBuilder.Build(2, FetchedAt, [Faerie], [prices], first);

        Assert.False(second.TryGetItem(HighQualityItem, out _));
        Assert.True(second.TryGetItem(FireShard, out _));
    }

    [Fact]
    public void WorldSetsCompareRegardlessOfOrder()
    {
        var snapshot = Build([Translate("dc-multi-trimmed.json", FireShard)]);

        Assert.True(snapshot.HasSameWorlds([Siren, Jenova, Faerie]));
        Assert.False(snapshot.HasSameWorlds([Siren, Jenova]));
        Assert.False(snapshot.HasSameWorlds([Siren, Jenova, Exodus]));
    }

    private static PriceSnapshot Build(IReadOnlyList<ChunkPrices> chunks)
    {
        return PriceSnapshotBuilder.Build(1, FetchedAt, Allowed, chunks, null);
    }

    private static ChunkPrices Translate(string fixture, params uint[] itemIds)
    {
        return UniversalisTranslator.Translate(Read(fixture), Chunk(PriceQueryKind.DataCenter, "Aether", Allowed, itemIds), FetchedAt);
    }

    private static int CountOffers(PriceSnapshot snapshot)
    {
        var count = 0;
        for (var index = 0; index < snapshot.Items.Length; index++)
        {
            count += snapshot.Items[index].Offers.Length;
        }

        return count;
    }
}
