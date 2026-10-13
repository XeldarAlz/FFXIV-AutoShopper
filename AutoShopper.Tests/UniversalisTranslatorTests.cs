using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Pricing.Model;
using AutoShopper.Core.Pricing.Universalis;
using static AutoShopper.Tests.PricingFixtures;

namespace AutoShopper.Tests;

public sealed class UniversalisTranslatorTests
{
    [Fact]
    public void DataCenterAnswerBecomesOffersPerItemAndWorld()
    {
        var chunk = Chunk(PriceQueryKind.DataCenter, "Aether", AetherWorlds, HighQualityItem, FireShard, Gil, OneGilItem);

        var result = UniversalisTranslator.Translate(Read("dc-multi-trimmed.json"), chunk, FetchedAt);

        Assert.False(result.Failed);
        Assert.Equal(4, result.Items.Length);
        Assert.Equal(6, Item(result, HighQualityItem).Offers.Length);
        Assert.Equal(6, Item(result, FireShard).Offers.Length);
        Assert.Equal(6, Item(result, OneGilItem).Offers.Length);
        Assert.Empty(Item(result, Gil).Offers);
        Assert.Equal(PriceCoverage.NotMarketable, Item(result, Gil).Coverage);
    }

    [Fact]
    public void OfferKeepsTheListingIdentityAndAnEstimatedFee()
    {
        var chunk = Chunk(PriceQueryKind.DataCenter, "Aether", AetherWorlds, HighQualityItem, FireShard);

        var result = UniversalisTranslator.Translate(Read("dc-multi-trimmed.json"), chunk, FetchedAt);
        var offer = Item(result, HighQualityItem).Offers[0];

        Assert.Equal(Faerie.WorldId, offer.WorldId);
        Assert.Equal(AetherId, offer.DataCenterId);
        Assert.True(offer.IsHq);
        Assert.Equal(34, offer.Quantity);
        Assert.Equal(59, offer.UnitPrice);
        Assert.Equal(100, offer.Fee);
        Assert.True(offer.IsEstimate);
        Assert.Equal(ListingKeys.ForGameListing(3799913032187154), offer.ListingKey);
        Assert.Equal(FromUnixMilliseconds(1791759169759), offer.ObservedAtUtc);
    }

    [Fact]
    public void FeeAndBaseMatchWhatUniversalisComputes()
    {
        var body = Read("dc-multi-full.json");
        var chunk = Chunk(PriceQueryKind.DataCenter, "Aether", AetherWorlds, HighQualityItem, FireShard);

        var result = UniversalisTranslator.Translate(body, chunk, FetchedAt);

        using var document = JsonDocument.Parse(body);
        var checkedListings = 0;
        foreach (var item in document.RootElement.GetProperty("items").EnumerateObject())
        {
            var offers = Item(result, uint.Parse(item.Name)).Offers;
            foreach (var listing in item.Value.GetProperty("listings").EnumerateArray())
            {
                var key = ListingKeys.ForGameListing(ulong.Parse(listing.GetProperty("listingID").GetString()!));
                var offer = Find(offers, key);
                Assert.Equal(listing.GetProperty("total").GetInt64(), offer.UnitPrice * offer.Quantity);
                Assert.Equal(listing.GetProperty("tax").GetInt64(), offer.Fee);
                Assert.True(offer.TryGetPayable(out var payable));
                Assert.Equal(offer.UnitPrice * offer.Quantity + offer.Fee, payable);
                checkedListings++;
            }
        }

        Assert.Equal(6, checkedListings);
    }

    [Fact]
    public void FullAnswerWithExtraFieldsReadsLikeTheTrimmedOne()
    {
        var chunk = Chunk(PriceQueryKind.DataCenter, "Aether", AetherWorlds, HighQualityItem, FireShard);

        var full = UniversalisTranslator.Translate(Read("dc-multi-full.json"), chunk, FetchedAt);
        var trimmed = UniversalisTranslator.Translate(Read("dc-multi-trimmed.json"), chunk, FetchedAt);

        var fullOffers = Item(full, FireShard).Offers;
        var trimmedOffers = Item(trimmed, FireShard).Offers;
        for (var index = 0; index < fullOffers.Length; index++)
        {
            Assert.Equal(trimmedOffers[index], fullOffers[index]);
        }
    }

