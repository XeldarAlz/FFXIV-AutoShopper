using System.Collections.Immutable;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Market.Model;
using AutoShopper.Core.Planning;
using static AutoShopper.Tests.Fixtures;

namespace AutoShopper.Tests;

public sealed class StopSelectionTests
{
    private const uint Ore = 5_100;
    private const uint Cloth = 5_200;
    private const int Generation = 7;

    private static readonly PlanDemand OreAny = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 10);
    private static readonly PlanDemand OreHigh = PlanDemand.ForPurchase(Ore, DemandQuality.HighQuality, 3);

    [Fact]
    public void ChoosesWholeListingsThatCoverTheOpenDemandCheapestFirst()
    {
        var listings = Listings(Listing(1, 6, 100), Listing(2, 4, 110), Listing(3, 10, 500));

        var choice = Choose([OreAny], [], [], listings);

        Assert.Empty(choice.Unresolved);
        Assert.Equal(10, Quantity(choice));
        Assert.Contains(choice.Choices, item => item.Listing.ListingId == 1);
        Assert.Contains(choice.Choices, item => item.Listing.ListingId == 2);
        Assert.DoesNotContain(choice.Choices, item => item.Listing.ListingId == 3);
        Assert.All(choice.Choices, item => Assert.Equal(OreAny.Key, item.Demand));
    }

    [Fact]
    public void PayableIsTheLiveListingPriceWithItsOwnFee()
    {
        var listings = Listings(Listing(1, 10, 100, tax: 37));

        var choice = Choose([OreAny], [], [], listings);

        var chosen = Assert.Single(choice.Choices);
        Assert.Equal(1_037, chosen.Payable);
    }

    [Fact]
    public void AcquisitionsShrinkTheDemandAndTheirListingsAreNotChosenAgain()
    {
        var listings = Listings(Listing(1, 6, 100), Listing(2, 4, 110), Listing(3, 4, 120));
        ImmutableArray<ConfirmedPurchase> acquired = [new(OreAny.Key, ListingKeys.ForGameListing(1), 6, 630)];

        var choice = Choose([OreAny], acquired, [], listings);

        Assert.Equal(4, Quantity(choice));
        Assert.DoesNotContain(choice.Choices, item => item.Listing.ListingId == 1);
    }

    [Fact]
    public void ExcludedListingsStayOutForTheRun()
    {
        var listings = Listings(Listing(1, 10, 100), Listing(2, 10, 150));

        var choice = Choose([OreAny], [], [ListingKeys.ForGameListing(1)], listings);

        var chosen = Assert.Single(choice.Choices);
        Assert.Equal(2UL, chosen.Listing.ListingId);
    }

    [Fact]
    public void MarkedListingsAreNeverOffered()
    {
        var listings = Listings(Listing(1, 10, 1), Listing(2, 10, 2), Listing(3, 10, 3), Listing(4, 10, 100));
        ImmutableArray<ListingMarks> marks = [ListingMarks.Own, ListingMarks.Mannequin, ListingMarks.Set, ListingMarks.None];

        var choice = StopSelection.Choose(NewPlanner(), RunPlan([OreAny]), [OreAny], [], [], Alpha, listings, marks);

        var chosen = Assert.Single(choice.Choices);
        Assert.Equal(4UL, chosen.Listing.ListingId);
    }

    [Fact]
    public void QualityDemandsTakeOnlyTheirQuality()
    {
        var listings = Listings(Listing(1, 3, 100, highQuality: true), Listing(2, 10, 50));

        var choice = Choose([OreAny, OreHigh], [], [], listings);

        Assert.Contains(choice.Choices, item => item.Demand == OreHigh.Key && item.Listing.ListingId == 1);
        Assert.Contains(choice.Choices, item => item.Demand == OreAny.Key && item.Listing.ListingId == 2);
    }

    [Fact]
    public void HighQualityOffsetOnTheItemIdIsUnderstood()
    {
        var listings = Listings(Listing(1, 3, 100, highQuality: true) with { ItemId = Ore + MarketItemId.HighQualityOffset });

        var choice = Choose([OreHigh], [], [], listings);

        var chosen = Assert.Single(choice.Choices);
        Assert.Equal(Ore + MarketItemId.HighQualityOffset, chosen.Listing.ItemId);
        Assert.Equal(Ore, StopSelection.Normalized(chosen.Listing).ItemId);
    }

    [Fact]
    public void ListingsAboveTheUnitCapLeaveTheDemandUnresolved()
    {
        var capped = PlanDemand.ForPurchase(Ore, DemandQuality.Any, 5, 0, 50);
        var listings = Listings(Listing(1, 5, 100));

        var choice = Choose([capped], [], [], listings);

        Assert.Empty(choice.Choices);
        var unresolved = Assert.Single(choice.Unresolved);
        Assert.Equal(UnresolvedReason.AboveUnitCap, unresolved.Reason);
        Assert.Equal(RunIssueReason.PriceCapExceeded, RunIssue.ForUnresolved(unresolved.Reason));
    }

    [Fact]
    public void NotEnoughStockIsReportedWithWhatIsMissing()
    {
        var listings = Listings(Listing(1, 4, 100));

        var choice = Choose([OreAny], [], [], listings);

        Assert.Equal(4, Quantity(choice));
        var unresolved = Assert.Single(choice.Unresolved);
        Assert.Equal(6, unresolved.MissingQuantity);
        Assert.Equal(RunIssueReason.NoStock, RunIssue.ForUnresolved(unresolved.Reason));
    }

    [Fact]
    public void ListingsOfOtherWorldsOrItemsAreIgnored()
    {
        var listings = Listings(Listing(1, 10, 100) with { WorldId = Beta.WorldId }, Listing(2, 10, 100) with { ItemId = Cloth }, Listing(3, 10, 200));

        var choice = Choose([OreAny], [], [], listings);

        var chosen = Assert.Single(choice.Choices);
        Assert.Equal(3UL, chosen.Listing.ListingId);
    }

    [Fact]
    public void NoListingsLeaveTheWholeOpenDemandUnresolved()
    {
        ImmutableArray<ConfirmedPurchase> acquired = [new(OreAny.Key, "game:9", 4, 400)];

        var choice = Choose([OreAny], acquired, [], []);

        Assert.Empty(choice.Choices);
        Assert.Equal(6, Assert.Single(choice.Unresolved).MissingQuantity);
    }

    [Fact]
    public void AuthorizedDemandsMergeDuplicatesAndDropSatisfiedLines()
    {
        ImmutableArray<PlanDemand> demands = [OreAny, PlanDemand.ForPurchase(Ore, DemandQuality.Any, 5), PlanDemand.ForPurchase(Cloth, DemandQuality.Any, 0)];

        var authorized = RunPlanning.AuthorizedDemands(demands);

        var merged = Assert.Single(authorized);
        Assert.Equal(15, merged.RequiredQuantity);
    }

    [Fact]
    public void RemainingRequestPlansOnlyWhatIsLeftOnAllowedWorlds()
    {
        var authorization = Authorization([OreAny, OreHigh], [Alpha.WorldId, Beta.WorldId]);
        var tally = new RunTally(authorization.Demands);
        tally.RecordConfirmed(OreHigh.Key, "game:1", 3, 300);
        tally.RecordConfirmed(OreAny.Key, "game:2", 4, 400);

        var request = RunPlanning.RemainingRequest(authorization, tally, [], AllWorlds, Settings(), ["game:5"]);

        var remaining = Assert.Single(request.Demands);
        Assert.Equal(OreAny.Key, remaining.Key);
        Assert.Equal(6, remaining.RequiredQuantity);
        Assert.Equal(2, request.Worlds.Length);
        Assert.Equal(Alpha.WorldId, request.Worlds[0].WorldId);
        Assert.Equal(Beta.WorldId, request.Worlds[1].WorldId);
        Assert.Equal("game:5", Assert.Single(request.ExcludedListingKeys));
    }

    [Fact]
    public void DemandsOfAnItemAreTheLinesOneSearchServes()
    {
        var authorization = Authorization([OreAny, OreHigh, PlanDemand.ForPurchase(Cloth, DemandQuality.Any, 2)], [Alpha.WorldId]);

        var oreDemands = RunPlanning.DemandsOfItem(authorization, Ore);

        Assert.Equal(2, oreDemands.Length);
        Assert.All(oreDemands, demand => Assert.Equal(Ore, demand.ItemId));
    }

    private static StopChoice Choose(ImmutableArray<PlanDemand> demands, ImmutableArray<ConfirmedPurchase> acquired, ImmutableArray<string> excluded, ImmutableArray<ObservedListing> listings)
        => StopSelection.Choose(NewPlanner(), RunPlan(demands), demands, acquired, excluded, Alpha, listings, default);

    private static PurchasePlan RunPlan(ImmutableArray<PlanDemand> demands)
        => NewPlanner().Plan(Request(Settings(), demands));

    private static ImmutableArray<ObservedListing> Listings(params ObservedListing[] listings) => [.. listings];

    private static ObservedListing Listing(ulong listingId, int quantity, long unitPrice, long? tax = null, bool highQuality = false)
        => new(listingId, 400 + listingId, 0, Ore, highQuality, quantity, unitPrice, tax ?? unitPrice * quantity * 5 / 100, false, false, Alpha.WorldId, Generation, ObservedAt);

    private static int Quantity(StopChoice choice)
    {
        var total = 0;
        for (var index = 0; index < choice.Choices.Length; index++)
        {
            total += choice.Choices[index].Listing.Quantity;
        }

        return total;
    }

    private static RunAuthorization Authorization(ImmutableArray<PlanDemand> demands, ImmutableArray<uint> worlds)
    {
        var problem = RunAuthorization.TryCreate(Guid.NewGuid(), 1_001, Alpha.WorldId, demands, worlds, 100_000, 0, 1, ObservedAt, out var authorization);
        Assert.Equal(AuthorizationProblem.None, problem);
        return authorization!;
    }
}
