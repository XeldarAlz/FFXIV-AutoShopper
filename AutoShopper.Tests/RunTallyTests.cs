using System.Collections.Immutable;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Market.Model;
using AutoShopper.Core.Planning;
using static AutoShopper.Tests.ExecutionFixtures;

namespace AutoShopper.Tests;

public sealed class RunTallyTests
{
    private static RunTally NewTally() => new([OreDemand, ClothDemand]);

    [Fact]
    public void ConfirmedPurchaseReducesWhatIsLeftAndCountsAsSpending()
    {
        var tally = NewTally();

        Assert.True(tally.RecordConfirmed(OreDemand.Key, "game:1", 6, 6_300));

        Assert.Equal(6, tally.Acquired(OreDemand.Key));
        Assert.Equal(4, tally.Outstanding(OreDemand.Key).RequiredQuantity);
        Assert.Equal(6_300, tally.ConfirmedSpend);
        Assert.Equal(6, tally.ConfirmedUnits);
        Assert.False(tally.IsSatisfied(OreDemand.Key));
        Assert.Equal(1, tally.Purchases);
    }

    [Fact]
    public void PurchasesCoveringEveryLineSatisfyTheTally()
    {
        var tally = NewTally();

        Assert.True(tally.RecordConfirmed(OreDemand.Key, "game:1", 10, 10_500));
        Assert.True(tally.RecordConfirmed(ClothDemand.Key, "game:2", 4, 400));

        Assert.True(tally.AllSatisfied);
        Assert.Equal(10_900, tally.ConfirmedSpend);
        Assert.Equal(14, tally.ConfirmedUnits);
        var line = tally.LineAt(0);
        Assert.Equal(OreDemand.Key, line.Demand.Key);
        Assert.Equal(10, line.Acquired);
        Assert.True(line.Satisfied);
    }

    [Fact]
    public void ListingIsCountedOnceHoweverOftenItArrives()
    {
        var tally = NewTally();

        Assert.True(tally.RecordConfirmed(OreDemand.Key, "game:1", 5, 5_050));
        Assert.False(tally.RecordConfirmed(OreDemand.Key, "game:1", 5, 5_050));

        Assert.Equal(5, tally.Acquired(OreDemand.Key));
        Assert.Equal(5_050, tally.ConfirmedSpend);
        Assert.Single(tally.Acquisitions());
    }

    [Fact]
    public void DemandOutsideTheAuthorizationIsRefused()
    {
        var tally = NewTally();

        Assert.False(tally.RecordConfirmed(new DemandKey(Ore, DemandQuality.HighQuality), "game:1", 5, 5_050));
        Assert.False(tally.RecordConfirmed(OreDemand.Key, "game:2", 0, 0));
        Assert.Equal(0, tally.Purchases);
        Assert.True(tally.IsSatisfied(new DemandKey(Ore, DemandQuality.HighQuality)));
    }

    [Fact]
    public void RestoreKeepsConfirmedProgressFromTheJournal()
    {
        ImmutableArray<ConfirmedPurchase> purchases = [new(OreDemand.Key, "game:1", 5, 5_050), new(ClothDemand.Key, "game:2", 4, 400)];

        var tally = RunTally.Restore([OreDemand, ClothDemand], purchases);

        Assert.Equal(5, tally.Acquired(OreDemand.Key));
        Assert.True(tally.IsSatisfied(ClothDemand.Key));
        Assert.Equal(5_450, tally.ConfirmedSpend);
        Assert.Equal(5, tally.LineAt(0).Missing);
    }

    [Fact]
    public void AcquisitionsFeedTheReplanWithTheirListingKeys()
    {
        var tally = NewTally();
        tally.RecordConfirmed(OreDemand.Key, "game:1", 5, 5_050);
        tally.RecordConfirmed(OreDemand.Key, "game:2", 2, 2_100);

        var acquisitions = tally.Acquisitions();

        Assert.Equal(2, acquisitions.Length);
        Assert.Equal("game:1", acquisitions[0].ListingKey);
        Assert.Equal("game:2", acquisitions[1].ListingKey);
    }

