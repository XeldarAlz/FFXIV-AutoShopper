using System.Collections.Immutable;
using AutoShopper.Core.Api.Model;
using AutoShopper.Core.Planning;
using static AutoShopper.Tests.Fixtures;

namespace AutoShopper.Tests;

public sealed class QuoteCoverTests
{
    private const uint Ore = 5_057;
    private const uint Cloth = 5_058;

    private static readonly DateTime FetchedAt = ObservedAt.AddHours(4.5);

    [Fact]
    public void AnExactFitCostsItsListingsWithTheFee()
    {
        var result = Single([new QuoteRequestItem(Ore, 3)], Offer(Alpha, Ore, 3, 600));

        Assert.Equal(new QuoteCover(true, 3, 1_890, 630, 4.5, 1), result.Normal);
        Assert.Equal(Ore, result.ItemId);
        Assert.Equal(3, result.Quantity);
    }

    [Fact]
    public void OnlyBiggerStacksBuyMoreUnitsThanAsked()
    {
        var result = Single([new QuoteRequestItem(Ore, 3)], Offer(Alpha, Ore, 10, 100));

        Assert.True(result.Normal.Available);
        Assert.Equal(10, result.Normal.Units);
        Assert.Equal(1_050, result.Normal.Total);
        Assert.Equal(105, result.Normal.UnitPrice);
    }

    [Fact]
    public void TheCheapestCombinationOfWholeListingsWins()
    {
        var result = Single(
            [new QuoteRequestItem(Ore, 10)],
            Offer(Alpha, Ore, 6, 10), Offer(Beta, Ore, 4, 11), Offer(Alpha, Ore, 10, 12), Offer(Gamma, Ore, 20, 6));

        Assert.Equal(10, result.Normal.Units);
        Assert.Equal(63 + 46, result.Normal.Total);
        Assert.Equal(2, result.Normal.Worlds);
    }

    [Fact]
    public void ABigStackCanBeCheaperThanAnExactFit()
    {
        var result = Single(
            [new QuoteRequestItem(Ore, 5)],
            Offer(Alpha, Ore, 5, 30), Offer(Beta, Ore, 12, 10));

        Assert.Equal(12, result.Normal.Units);
        Assert.Equal(126, result.Normal.Total);
        Assert.Equal(11, result.Normal.UnitPrice);
    }

    [Fact]
    public void NoCoverReadsAsUnavailableWithZeros()
    {
        var result = Single([new QuoteRequestItem(Ore, 3)], Offer(Alpha, Ore, 2, 100), Offer(Alpha, Cloth, 50, 1));

        Assert.Equal(default, result.Normal);
        Assert.False(result.Normal.Available);
        Assert.Equal(default, result.High);
    }

    [Fact]
    public void NormalAndHighQualityArePricedApart()
    {
        var result = Single(
            [new QuoteRequestItem(Ore, 2)],
            Offer(Alpha, Ore, 2, 100), Offer(Alpha, Ore, 2, 300, isHq: true), Offer(Beta, Ore, 2, 50, isHq: true));

        Assert.Equal(210, result.Normal.Total);
        Assert.Equal(105, result.High.Total);
        Assert.Equal(1, result.High.Worlds);
    }

    [Fact]
    public void HighQualityListingsNeverCoverTheNormalQualityAsk()
    {
        var result = Single([new QuoteRequestItem(Ore, 2)], Offer(Alpha, Ore, 5, 10, isHq: true));

        Assert.False(result.Normal.Available);
        Assert.True(result.High.Available);
        Assert.Equal(5, result.High.Units);
    }

    [Fact]
    public void EachItemGetsItsOwnCoverInRequestOrder()
    {
        var results = QuoteCovers.Compute(
            [new QuoteRequestItem(Cloth, 1), new QuoteRequestItem(Ore, 1)],
            [Offer(Alpha, Ore, 1, 100), Offer(Beta, Cloth, 1, 40)],
            FetchedAt);

        Assert.Equal(Cloth, results[0].ItemId);
        Assert.Equal(42, results[0].Normal.Total);
        Assert.Equal(Ore, results[1].ItemId);
        Assert.Equal(105, results[1].Normal.Total);
    }

    [Fact]
    public void TheUnitPriceRoundsUp()
    {
        var result = Single([new QuoteRequestItem(Ore, 3)], Offer(Alpha, Ore, 3, 7));

        Assert.Equal(22, result.Normal.Total);
        Assert.Equal(8, result.Normal.UnitPrice);
    }

    [Fact]
    public void AListingOfUnknownAgeDoesNotCountAsTheOldest()
    {
        var unknownAge = Offer(Beta, Ore, 2, 100) with { ObservedAtUtc = default };

        var result = Single([new QuoteRequestItem(Ore, 4)], Offer(Alpha, Ore, 2, 100), unknownAge);

        Assert.Equal(4, result.Normal.Units);
        Assert.Equal(4.5, result.Normal.OldestListingHours);
    }

    private static QuoteItemResult Single(QuoteRequestItem[] items, params PlanOffer[] offers)
        => Assert.Single(QuoteCovers.Compute(items, [.. offers], FetchedAt));
}
