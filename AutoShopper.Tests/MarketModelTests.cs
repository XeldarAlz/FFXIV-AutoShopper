using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Market.Model;
using static AutoShopper.Tests.ExecutionFixtures;

namespace AutoShopper.Tests;

public sealed class MarketModelTests
{
    [Fact]
    public void CrossCheckMatchesTheSameListingsInAnyOrder()
    {
        ObservedListing[] captured = [Captured(1), Captured(2), Captured(3)];
        OfferedListing[] offered = [Offered(3), Offered(1), Offered(2)];

        Assert.Equal(CrossCheck.Match, ListingCrossCheck.Compare(captured, offered));
        Assert.Equal(CrossCheck.Match, ListingCrossCheck.Compare([], []));
    }

    [Fact]
    public void CrossCheckMatchesTheMannequinFlagOfThePage()
    {
        ObservedListing[] captured = [Captured(1), Captured(2) with { IsMannequin = true }];
        OfferedListing[] offered = [Offered(1), Offered(2) with { OnMannequin = true }];

        Assert.Equal(CrossCheck.Match, ListingCrossCheck.Compare(captured, offered));
    }

    [Fact]
    public void CrossCheckRefusesAnythingThatDiffers()
    {
        ObservedListing[] captured = [Captured(1), Captured(2)];

        Assert.Equal(CrossCheckResult.CountMismatch, ListingCrossCheck.Compare(captured, [Offered(1)]).Result);
        Assert.Equal(CrossCheckResult.MissingListing, ListingCrossCheck.Compare(captured, [Offered(1), Offered(9)]).Result);
        Assert.Equal(CrossCheckResult.DuplicateListing, ListingCrossCheck.Compare([Captured(1), Captured(1)], [Offered(1), Offered(1)]).Result);

        var price = ListingCrossCheck.Compare(captured, [Offered(2) with { UnitPrice = 999 }, Offered(1)]);
        Assert.Equal(new CrossCheck(CrossCheckResult.FieldMismatch, 1, 0, ListingField.UnitPrice), price);

        var mannequin = ListingCrossCheck.Compare(captured, [Offered(1), Offered(2) with { OnMannequin = true }]);
        Assert.Equal(new CrossCheck(CrossCheckResult.FieldMismatch, 1, 1, ListingField.Mannequin), mannequin);
    }

    [Fact]
    public void ListingFieldsReportTheFirstDifference()
    {
        var fields = new ListingFields(77, 501, Ore, 5, 1_000, 50, false);

        Assert.Equal(ListingField.None, fields.FirstDifference(fields));
        Assert.Equal(ListingField.ListingId, fields.FirstDifference(fields with { ListingId = 78, Quantity = 4 }));
        Assert.Equal(ListingField.Quantity, fields.FirstDifference(fields with { Quantity = 4 }));
        Assert.Equal(ListingField.TotalTax, fields.FirstDifference(fields with { TotalTax = 51 }));
        Assert.Equal(ListingField.Quality, fields.FirstDifference(fields with { IsHq = true }));
    }

    [Fact]
    public void ListingFieldsComeOnlyFromValuesTheGameCanHold()
    {
        Assert.True(ListingFields.TryFrom(Listing(), out var fields));
        Assert.Equal(new ListingFields(77, 501, Ore, 5, 1_000, 50, false), fields);
        Assert.False(ListingFields.TryFrom(Listing(quantity: 0), out _));
        Assert.False(ListingFields.TryFrom(Listing(unitPrice: (long)uint.MaxValue + 1), out _));
        Assert.False(ListingFields.TryFrom(Listing(totalTax: -1), out _));
    }

    [Fact]
    public void SnapshotMarksOwnAndMannequinListings()
    {
        var own = Listing() with { ListingId = 1, SellerContentId = Character };
        var mannequin = Listing() with { ListingId = 2, IsMannequin = true };
        var plain = Listing() with { ListingId = 4 };

        var snapshot = ListingSnapshot.Create(Ore, HomeWorld, SearchGeneration, Now, Character, 5, [own, mannequin, plain]);

        Assert.Equal([ListingMarks.Own, ListingMarks.Mannequin, ListingMarks.None], snapshot.Marks.AsSpan().ToArray());
        Assert.False(snapshot.IsComplete);
        Assert.True(snapshot.TryFind(4, out var found));
        Assert.Equal(plain, found);
        Assert.False(snapshot.TryFind(9, out _));
        Assert.Equal(ListingMarks.None, ListingSnapshot.MarksOf(Listing() with { SellerContentId = 0 }, 0));
    }

    [Fact]
    public void MarketMessagesAreClassified()
    {
        Assert.Equal(PurchaseRejection.ListingGone, PurchaseMessages.Classify(4_523));
        Assert.Equal(PurchaseRejection.InsufficientGil, PurchaseMessages.Classify(392));
        Assert.Equal(PurchaseRejection.OwnListing, PurchaseMessages.Classify(468));
        Assert.Equal(PurchaseRejection.Unclassified, PurchaseMessages.Classify(4_526));
        Assert.Equal(PurchaseRejection.None, PurchaseMessages.Classify(1));
        Assert.True(PurchaseMessages.IsMarketMessage(PurchaseMessages.SuccessCandidate));
        Assert.True(PurchaseMessages.IsMarketMessage(385));
        Assert.False(PurchaseMessages.IsPurchaseFailure(385));
        Assert.Equal(PurchaseRejection.Unclassified, PurchaseMessages.ClassifyResponse(385));
        Assert.Equal(PurchaseRejection.SetListing, PurchaseMessages.ClassifyResponse(4_525));
    }

    [Fact]
    public void HighQualityOffsetIsNormalized()
    {
        Assert.Equal(Ore, MarketItemId.Normalize(Ore + MarketItemId.HighQualityOffset));
        Assert.True(MarketItemId.Matches(Ore + MarketItemId.HighQualityOffset, Ore, true));
        Assert.True(MarketItemId.Matches(Ore, Ore, true));
        Assert.False(MarketItemId.Matches(Ore + MarketItemId.HighQualityOffset, Ore, false));
        Assert.False(MarketItemId.Matches(Cloth, Ore, false));
    }

    private static ObservedListing Captured(ulong listingId)
    {
        return Listing() with { ListingId = listingId };
    }

    private static OfferedListing Offered(ulong listingId)
    {
        return new OfferedListing(listingId, 501, Ore, false, 5, 1_000, 50, false);
    }
}
