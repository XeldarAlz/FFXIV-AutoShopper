using AutoShopper.Core.Planning;
using AutoShopper.Core.Preview.Model;
using AutoShopper.Core.Shopping.Model;
using static AutoShopper.Tests.Fixtures;
using PlanDemandKey = AutoShopper.Core.Planning.DemandKey;

namespace AutoShopper.Tests;

public sealed class DemandBuilderTests
{
    private const uint Ore = 5_100;
    private const uint Shard = 2;

    [Fact]
    public void BuyLineBecomesAFixedPurchaseWithItsLimits()
    {
        var list = List(Line(Ore, 12, ItemQuality.HighQuality, QuantityMode.Buy, cap: 450, excess: 3));

        var set = Build(list, owned: null);

        var demand = Assert.Single(set.Demands);
        Assert.Equal(new PlanDemandKey(Ore, DemandQuality.HighQuality), demand.Key);
        Assert.Equal(12, demand.RequiredQuantity);
        Assert.Equal(3, demand.ExcessAllowance);
        Assert.Equal(450, demand.UnitCapIncludingFee);
        Assert.Equal(0, set.TargetsUnknown);
    }

    [Fact]
    public void TargetLineAsksForTheShortfall()
    {
        var list = List(Line(Ore, 20, ItemQuality.Any, QuantityMode.ReachInventory));

        var set = Build(list, Owned(Ore, normal: 10, high: 5));

        var demand = Assert.Single(set.Demands);
        Assert.Equal(5, demand.RequiredQuantity);
        Assert.Equal(DemandQuality.Any, demand.Quality);
    }

    [Fact]
    public void AnyTargetCountsWhatAStricterTargetWillBuy()
    {
        var list = List(
            Line(Ore, 10, ItemQuality.HighQuality, QuantityMode.ReachInventory),
            Line(Ore, 15, ItemQuality.Any, QuantityMode.ReachInventory));

        var set = Build(list, Owned(Ore, normal: 3, high: 4));

        Assert.Equal(2, set.Demands.Length);
        Assert.Equal(6, Required(set, DemandQuality.HighQuality));
        Assert.Equal(2, Required(set, DemandQuality.Any));
    }

    [Fact]
    public void MetTargetsAndUnknownOwnedCountsAreReported()
    {
        var list = List(
            Line(Ore, 5, ItemQuality.Any, QuantityMode.ReachInventory),
            Line(Shard, 50, ItemQuality.Any, QuantityMode.ReachInventory),
            Line(Shard, 7, ItemQuality.NormalQuality, QuantityMode.Buy));

        var known = Build(list, Owned(Ore, normal: 9, high: 0));
        var unknown = Build(list, owned: null);

        Assert.Equal(1, known.TargetsMet);
        Assert.Equal(1, known.TargetsUnknown);
        Assert.Single(known.Demands);
        Assert.Equal(2, unknown.TargetsUnknown);
        Assert.Equal(DemandQuality.NormalQuality, Assert.Single(unknown.Demands).Quality);
        Assert.Equal([Shard, Ore], known.ItemIds.AsSpan().ToArray());
    }

    [Fact]
    public void InvalidLinesAreLeftOutAndDuplicatesMerge()
    {
        var list = List(
            Line(Ore, 4, ItemQuality.Any, QuantityMode.Buy),
            Line(Ore, 6, ItemQuality.Any, QuantityMode.Buy),
            Line(0, 3, ItemQuality.Any, QuantityMode.Buy),
            Line(Shard, 0, ItemQuality.Any, QuantityMode.Buy));

        var set = Build(list, owned: null);

        var demand = Assert.Single(set.Demands);
        Assert.Equal(10, demand.RequiredQuantity);
        Assert.Equal([Ore], set.ItemIds.AsSpan().ToArray());
    }

    [Fact]
    public void UnitCapIncludesTheFee()
    {
        var list = List(Line(Ore, 2, ItemQuality.Any, QuantityMode.Buy, cap: 105));
        var set = Build(list, owned: null);
        var request = Request(Settings(), set.Demands, Offer(Alpha, Ore, 1, 100), Offer(Alpha, Ore, 1, 101));

        var plan = NewPlanner().Plan(request);

        Assert.Equal(1, PlannedQuantity(plan, set.Demands[0].Key));
        Assert.Equal(UnresolvedReason.AboveUnitCap, Unresolved(plan, set.Demands[0].Key).Reason);
        Assert.Equal(105, plan.Totals.PurchaseEstimate);
    }

    [Fact]
    public void SameDemandsCompareByValue()
    {
        var list = List(Line(Ore, 20, ItemQuality.Any, QuantityMode.ReachInventory));

        var first = Build(list, Owned(Ore, normal: 10, high: 0));
        var second = Build(list, Owned(Ore, normal: 10, high: 0));
        var third = Build(list, Owned(Ore, normal: 11, high: 0));

        Assert.True(first.SameDemands(second));
        Assert.False(first.SameDemands(third));
    }

    private static DemandSet Build(ShoppingList list, Dictionary<uint, OwnedCount>? owned)
    {
        return DemandBuilder.Build(ListNormalizer.Normalize(list.Lines).Demands, owned);
    }

    private static int Required(DemandSet set, DemandQuality quality)
    {
        for (var index = 0; index < set.Demands.Length; index++)
        {
            if (set.Demands[index].Quality == quality)
            {
                return set.Demands[index].RequiredQuantity;
            }
        }

        throw new Xunit.Sdk.XunitException($"No {quality} demand");
    }

    private static Dictionary<uint, OwnedCount> Owned(uint itemId, int normal, int high)
    {
        return new Dictionary<uint, OwnedCount> { [itemId] = new OwnedCount(normal, high) };
    }

    private static ShoppingList List(params ShoppingLine[] lines)
    {
        var list = ShoppingList.Create("Test", ObservedAt);
        list.Lines.AddRange(lines);
        return list;
    }

    private static ShoppingLine Line(uint itemId, int quantity, ItemQuality quality, QuantityMode mode, long cap = 0, int excess = 0)
    {
        var line = ShoppingLine.Create(itemId, quantity, quality, mode);
        line.MaxUnitPrice = cap;
        line.ExcessAllowance = excess;
        return line;
    }
}
