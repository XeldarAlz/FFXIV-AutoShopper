using System.Collections.Immutable;
using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Pricing.Model;
using static AutoShopper.Tests.Fixtures;

namespace AutoShopper.Tests;

public sealed class RunEstimatesTests
{
    private const uint Ore = 5_057;
    private const uint Cloth = 5_325;
    private static readonly DateTime RefreshedAt = ObservedAt.AddHours(4);

    [Fact]
    public void ARefreshReplacesOnlyTheWorldsItCovered()
    {
        var copy = RunEstimates.From(Snapshot(ObservedAt, [Alpha, Beta, Gamma], Offer(Alpha, Ore, 5, 100), Offer(Beta, Ore, 5, 110), Offer(Gamma, Ore, 5, 120)));

        var merged = copy.MergedWith(Snapshot(RefreshedAt, [Beta], Offer(Beta, Ore, 3, 90)));

        Assert.Equal(RefreshedAt, merged.FetchedAtUtc);
        Assert.Equal(3, merged.Offers.Length);
        Assert.Contains(Offer(Alpha, Ore, 5, 100), merged.Offers);
        Assert.Contains(Offer(Gamma, Ore, 5, 120), merged.Offers);
        Assert.Contains(Offer(Beta, Ore, 3, 90), merged.Offers);
        Assert.DoesNotContain(Offer(Beta, Ore, 5, 110), merged.Offers);
    }

    [Fact]
    public void AnItemTheRefreshCouldNotPriceKeepsWhatTheCopyHeld()
    {
        var copy = RunEstimates.From(Snapshot(ObservedAt, [Alpha, Beta], Offer(Beta, Ore, 5, 110), Offer(Beta, Cloth, 2, 300)));
        var refresh = Snapshot(RefreshedAt, [Beta], [Cloth], Offer(Beta, Ore, 4, 95));

        var merged = copy.MergedWith(refresh);

        Assert.Contains(Offer(Beta, Cloth, 2, 300), merged.Offers);
        Assert.Contains(Offer(Beta, Ore, 4, 95), merged.Offers);
        Assert.DoesNotContain(Offer(Beta, Ore, 5, 110), merged.Offers);
    }

    [Fact]
    public void AnItemSoldOutOnARefreshedWorldLosesItsOldOffersThere()
    {
        var copy = RunEstimates.From(Snapshot(ObservedAt, [Alpha, Beta], Offer(Alpha, Ore, 5, 100), Offer(Beta, Ore, 5, 110)));
        var refresh = Snapshot(RefreshedAt, [Beta], [], [Ore]);

        var merged = copy.MergedWith(refresh);

        Assert.Equal([Offer(Alpha, Ore, 5, 100)], merged.Offers.AsSpan().ToArray());
    }

    [Fact]
    public void ARefreshThatAnsweredNothingLeavesTheCopyAsItWas()
    {
        var copy = RunEstimates.From(Snapshot(ObservedAt, [Alpha], Offer(Alpha, Ore, 5, 100)));

        var merged = copy.MergedWith(Snapshot(RefreshedAt, [Alpha], [Ore]));

        Assert.Same(copy, merged);
        Assert.Equal(ObservedAt, merged.FetchedAtUtc);
    }

    [Fact]
    public void TheSnapshotTheCopyCameFromIsNeverChanged()
    {
        var shared = Snapshot(ObservedAt, [Alpha, Beta], Offer(Alpha, Ore, 5, 100), Offer(Beta, Ore, 5, 110));
        var copy = RunEstimates.From(shared);

        copy.MergedWith(Snapshot(RefreshedAt, [Beta], Offer(Beta, Ore, 1, 80)));

        Assert.Equal(2, shared.Offers.Length);
        Assert.Equal(2, shared.Worlds.Length);
        Assert.Equal(ObservedAt, shared.FetchedAtUtc);
    }

    private static PriceSnapshot Snapshot(DateTime fetchedAtUtc, PlanWorld[] worlds, params PlanOffer[] offers)
        => Snapshot(fetchedAtUtc, worlds, [], [], offers);

    private static PriceSnapshot Snapshot(DateTime fetchedAtUtc, PlanWorld[] worlds, uint[] failedItems, params PlanOffer[] offers)
        => Snapshot(fetchedAtUtc, worlds, failedItems, [], offers);

    // Items are the ones the offers name, the failed ones and the answered ones without offers.
    private static PriceSnapshot Snapshot(DateTime fetchedAtUtc, PlanWorld[] worlds, uint[] failedItems, uint[] emptyItems, params PlanOffer[] offers)
    {
        var itemIds = new SortedSet<uint>(failedItems);
        itemIds.UnionWith(emptyItems);
        for (var index = 0; index < offers.Length; index++)
        {
            itemIds.Add(offers[index].ItemId);
        }

        var sortedIds = new uint[itemIds.Count];
        itemIds.CopyTo(sortedIds);
        var items = ImmutableArray.CreateBuilder<ItemPrices>(sortedIds.Length);
        for (var itemIndex = 0; itemIndex < sortedIds.Length; itemIndex++)
        {
            var itemId = sortedIds[itemIndex];
            var failed = Array.IndexOf(failedItems, itemId) >= 0;
            var itemOffers = ImmutableArray.CreateBuilder<PlanOffer>();
            for (var index = 0; index < offers.Length; index++)
            {
                if (!failed && offers[index].ItemId == itemId)
                {
                    itemOffers.Add(offers[index]);
                }
            }

            items.Add(new ItemPrices
            {
                ItemId = itemId,
                Coverage = failed ? PriceCoverage.RequestFailed : PriceCoverage.None,
                Worlds = ImmutableArray<WorldPrice>.Empty,
                Offers = itemOffers.ToImmutable(),
            });
        }

        var scopeWorlds = ImmutableArray.CreateBuilder<ScopeWorld>(worlds.Length);
        for (var index = 0; index < worlds.Length; index++)
        {
            scopeWorlds.Add(new ScopeWorld(worlds[index].WorldId, worlds[index].Name, worlds[index].DataCenterId, "Center", (byte)worlds[index].RegionId));
        }

        var snapshotOffers = ImmutableArray.CreateBuilder<PlanOffer>();
        for (var index = 0; index < items.Count; index++)
        {
            snapshotOffers.AddRange(items[index].Offers);
        }

        return new PriceSnapshot
        {
            Generation = 1,
            FetchedAtUtc = fetchedAtUtc,
            Worlds = scopeWorlds.MoveToImmutable(),
            Items = items.MoveToImmutable(),
            Offers = snapshotOffers.ToImmutable(),
            Freshness = ImmutableArray<WorldFreshness>.Empty,
            Coverage = failedItems.Length > 0 ? PriceCoverage.RequestFailed : PriceCoverage.None,
            RequestCount = 1,
            FailedRequestCount = 0,
        };
    }
}
