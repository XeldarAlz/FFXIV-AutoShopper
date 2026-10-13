using System.Collections.Immutable;
using System.Threading;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Preview.Model;
using AutoShopper.Core.Pricing.Model;
using AutoShopper.Core.Pricing.Universalis;
using static AutoShopper.Tests.PricingFixtures;

namespace AutoShopper.Tests;

public sealed class PreviewTests
{
    private static readonly ImmutableArray<ScopeWorld> Allowed = [Jenova, Faerie, Siren];
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(3);

    [Fact]
    public void AllThreeModesArePlannedOnTheSameData()
    {
        var result = Run(Snapshot(), Demands(PlanDemand.ForPurchase(FireShard, DemandQuality.Any, 3000)));

        Assert.Equal(PreviewRunner.Modes.Length, result.Plans.Length);
        for (var index = 0; index < PreviewRunner.Modes.Length; index++)
        {
            var mode = PreviewRunner.Modes[index];
            Assert.Equal(mode, result.For(mode).Mode);
            Assert.True(result.For(mode).IsComplete);
        }

        Assert.Same(result.For(PlanMode.Balanced), result.Selected);
        Assert.True(result.For(PlanMode.Cheapest).Totals.PurchaseEstimate <= result.For(PlanMode.FewestStops).Totals.PurchaseEstimate);
    }

    [Fact]
    public void FewestStopsUsesOneWorldWhenThePremiumAllowsIt()
    {
        var result = Run(Snapshot(), Demands(PlanDemand.ForPurchase(FireShard, DemandQuality.Any, 3000)));

        Assert.True(result.For(PlanMode.FewestStops).Totals.StopCount <= result.For(PlanMode.Cheapest).Totals.StopCount);
    }

    [Fact]
    public void UnresolvedItemsExplainWhereThePricesWent()
    {
        var snapshot = Snapshot();
        var result = Run(snapshot, Demands(
            PlanDemand.ForPurchase(Gil, DemandQuality.Any, 1),
            PlanDemand.ForPurchase(42, DemandQuality.Any, 1),
            PlanDemand.ForPurchase(HighQualityItem, DemandQuality.NormalQuality, 500)));
        var plan = result.Selected;

        Assert.Equal(PreviewReason.NotMarketable, PreviewReview.ReasonFor(Find(plan, Gil), snapshot));
        Assert.Equal(PreviewReason.NotFetched, PreviewReview.ReasonFor(Find(plan, 42), snapshot));
        Assert.Equal(PreviewReason.InsufficientStock, PreviewReview.ReasonFor(Find(plan, HighQualityItem), snapshot));
        Assert.Equal(PreviewReason.NotFetched, PreviewReview.ReasonFor(Find(plan, Gil), null));
    }

    [Fact]
    public void FailedRequestAndEmptyMarketAreTold()
    {
        var failed = ChunkPrices.FailedWith(Chunk(PriceQueryKind.DataCenter, "Aether", Allowed, FireShard), PriceFailure.Timeout);
        var empty = ItemPrices.WithoutData(OneGilItem, Allowed, PriceCoverage.NoData);
        var emptyChunk = new ChunkPrices { Chunk = Chunk(PriceQueryKind.DataCenter, "Aether", Allowed, OneGilItem), Failure = PriceFailure.None, Items = [empty] };
        var snapshot = PriceSnapshotBuilder.Build(1, FetchedAt, Allowed, [failed, emptyChunk], null);
        var result = Run(snapshot, Demands(PlanDemand.ForPurchase(FireShard, DemandQuality.Any, 1), PlanDemand.ForPurchase(OneGilItem, DemandQuality.Any, 1)));

        Assert.Equal(PreviewReason.RequestFailed, PreviewReview.ReasonFor(Find(result.Selected, FireShard), snapshot));
        Assert.Equal(PreviewReason.NoMarketData, PreviewReview.ReasonFor(Find(result.Selected, OneGilItem), snapshot));
        Assert.True((PreviewReview.Evaluate(result, FetchedAt, StaleAfter) & PreviewWarnings.PartialCoverage) != 0);
    }

