using AutoShopper.Core.Planning;
using AutoShopper.Core.Preview.Model;
using AutoShopper.Core.Shopping.Model;

namespace AutoShopper.Tests;

// Lines of one item and quality become one planner demand; a total past 999,999 is flagged on the lines in the list
// editor instead of being clamped or silently dropping the item from the plan.
public sealed class MergedLineLimitTests
{
    private const uint Ore = 5_057;
    private const uint Shard = 2;

    [Fact]
    public void BuyLinesAddingUpPastTheLimitAreFlaggedNotClamped()
    {
        ShoppingLine[] lines = [Line(Ore, 600_000), Line(Ore, 500_000)];

        var validation = ListNormalizer.Validate(lines, null);
        var normalized = ListNormalizer.Normalize(lines);

        Assert.True(validation.HasErrors);
        Assert.Equal(2, validation.ErrorLines);
        Assert.Equal(LineProblems.MergedOverLimit | LineProblems.Duplicate, validation.At(0));
        Assert.Equal(LineProblems.MergedOverLimit | LineProblems.Duplicate, validation.At(1));
        Assert.Equal(1_100_000, Assert.Single(normalized.Demands.ToArray()).Quantity);
    }

    [Fact]
    public void ThePlannerRefusesTheMergedTotalInsteadOfBuyingACutAmount()
    {
        ShoppingLine[] lines = [Line(Ore, 600_000), Line(Ore, 500_000)];

        var demands = DemandBuilder.Build(ListNormalizer.Normalize(lines).Demands, null);
        var normalized = DemandNormalizer.Normalize(demands.Demands);

        Assert.Empty(normalized.Lines);
        Assert.Equal(1_100_000, Assert.Single(normalized.Invalid).RequiredQuantity);
    }

    [Fact]
    public void ABuyLineAndATopUpTargetOfTheSameQualityCountTogether()
    {
        ShoppingLine[] lines = [Line(Ore, 999_999), Line(Ore, 1, mode: QuantityMode.ReachInventory)];

        var validation = ListNormalizer.Validate(lines, null);

        Assert.Equal(LineProblems.MergedOverLimit, validation.At(0));
        Assert.Equal(LineProblems.MergedOverLimit, validation.At(1));
    }

    [Fact]
    public void ExactlyTheLimitIsAllowed()
    {
        ShoppingLine[] lines = [Line(Ore, 500_000), Line(Ore, 499_999)];

        var validation = ListNormalizer.Validate(lines, null);

        Assert.False(validation.HasErrors);
        Assert.Equal(LineProblems.Duplicate, validation.At(0));
    }

    [Fact]
    public void TopUpTargetsUseTheLargestRatherThanTheSum()
    {
        ShoppingLine[] lines = [Line(Ore, 999_999, mode: QuantityMode.ReachInventory), Line(Ore, 999_999, mode: QuantityMode.ReachInventory)];

        Assert.False(ListNormalizer.Validate(lines, null).HasErrors);
    }

    [Fact]
    public void QualitiesAndItemsCountApart()
    {
        ShoppingLine[] lines =
        [
            Line(Ore, 600_000, ItemQuality.HighQuality),
            Line(Ore, 600_000, ItemQuality.NormalQuality),
            Line(Ore, 600_000),
            Line(Shard, 600_000),
        ];

        Assert.False(ListNormalizer.Validate(lines, null).HasErrors);
    }

    [Fact]
    public void ExtraAllowancesOfABuyLineAndATopUpTargetCountTogether()
    {
        ShoppingLine[] lines = [Line(Ore, 10, excess: 600_000), Line(Ore, 10, mode: QuantityMode.ReachInventory, excess: 600_000)];

        var validation = ListNormalizer.Validate(lines, null);

        Assert.Equal(LineProblems.MergedOverLimit, validation.At(0));
        Assert.Equal(LineProblems.MergedOverLimit, validation.At(1));
    }

    [Fact]
    public void ALineThatIsAlreadyInvalidDoesNotCount()
    {
        ShoppingLine[] lines = [Line(Ore, 999_999), Line(Ore, 600_000, cap: -1)];

        var validation = ListNormalizer.Validate(lines, null);

        Assert.Equal(LineProblems.Duplicate, validation.At(0));
        Assert.Equal(LineProblems.InvalidUnitCap | LineProblems.Duplicate, validation.At(1));
    }

    private static ShoppingLine Line(uint itemId, int quantity, ItemQuality quality = ItemQuality.Any, QuantityMode mode = QuantityMode.Buy, int excess = 0, long cap = 0)
    {
        var line = ShoppingLine.Create(itemId, quantity, quality, mode);
        line.ExcessAllowance = excess;
        line.MaxUnitPrice = cap;
        return line;
    }
}