    [Fact]
    public void RevisionRisesWithEveryCountedPurchase()
    {
        var tally = NewTally();
        var before = tally.Revision;

        tally.RecordConfirmed(OreDemand.Key, "game:1", 5, 5_050);
        tally.RecordConfirmed(OreDemand.Key, "game:1", 5, 5_050);

        Assert.Equal(before + 1, tally.Revision);
    }
}

public sealed class RunReactionTests
{
    private static readonly (GuardRejection Rejection, RunReaction Reaction)[] GuardReactions =
    [
        (GuardRejection.InventoryFull, RunReaction.StopBuying),
        (GuardRejection.StaleObservation, RunReaction.Research),
        (GuardRejection.ListingChanged, RunReaction.Research),
        (GuardRejection.UnitCapExceeded, RunReaction.SkipListing),
        (GuardRejection.BudgetExceeded, RunReaction.SkipListing),
        (GuardRejection.ReserveBreached, RunReaction.SkipListing),
        (GuardRejection.OwnRetainer, RunReaction.SkipListing),
        (GuardRejection.UniqueAlreadyOwned, RunReaction.SkipItem),
        (GuardRejection.WrongCharacter, RunReaction.EndRun),
        (GuardRejection.ExecutionUnavailable, RunReaction.EndRun),
        (GuardRejection.PurchasePending, RunReaction.NeedsReconciliation),
        (GuardRejection.CrystalLimitExceeded, RunReaction.SkipListing),
    ];

    private static readonly (GuardRejection Rejection, RunIssueReason Issue)[] GuardIssues =
    [
        (GuardRejection.UnitCapExceeded, RunIssueReason.PriceCapExceeded),
        (GuardRejection.BudgetExceeded, RunIssueReason.BudgetReached),
        (GuardRejection.ReserveBreached, RunIssueReason.InsufficientGil),
        (GuardRejection.InventoryFull, RunIssueReason.InventoryFull),
        (GuardRejection.ExecutionUnavailable, RunIssueReason.UnsupportedGameVersion),
        (GuardRejection.CrystalLimitExceeded, RunIssueReason.CrystalLimit),
    ];

    private static readonly (PurchaseRejection Rejection, RunReaction Reaction, bool HasIssue)[] ServerReactions =
    [
        (PurchaseRejection.ListingGone, RunReaction.ExcludeAndResearch, false),
        (PurchaseRejection.Throttled, RunReaction.BackOffAndResearch, false),
        (PurchaseRejection.InventoryFull, RunReaction.StopBuying, true),
        (PurchaseRejection.InsufficientGil, RunReaction.StopBuying, true),
        (PurchaseRejection.UniqueItem, RunReaction.SkipItem, true),
        (PurchaseRejection.Unclassified, RunReaction.ExcludeAndResearch, true),
        (PurchaseRejection.OwnListing, RunReaction.ExcludeAndResearch, true),
    ];

    private static readonly (PurchaseRefusal Refusal, RunReaction Reaction)[] SendReactions =
    [
        (PurchaseRefusal.ListingGone, RunReaction.ExcludeAndResearch),
        (PurchaseRefusal.StaleGeneration, RunReaction.Research),
        (PurchaseRefusal.ListingChanged, RunReaction.Research),
        (PurchaseRefusal.ListingKindRefused, RunReaction.SkipListing),
        (PurchaseRefusal.ExecutionUnavailable, RunReaction.EndRun),
        (PurchaseRefusal.AttemptPending, RunReaction.NeedsReconciliation),
    ];

    [Fact]
    public void GuardRefusalsMapToTheirReaction()
    {
        for (var index = 0; index < GuardReactions.Length; index++)
        {
            var (rejection, reaction) = GuardReactions[index];
            Assert.Equal(reaction, RunReactions.ForGuard(rejection).Reaction);
        }
    }