    [Fact]
    public void ListingsOutsideTheAllowedWorldsAreDropped()
    {
        var chunk = Chunk(PriceQueryKind.DataCenter, "Aether", [Jenova, Faerie], HighQualityItem, FireShard);

        var result = UniversalisTranslator.Translate(Read("dc-multi-trimmed.json"), chunk, FetchedAt);

        Assert.Equal(3, Item(result, HighQualityItem).Offers.Length);
        var fireShards = Item(result, FireShard).Offers;
        Assert.Equal(3, fireShards.Length);
        foreach (var offer in fireShards)
        {
            Assert.True(offer.WorldId == Jenova.WorldId || offer.WorldId == Faerie.WorldId);
        }

        Assert.Equal(2, Item(result, FireShard).Worlds.Length);
    }

    [Fact]
    public void RegionAnswerMarksWorldsWithoutUploadAndWorldsItDoesNotMention()
    {
        var chunk = Chunk(PriceQueryKind.Region, "North-America", [Exodus, Rafflesia, Phantom], HighQualityItem, OneGilItem);

        var result = UniversalisTranslator.Translate(Read("region-multi-trimmed.json"), chunk, FetchedAt);
        var item = Item(result, HighQualityItem);

        Assert.Equal(2, item.Offers.Length);
        Assert.Equal(Exodus.WorldId, item.Offers[0].WorldId);
        Assert.Equal(PrimalId, item.Offers[0].DataCenterId);
        Assert.Equal(PriceCoverage.NoData, World(item, Rafflesia.WorldId).Coverage);
        Assert.False(World(item, Rafflesia.WorldId).HasUpload);
        Assert.Equal(PriceCoverage.MissingWorld, World(item, Phantom.WorldId).Coverage);
        Assert.Equal(2, World(item, Exodus.WorldId).ListingCount);
        Assert.True((item.Coverage & PriceCoverage.NoData) != 0);
        Assert.True((item.Coverage & PriceCoverage.MissingWorld) != 0);
    }

    [Fact]
    public void WorldAnswerWithoutListingWorldsUsesTheQueriedWorld()
    {
        var chunk = Chunk(PriceQueryKind.World, "73", [Adamantoise], HighQualityItem);

        var result = UniversalisTranslator.Translate(Read("world-single-trimmed.json"), chunk, FetchedAt);
        var item = Item(result, HighQualityItem);

        Assert.Equal(4, item.Offers.Length);
        foreach (var offer in item.Offers)
        {
            Assert.Equal(Adamantoise.WorldId, offer.WorldId);
            Assert.Equal(AetherId, offer.DataCenterId);
        }

        var world = World(item, Adamantoise.WorldId);
        Assert.Equal(FromUnixMilliseconds(1791777279919), world.UploadedAtUtc);
        Assert.Equal(PriceCoverage.None, item.Coverage);
    }

    [Fact]
    public void SingleItemShapeOnADataCenterIsRead()
    {
        var chunk = Chunk(PriceQueryKind.DataCenter, "Aether", AetherWorlds, OneGilItem);

        var result = UniversalisTranslator.Translate(Read("dc-single-trimmed.json"), chunk, FetchedAt);
        var item = Item(result, OneGilItem);

        Assert.Equal(3, item.Offers.Length);
        Assert.Equal(AetherWorlds.Length, item.Worlds.Length);
        Assert.Equal(PriceCoverage.None, item.Coverage);
    }

