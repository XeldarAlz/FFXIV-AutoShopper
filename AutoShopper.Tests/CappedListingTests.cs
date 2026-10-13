using System.Collections.Immutable;
using System.Text;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Preview.Model;
using AutoShopper.Core.Pricing.Model;
using AutoShopper.Core.Pricing.Universalis;
using static AutoShopper.Tests.PricingFixtures;

namespace AutoShopper.Tests;

public sealed class CappedListingTests
{
    private const uint ListedItem = 10;

    private static readonly ImmutableArray<ScopeWorld> Allowed = [Jenova, Faerie, Siren];

    [Fact]
    public void HighQualityMissingOnACappedWorldIsReportedAsCapped()
    {
        var snapshot = Build([CappedNormalQuality()]);
        var missing = new UnresolvedDemand(new DemandKey(ListedItem, DemandQuality.HighQuality), 5, 0, 5, UnresolvedReason.NoListings);

        Assert.Equal(PreviewReason.Capped, PreviewReview.ReasonFor(missing, snapshot));
    }

    [Fact]
    public void ShortStockOnACappedWorldIsReportedAsCapped()
    {
        var snapshot = Build([CappedNormalQuality()]);
        var shortOnCappedWorld = new UnresolvedDemand(new DemandKey(ListedItem, DemandQuality.NormalQuality), 500, 100, 400, UnresolvedReason.InsufficientStock);

        Assert.Equal(PreviewReason.Capped, PreviewReview.ReasonFor(shortOnCappedWorld, snapshot));
    }

    [Fact]
    public void WithoutACapTheReasonsStayAsTheyWere()
    {
        var snapshot = Build([Translate(HighQualityItem, FireShard)]);
        var missing = new UnresolvedDemand(new DemandKey(FireShard, DemandQuality.HighQuality), 5, 0, 5, UnresolvedReason.NoListings);
        var shortStock = new UnresolvedDemand(new DemandKey(FireShard, DemandQuality.Any), 9_999, 10, 9_989, UnresolvedReason.InsufficientStock);
        var aboveCap = new UnresolvedDemand(new DemandKey(ListedItem, DemandQuality.Any), 5, 0, 5, UnresolvedReason.AboveUnitCap);

        Assert.Equal(PreviewReason.NoListings, PreviewReview.ReasonFor(missing, snapshot));
        Assert.Equal(PreviewReason.InsufficientStock, PreviewReview.ReasonFor(shortStock, snapshot));
        Assert.Equal(PreviewReason.AboveUnitCap, PreviewReview.ReasonFor(aboveCap, Build([CappedNormalQuality()])));
        Assert.Equal(PreviewReason.InsufficientStock, PreviewReview.ReasonFor(shortStock, null));
    }

    private static ChunkPrices CappedNormalQuality()
    {
        var builder = new StringBuilder("""{"itemID":10,"worldID":54,"lastUploadTime":1791790303256,"hasData":true,"listings":[""");
        for (var index = 0; index < UniversalisTranslator.TruncationThreshold; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append("{\"listingID\":\"").Append(index + 1).Append("\",\"pricePerUnit\":10,\"quantity\":1,\"hq\":false,\"onMannequin\":false}");
        }

        builder.Append("]}");
        return UniversalisTranslator.Translate(Encoding.UTF8.GetBytes(builder.ToString()), Chunk(PriceQueryKind.World, "54", [Faerie], ListedItem));
    }

    private static PriceSnapshot Build(IReadOnlyList<ChunkPrices> chunks)
    {
        return PriceSnapshotBuilder.Build(1, FetchedAt, Allowed, chunks, null);
    }

    private static ChunkPrices Translate(params uint[] itemIds)
    {
        return UniversalisTranslator.Translate(Read("dc-multi-trimmed.json"), Chunk(PriceQueryKind.DataCenter, "Aether", Allowed, itemIds));
    }
}