    [Fact]
    public void GuardRefusalsThatStopALineCarryTheirReason()
    {
        for (var index = 0; index < GuardIssues.Length; index++)
        {
            var (rejection, issue) = GuardIssues[index];
            var response = RunReactions.ForGuard(rejection);
            Assert.True(response.HasIssue);
            Assert.Equal(issue, response.Issue);
        }
    }

    [Fact]
    public void ServerRefusalsMapToTheirReaction()
    {
        for (var index = 0; index < ServerReactions.Length; index++)
        {
            var (rejection, reaction, hasIssue) = ServerReactions[index];
            var response = RunReactions.ForRejection(rejection);
            Assert.Equal(reaction, response.Reaction);
            Assert.Equal(hasIssue, response.HasIssue);
        }
    }

    [Fact]
    public void AFullCrystalPouchSkipsTheListingWithoutStoppingTheRun()
    {
        var response = RunReactions.ForGuard(GuardRejection.CrystalLimitExceeded);

        Assert.Equal(RunReaction.SkipListing, response.Reaction);
        Assert.NotEqual(RunReaction.StopBuying, response.Reaction);
    }

    [Fact]
    public void AStaleLiveReadIsSearchedAgainWithoutCountingAsARefusal()
    {
        Assert.Equal(RunReaction.Continue, RunReactions.ForLiveRead(LiveListingRead.Found).Reaction);
        Assert.Equal(RunReaction.Research, RunReactions.ForLiveRead(LiveListingRead.Stale).Reaction);
        Assert.Equal(RunReaction.ExcludeAndResearch, RunReactions.ForLiveRead(LiveListingRead.Gone).Reaction);
        Assert.False(RunReactions.ForLiveRead(LiveListingRead.Stale).HasIssue);
    }

    [Fact]
    public void SendRefusalsMapToTheirReaction()
    {
        for (var index = 0; index < SendReactions.Length; index++)
        {
            var (refusal, reaction) = SendReactions[index];
            Assert.Equal(reaction, RunReactions.ForRefusal(refusal).Reaction);
        }
    }

    [Fact]
    public void EveryRefusalHasAReaction()
    {
        var guardRejections = Enum.GetValues<GuardRejection>();
        for (var index = 0; index < guardRejections.Length; index++)
        {
            Assert.True(Enum.IsDefined(RunReactions.ForGuard(guardRejections[index]).Reaction));
        }

        var refusals = Enum.GetValues<PurchaseRefusal>();
        for (var index = 0; index < refusals.Length; index++)
        {
            Assert.True(Enum.IsDefined(RunReactions.ForRefusal(refusals[index]).Reaction));
        }

        var rejections = Enum.GetValues<PurchaseRejection>();
        for (var index = 0; index < rejections.Length; index++)
        {
            var reaction = RunReactions.ForRejection(rejections[index]).Reaction;
            Assert.True(Enum.IsDefined(reaction));
            Assert.NotEqual(RunReaction.Continue, reaction);
        }
    }

    [Fact]
    public void SearchesWithoutProgressAreBounded()
    {
        var budget = new ItemRetryBudget();
        var searches = 0;
        while (budget.TryBeginSearch())
        {
            searches++;
            Assert.True(searches <= 10);
        }

        Assert.Equal(ItemRetryBudget.MaximumSearches, searches);
    }

    [Fact]
    public void ProgressStartsTheSearchCountAgain()
    {
        var budget = new ItemRetryBudget();
        for (var round = 0; round < 10; round++)
        {
            Assert.True(budget.TryBeginSearch());
            budget.RecordProgress();
        }

        Assert.False(budget.Exhausted);
    }

    [Fact]
    public void RejectionsAreBoundedEvenWithProgress()
    {
        var budget = new ItemRetryBudget();
        Assert.True(budget.TryBeginSearch());
        Assert.True(budget.RecordRejection());
        budget.RecordProgress();
        Assert.True(budget.RecordRejection());

        Assert.False(budget.RecordRejection());
        Assert.True(budget.Exhausted);
        Assert.False(budget.TryBeginSearch());
    }
}