    [Fact]
    public void MannequinsAndMalformedListingsAreRefused()
    {
        const string body = """
            {"itemIDs":[10,11],"items":{"10":{"itemID":10,"lastUploadTime":1791790303256,
            "worldUploadTimes":{"54":1791790303256},"hasData":true,"listings":[
            {"listingID":"1","pricePerUnit":100,"quantity":2,"hq":false,"worldID":54,"onMannequin":true},
            {"listingID":"2","pricePerUnit":100,"quantity":0,"hq":false,"worldID":54,"onMannequin":false},
            {"listingID":"3","pricePerUnit":-5,"quantity":1,"hq":false,"worldID":54,"onMannequin":false},
            {"listingID":"","pricePerUnit":100,"quantity":1,"hq":false,"worldID":54,"onMannequin":false},
            {"listingID":"abc","pricePerUnit":100,"quantity":3,"hq":true,"worldID":54,"onMannequin":false}]}},
            "unresolvedItems":[]}
            """;
        var chunk = Chunk(PriceQueryKind.DataCenter, "Aether", [Faerie], 10, 11);

        var result = UniversalisTranslator.Translate(Encoding.UTF8.GetBytes(body), chunk, FetchedAt);
        var item = Item(result, 10);

        var offer = Assert.Single(item.Offers);
        Assert.Equal("universalis:abc", offer.ListingKey);
        Assert.Equal(15, offer.Fee);
        Assert.True((item.Coverage & PriceCoverage.InvalidListings) != 0);
        Assert.Equal(PriceCoverage.NotReturned, Item(result, 11).Coverage);
    }

    [Fact]
    public void ItemWithoutDataHasNoUploadAnywhere()
    {
        const string body = """{"itemIDs":[10,11],"items":{"10":{"itemID":10,"lastUploadTime":0,"hasData":false,"listings":[]},"11":{"itemID":11,"hasData":true,"listings":[]}},"unresolvedItems":[]}""";
        var chunk = Chunk(PriceQueryKind.DataCenter, "Aether", [Faerie, Jenova], 10, 11);

        var result = UniversalisTranslator.Translate(Encoding.UTF8.GetBytes(body), chunk, FetchedAt);

        Assert.Equal(PriceCoverage.NoData, Item(result, 10).Coverage);
        Assert.False(Item(result, 10).HasAnyUpload);
        Assert.Equal(PriceCoverage.MissingWorld, Item(result, 11).Coverage);
    }

    [Fact]
    public void AHundredListingsOnOneWorldMayBeCut()
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
        var chunk = Chunk(PriceQueryKind.World, "54", [Faerie], 10);

        var result = UniversalisTranslator.Translate(Encoding.UTF8.GetBytes(builder.ToString()), chunk, FetchedAt);

        Assert.Equal(PriceCoverage.Truncated, Item(result, 10).Coverage);
        Assert.Equal(UniversalisTranslator.TruncationThreshold, Item(result, 10).Offers.Length);
    }

    [Theory]
    [InlineData("{\"itemIDs\":")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"items\":{\"10\":{\"listings\":[{\"pricePerUnit\":\"cheap\"}]}}}")]
    public void MalformedAnswerFailsTheWholeRequest(string body)
    {
        var chunk = Chunk(PriceQueryKind.DataCenter, "Aether", [Faerie], 10, 11);

        var result = UniversalisTranslator.Translate(Encoding.UTF8.GetBytes(body), chunk, FetchedAt);

        Assert.Equal(PriceFailure.Malformed, result.Failure);
        Assert.Equal(PriceCoverage.RequestFailed, Item(result, 10).Coverage);
        Assert.Empty(Item(result, 10).Offers);
    }

    internal static ItemPrices Item(ChunkPrices result, uint itemId)
    {
        for (var index = 0; index < result.Items.Length; index++)
        {
            if (result.Items[index].ItemId == itemId)
            {
                return result.Items[index];
            }
        }

        throw new Xunit.Sdk.XunitException($"No prices for item {itemId}");
    }

    private static WorldPrice World(ItemPrices item, uint worldId)
    {
        Assert.True(PriceSnapshotBuilder.TryFindWorld(item.Worlds, worldId, out var price));
        return price;
    }

    private static PlanOffer Find(ImmutableArray<PlanOffer> offers, string key)
    {
        for (var index = 0; index < offers.Length; index++)
        {
            if (offers[index].ListingKey == key)
            {
                return offers[index];
            }
        }

        throw new Xunit.Sdk.XunitException($"No offer {key}");
    }
}