    [Fact]
    public void WarningsNameMissingPricesStaleDataAndChangedScope()
    {
        var snapshot = Snapshot();
        var result = Run(snapshot, Demands(PlanDemand.ForPurchase(FireShard, DemandQuality.Any, 10), PlanDemand.ForPurchase(42, DemandQuality.Any, 1)));
        var fresh = PreviewReview.Evaluate(result, FetchedAt, TimeSpan.FromDays(30));
        var later = PreviewReview.Evaluate(result, FetchedAt.AddDays(40), TimeSpan.FromDays(30));
        var narrower = Run(snapshot, Demands(PlanDemand.ForPurchase(FireShard, DemandQuality.Any, 10)), [Jenova.ToPlanWorld()]);

        Assert.Equal(PreviewWarnings.MissingPrices | PreviewWarnings.Incomplete, fresh);
        Assert.True((later & PreviewWarnings.StaleData) != 0);
        Assert.True((PreviewReview.Evaluate(narrower, FetchedAt, TimeSpan.FromDays(30)) & PreviewWarnings.ScopeChanged) != 0);
    }

    [Fact]
    public void NoSnapshotMeansNoPrices()
    {
        var result = Run(null, Demands(PlanDemand.ForPurchase(FireShard, DemandQuality.Any, 10)));

        var warnings = PreviewReview.Evaluate(result, FetchedAt, StaleAfter);

        Assert.True((warnings & PreviewWarnings.NoPrices) != 0);
        Assert.False(result.Selected.IsComplete);
    }

    [Fact]
    public void CancelledPreviewThrows()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => PreviewRunner.Run(Input(Snapshot(), Demands(PlanDemand.ForPurchase(FireShard, DemandQuality.Any, 1)), null), new FixedTimeProvider(), cancellation.Token));
    }

    private static PreviewResult Run(PriceSnapshot? snapshot, DemandSet demands, ImmutableArray<PlanWorld>? worlds = null)
    {
        return PreviewRunner.Run(Input(snapshot, demands, worlds), new FixedTimeProvider(), CancellationToken.None);
    }

    private static PreviewInput Input(PriceSnapshot? snapshot, DemandSet demands, ImmutableArray<PlanWorld>? worlds)
    {
        var planWorlds = worlds ?? [Jenova.ToPlanWorld(), Faerie.ToPlanWorld(), Siren.ToPlanWorld()];
        return new PreviewInput
        {
            Generation = 1,
            ListId = Guid.NewGuid(),
            Demands = demands,
            Prices = snapshot,
            Worlds = planWorlds,
            Settings = new PlanSettings
            {
                Mode = PlanMode.Balanced,
                CurrentWorld = Faerie.ToPlanWorld(),
                HomeWorld = Faerie.ToPlanWorld(),
                StopPenaltyGil = 2_000,
                CrossDataCenterPenaltyGil = 10_000,
                FewestStopsPremiumPercent = 10,
            },
        };
    }

    private static DemandSet Demands(params PlanDemand[] demands)
    {
        var ids = new List<uint>();
        for (var index = 0; index < demands.Length; index++)
        {
            if (!ids.Contains(demands[index].ItemId))
            {
                ids.Add(demands[index].ItemId);
            }
        }

        ids.Sort();
        return new DemandSet { Demands = [.. demands], ItemIds = [.. ids], TargetsMet = 0, TargetsUnknown = 0 };
    }

    private static PriceSnapshot Snapshot()
    {
        var chunk = Chunk(PriceQueryKind.DataCenter, "Aether", Allowed, HighQualityItem, FireShard, Gil, OneGilItem);
        var prices = UniversalisTranslator.Translate(Read("dc-multi-trimmed.json"), chunk);
        return PriceSnapshotBuilder.Build(1, FetchedAt, Allowed, [prices], null);
    }

    private static UnresolvedDemand Find(PurchasePlan plan, uint itemId)
    {
        for (var index = 0; index < plan.Unresolved.Length; index++)
        {
            if (plan.Unresolved[index].Demand.ItemId == itemId)
            {
                return plan.Unresolved[index];
            }
        }

        throw new Xunit.Sdk.XunitException($"Item {itemId} is not unresolved");
    }
}
