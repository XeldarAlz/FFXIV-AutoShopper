using AutoShopper.Core.Shopping.Model;

namespace AutoShopper.Tests;

public sealed class CrystalRestockTests
{
    private const uint FireShard = 2;
    private const uint WaterShard = 7;
    private const uint FireCrystal = 8;
    private const uint IceCrystal = 9;
    private const uint FireCluster = 14;
    private const uint WaterCluster = 19;

    private static readonly Predicate<uint> AnyCrystal = static itemId => itemId is >= FireShard and <= WaterCluster;

    [Fact]
    public void ItemIdsFollowTheGameSheetOrder()
    {
        Assert.Equal(FireShard, CrystalRestock.ItemIdOf(0, 0));
        Assert.Equal(WaterShard, CrystalRestock.ItemIdOf(5, 0));
        Assert.Equal(FireCrystal, CrystalRestock.ItemIdOf(0, 1));
        Assert.Equal(IceCrystal, CrystalRestock.ItemIdOf(1, 1));
        Assert.Equal(FireCluster, CrystalRestock.ItemIdOf(0, 2));
        Assert.Equal(WaterCluster, CrystalRestock.ItemIdOf(5, 2));
    }

    [Fact]
    public void EveryElementAndKindIsADistinctItem()
    {
        var seen = new HashSet<uint>();
        for (var kindIndex = 0; kindIndex < CrystalRestock.KindCount; kindIndex++)
        {
            for (var elementIndex = 0; elementIndex < CrystalRestock.ElementCount; elementIndex++)
            {
                Assert.True(seen.Add(CrystalRestock.ItemIdOf(elementIndex, kindIndex)));
            }
        }

        Assert.Equal(18, seen.Count);
    }

    [Fact]
    public void CountMultipliesElementsByKinds()
    {
        Assert.Equal(18, CrystalRestock.Count(CrystalElements.All, CrystalKinds.All));
        Assert.Equal(4, CrystalRestock.Count(CrystalElements.Fire | CrystalElements.Ice, CrystalKinds.Shard | CrystalKinds.Cluster));
        Assert.Equal(0, CrystalRestock.Count(CrystalElements.None, CrystalKinds.All));
        Assert.Equal(0, CrystalRestock.Count(CrystalElements.All, CrystalKinds.None));
    }

    [Fact]
    public void PicksBecomeTopUpLinesAtAnyQuality()
    {
        var lines = new List<ShoppingLine>();

        var outcome = CrystalRestock.Apply(lines, CrystalElements.Fire | CrystalElements.Ice, CrystalKinds.Crystal, 500, AnyCrystal);

        Assert.Equal(new CrystalRestockOutcome(2, 0, 0), outcome);
        Assert.Equal(new[] { FireCrystal, IceCrystal }, new[] { lines[0].ItemId, lines[1].ItemId });
        for (var index = 0; index < lines.Count; index++)
        {
            Assert.Equal(QuantityMode.ReachInventory, lines[index].Mode);
            Assert.Equal(ItemQuality.Any, lines[index].Quality);
            Assert.Equal(500, lines[index].Quantity);
            Assert.NotEqual(Guid.Empty, lines[index].Id);
        }
    }

    [Fact]
    public void AnExistingTopUpLineTakesTheNewTarget()
    {
        var existing = ShoppingLine.Create(FireShard, 200, ItemQuality.Any, QuantityMode.ReachInventory);
        var lines = new List<ShoppingLine> { existing };

        var outcome = CrystalRestock.Apply(lines, CrystalElements.Fire, CrystalKinds.Shard, 900, AnyCrystal);

        Assert.Equal(new CrystalRestockOutcome(0, 1, 0), outcome);
        Assert.Single(lines);
        Assert.Same(existing, lines[0]);
        Assert.Equal(900, existing.Quantity);
    }

    [Fact]
    public void BuyLinesAndQualityLinesAreLeftAlone()
    {
        var buy = ShoppingLine.Create(FireShard, 30, ItemQuality.Any, QuantityMode.Buy);
        var normal = ShoppingLine.Create(FireShard, 40, ItemQuality.NormalQuality, QuantityMode.ReachInventory);
        var lines = new List<ShoppingLine> { buy, normal };

        var outcome = CrystalRestock.Apply(lines, CrystalElements.Fire, CrystalKinds.Shard, 600, AnyCrystal);

        Assert.Equal(new CrystalRestockOutcome(1, 0, 0), outcome);
        Assert.Equal(3, lines.Count);
        Assert.Equal(30, buy.Quantity);
        Assert.Equal(40, normal.Quantity);
        Assert.Equal(QuantityMode.ReachInventory, lines[2].Mode);
        Assert.Equal(ItemQuality.Any, lines[2].Quality);
    }

    [Fact]
    public void IdsTheGameDataRefusesAreLeftOut()
    {
        var lines = new List<ShoppingLine>();

        var outcome = CrystalRestock.Apply(lines, CrystalElements.Fire | CrystalElements.Water, CrystalKinds.Cluster, 100, static itemId => itemId != WaterCluster);

        Assert.Equal(new CrystalRestockOutcome(1, 0, 1), outcome);
        Assert.Equal(FireCluster, Assert.Single(lines).ItemId);
    }

    [Fact]
    public void AFullListLeavesTheRestOut()
    {
        var lines = new List<ShoppingLine>(ShoppingLimits.MaximumLinesPerList);
        for (var index = 0; index < ShoppingLimits.MaximumLinesPerList - 1; index++)
        {
            lines.Add(ShoppingLine.Create(100_000u + (uint)index, 1, ItemQuality.Any, QuantityMode.Buy));
        }

        var outcome = CrystalRestock.Apply(lines, CrystalElements.Fire | CrystalElements.Ice, CrystalKinds.Shard, 50, AnyCrystal);

        Assert.Equal(new CrystalRestockOutcome(1, 0, 1), outcome);
        Assert.Equal(ShoppingLimits.MaximumLinesPerList, lines.Count);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(9_999, 9_999)]
    [InlineData(50_000, 9_999)]
    public void TheTargetStaysWithinWhatTheCrystalBagHolds(long target, int expected)
    {
        Assert.Equal(expected, CrystalRestock.ClampTarget(target));
    }

    [Fact]
    public void NothingPickedChangesNothing()
    {
        var lines = new List<ShoppingLine>();

        var outcome = CrystalRestock.Apply(lines, CrystalElements.None, CrystalKinds.All, 100, AnyCrystal);

        Assert.Equal(default, outcome);
        Assert.Empty(lines);
    }
}
