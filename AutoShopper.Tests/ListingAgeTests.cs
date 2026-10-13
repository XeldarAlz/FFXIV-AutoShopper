using System.Collections.Immutable;
using System.Text;
using System.Threading;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Pricing.Model;
using AutoShopper.Core.Pricing.Universalis;
using static AutoShopper.Tests.PricingFixtures;

namespace AutoShopper.Tests;

public sealed class ListingAgeTests
{
    private const uint ListedItem = 10;

    [Fact]
    public void AListingWithNoUploadOrReviewTimeHasAnUnknownAgeAndKeepsTheWorldFlag()
    {
        const string body = """
            {"itemIDs":[10],"items":{"10":{"itemID":10,"lastUploadTime":1791790303256,
            "worldUploadTimes":{"54":1791790303256},"hasData":true,"listings":[
            {"listingID":"x1","pricePerUnit":100,"quantity":2,"hq":false,"worldID":54,"onMannequin":false},
            {"listingID":"x2","pricePerUnit":120,"quantity":3,"hq":false,"worldID":40,"onMannequin":false,"lastReviewTime":0}]}},
            "unresolvedItems":[]}
            """;
        var chunk = Chunk(PriceQueryKind.DataCenter, "Aether", [Faerie, Jenova], ListedItem);

        var item = UniversalisTranslatorTests.Item(UniversalisTranslator.Translate(Encoding.UTF8.GetBytes(body), chunk), ListedItem);

        var unknown = Offer(item, "universalis:x2", 3);
        Assert.Equal(default, unknown.ObservedAtUtc);
        Assert.True(PriceSnapshotBuilder.TryFindWorld(item.Worlds, Jenova.WorldId, out var jenova));
        Assert.False(jenova.HasUpload);
        Assert.Equal(PriceCoverage.MissingWorld, jenova.Coverage);
        Assert.Equal(1, jenova.ListingCount);
        Assert.True((item.Coverage & PriceCoverage.MissingWorld) != 0);
        Assert.Equal(FromUnixMilliseconds(1791790303256), Offer(item, "universalis:x1", 2).ObservedAtUtc);
    }

    [Fact]
    public void AListingWithAReviewTimeStillDatesItsWorld()
    {
        const string body = """
            {"itemIDs":[10],"items":{"10":{"itemID":10,"lastUploadTime":1791790303256,"worldUploadTimes":{},"hasData":true,"listings":[
            {"listingID":"7","pricePerUnit":100,"quantity":2,"hq":false,"worldID":40,"onMannequin":false,"lastReviewTime":1791790000}]}},
            "unresolvedItems":[]}
            """;
        var chunk = Chunk(PriceQueryKind.DataCenter, "Aether", [Jenova], ListedItem);

        var item = UniversalisTranslatorTests.Item(UniversalisTranslator.Translate(Encoding.UTF8.GetBytes(body), chunk), ListedItem);

        var reviewed = DateTimeOffset.FromUnixTimeSeconds(1791790000).UtcDateTime;
        Assert.Equal(reviewed, item.Offers[0].ObservedAtUtc);
        Assert.Equal(reviewed, item.Worlds[0].UploadedAtUtc);
        Assert.Equal(PriceCoverage.None, item.Coverage);
    }

    [Fact]
    public void AnOfferOfUnknownAgeDoesNotWidenThePlanAgeRange()
    {
        var known = new DateTime(2026, 10, 12, 20, 0, 0, DateTimeKind.Utc);
        Assert.True(PlanOffer.TryCreateEstimate(Jenova.WorldId, AetherId, ListedItem, false, 5, 10, "universalis:a", known, out var dated));
        Assert.True(PlanOffer.TryCreateEstimate(Faerie.WorldId, AetherId, ListedItem, false, 5, 10, "universalis:b", default, out var undated));
        var request = new PlanRequest
        {
            Demands = [PlanDemand.ForPurchase(ListedItem, DemandQuality.Any, 10)],
            Offers = [dated, undated],
            Worlds = [Jenova.ToPlanWorld(), Faerie.ToPlanWorld()],
            Settings = new PlanSettings { Mode = PlanMode.Cheapest, CurrentWorld = Jenova.ToPlanWorld(), HomeWorld = Jenova.ToPlanWorld() },
            SourceLabel = "test",
        };

        var plan = new Planner(new FixedTimeProvider()).Plan(request, CancellationToken.None);

        Assert.True(plan.IsComplete);
        Assert.Equal(known, plan.Provenance.OldestObservationUtc);
        Assert.Equal(known, plan.Provenance.NewestObservationUtc);
        Assert.Equal(2, plan.Provenance.EstimatedLineCount);
    }

    private static PlanOffer Offer(ItemPrices item, string key, int quantity)
    {
        for (var index = 0; index < item.Offers.Length; index++)
        {
            if (item.Offers[index].ListingKey == key)
            {
                Assert.Equal(quantity, item.Offers[index].Quantity);
                return item.Offers[index];
            }
        }

        throw new Xunit.Sdk.XunitException($"No offer {key}");
    }
}
