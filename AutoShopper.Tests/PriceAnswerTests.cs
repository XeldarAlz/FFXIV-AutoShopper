using System.Collections.Immutable;
using AutoShopper.Core.Pricing.Model;
using AutoShopper.Core.Pricing.Universalis;
using static AutoShopper.Tests.PricingFixtures;

namespace AutoShopper.Tests;

public sealed class PriceAnswerTests
{
    private static readonly ImmutableArray<ScopeWorld> Allowed = [Jenova, Faerie, Siren];

    [Fact]
    public void ANotFoundForSeveralItemsKeepsTheirPreviousPricesMarkedFailed()
    {
        var first = Build([Translate(HighQualityItem, FireShard)], null);
        Assert.True(first.TryGetItem(FireShard, out var before));
        var chunk = Chunk(PriceQueryKind.DataCenter, "Aether", Allowed, HighQualityItem, FireShard);

        var second = Build([ChunkPrices.FailedWith(chunk, PriceFailure.NotFound)], first);

        Assert.True(second.TryGetItem(FireShard, out var kept));
        Assert.True(kept.Offers.AsSpan().SequenceEqual(before.Offers.AsSpan()));
        Assert.Equal(before.Coverage | PriceCoverage.RequestFailed, kept.Coverage);
        Assert.Equal(PriceCoverage.None, kept.Coverage & PriceCoverage.NotFound);
        Assert.Equal(1, second.FailedRequestCount);
        Assert.Equal(CountOffers(first), second.Offers.Length);
    }

    [Fact]
    public void ANotFoundForOneItemMeansUniversalisDoesNotTrackIt()
    {
        var first = Build([Translate(FireShard)], null);
        var chunk = Chunk(PriceQueryKind.DataCenter, "Aether", Allowed, FireShard);

        var second = Build([ChunkPrices.FailedWith(chunk, PriceFailure.NotFound)], first);

        Assert.True(second.TryGetItem(FireShard, out var item));
        Assert.Equal(PriceCoverage.NotFound, item.Coverage);
        Assert.Empty(item.Offers);
        Assert.Equal(0, second.FailedRequestCount);
    }

    private static PriceSnapshot Build(IReadOnlyList<ChunkPrices> chunks, PriceSnapshot? previous)
    {
        return PriceSnapshotBuilder.Build(previous is null ? 1 : previous.Generation + 1, FetchedAt, Allowed, chunks, previous);
    }

    private static ChunkPrices Translate(params uint[] itemIds)
    {
        return UniversalisTranslator.Translate(Read("dc-multi-trimmed.json"), Chunk(PriceQueryKind.DataCenter, "Aether", Allowed, itemIds));
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
