using AutoShopper.Core.Recipes;
using static AutoShopper.Tests.ImportFixtures;

namespace AutoShopper.Tests;

public sealed class RecipeExpansionTests
{
    private const uint Nugget = 50001;
    private const uint Bar = 50002;
    private const uint Chain = 50003;
    private const uint Anvil = 50004;
    private const uint BarRecipe = 32001;
    private const uint ChainRecipe = 32002;
    private const uint AnvilRecipe = 32003;

    [Fact]
    public void FinishedItemsCountCraftsTimesYield()
    {
        var book = Workshop();

        var result = RecipeExpansion.Expand([new RecipeOrder(RivetsRecipe, 3)], ExpansionMode.FinishedItems, book, book);

        Assert.Equal(new Dictionary<uint, long> { [Rivets] = 9 }, Quantities(result));
    }

    [Fact]
    public void DirectIngredientsStayOneLevelDeep()
    {
        var book = Workshop();

        var result = RecipeExpansion.Expand([new RecipeOrder(ShieldRecipe, 1)], ExpansionMode.DirectIngredients, book, book);

        Assert.Equal(new Dictionary<uint, long> { [WindShard] = 2, [Ingot] = 1, [Rivets] = 7 }, Quantities(result));
    }

    [Fact]
    public void YieldRoundsCraftsUp()
    {
        var book = Workshop();

        var result = RecipeExpansion.Expand([new RecipeOrder(ShieldRecipe, 1)], ExpansionMode.FullyExpanded, book, book);

        // Seven rivets at three a craft is three crafts: three ingots for the rivets and one for the shield, three ore each.
        Assert.Equal(new Dictionary<uint, long> { [FireShard] = 7, [WindShard] = 2, [Ore] = 12 }, Quantities(result));
    }

    [Fact]
    public void SharedIntermediateRoundsOnItsTotal()
    {
        var book = new FakeRecipeBook(
            [
                Recipe(BarRecipe, Bar, 2, (Nugget, 3)),
                Recipe(ChainRecipe, Chain, 1, (Bar, 1)),
                Recipe(AnvilRecipe, Anvil, 1, (Bar, 1), (Chain, 3)),
            ],
            []);

        var result = RecipeExpansion.Expand([new RecipeOrder(AnvilRecipe, 1)], ExpansionMode.FullyExpanded, book, book);

        // Four bars at two a craft is two crafts; rounding per consumer would have made three.
        Assert.Equal(new Dictionary<uint, long> { [Nugget] = 6 }, Quantities(result));
    }

    [Fact]
    public void FullExpansionNeverKeepsAnIntermediateNextToItsIngredients()
    {
        var book = Workshop();

        var result = RecipeExpansion.Expand([new RecipeOrder(SwordRecipe, 2)], ExpansionMode.FullyExpanded, book, book);

        var quantities = Quantities(result);
        Assert.False(quantities.ContainsKey(Ingot));
        Assert.False(quantities.ContainsKey(Grip));
        Assert.Equal(new Dictionary<uint, long> { [FireShard] = 10, [WindShard] = 2, [Ore] = 12, [Leather] = 2 }, quantities);
    }

    [Fact]
    public void ListsOwnPreCraftsAreNotBoughtAgain()
    {
        var book = Workshop();
        RecipeOrder[] orders = [new(SwordRecipe, 2), new(IngotRecipe, 4)];

        var direct = Quantities(RecipeExpansion.Expand(orders, ExpansionMode.DirectIngredients, book, book));
        var finished = Quantities(RecipeExpansion.Expand(orders, ExpansionMode.FinishedItems, book, book));

        Assert.Equal(new Dictionary<uint, long> { [FireShard] = 10, [Ore] = 12, [Grip] = 2 }, direct);
        Assert.Equal(new Dictionary<uint, long> { [Sword] = 2 }, finished);
    }

    [Fact]
    public void ShortPreCraftsAreToppedUp()
    {
        var book = Workshop();
        RecipeOrder[] orders = [new(SwordRecipe, 3), new(IngotRecipe, 4)];

        var direct = Quantities(RecipeExpansion.Expand(orders, ExpansionMode.DirectIngredients, book, book));
        var full = Quantities(RecipeExpansion.Expand(orders, ExpansionMode.FullyExpanded, book, book));

        Assert.Equal(2, direct[Ingot]);
        Assert.Equal(12, direct[Ore]);
        Assert.False(full.ContainsKey(Ingot));
        Assert.Equal(18, full[Ore]);
    }

    [Fact]
    public void RepeatedRecipesAreMerged()
    {
        var book = Workshop();
        RecipeOrder[] split = [new(RivetsRecipe, 1), new(ShieldRecipe, 1), new(RivetsRecipe, 2)];
        RecipeOrder[] merged = [new(RivetsRecipe, 3), new(ShieldRecipe, 1)];

        var splitFull = RecipeExpansion.Expand(split, ExpansionMode.FullyExpanded, book, book);
        var mergedFull = RecipeExpansion.Expand(merged, ExpansionMode.FullyExpanded, book, book);
        var splitFinished = RecipeExpansion.Expand(split, ExpansionMode.FinishedItems, book, book);

        Assert.Equal(mergedFull.Demands, splitFull.Demands);
        // Nine rivets made, seven used by the shield.
        Assert.Equal(new Dictionary<uint, long> { [Rivets] = 2, [Shield] = 1 }, Quantities(splitFinished));
    }

    [Fact]
    public void LowestRecipeIdMakesAnIntermediate()
    {
        var book = Workshop();

        var result = RecipeExpansion.Expand([new RecipeOrder(RivetsRecipe, 1)], ExpansionMode.FullyExpanded, book, book);

        Assert.Equal(new Dictionary<uint, long> { [FireShard] = 2, [Ore] = 3 }, Quantities(result));
    }

    [Fact]
    public void UnmarketableItemsAreReportedNotDropped()
    {
        var book = Workshop();

        var result = RecipeExpansion.Expand([new RecipeOrder(SwordRecipe, 1)], ExpansionMode.DirectIngredients, book, book);

        var grip = Array.Find(result.Demands, demand => demand.ItemId == Grip);
        Assert.Equal(1, grip.Quantity);
        Assert.False(grip.IsMarketable);
        Assert.True(Array.Find(result.Demands, demand => demand.ItemId == Ingot).IsMarketable);
    }

    [Fact]
    public void UnknownRecipesAreListedOnce()
    {
        var book = Workshop();

        var result = RecipeExpansion.Expand([new RecipeOrder(99, 1), new RecipeOrder(IngotRecipe, 1), new RecipeOrder(98, 1), new RecipeOrder(99, 2)], ExpansionMode.FinishedItems, book, book);

        Assert.Equal(new uint[] { 98, 99 }, result.UnknownRecipes);
        Assert.Equal(new Dictionary<uint, long> { [Ingot] = 1 }, Quantities(result));
    }

    [Fact]
    public void RecipeLoopEnds()
    {
        const uint first = 60001;
        const uint second = 60002;
        var book = new FakeRecipeBook([Recipe(33001, first, 1, (second, 1)), Recipe(33002, second, 1, (first, 1))], []);

        var result = RecipeExpansion.Expand([new RecipeOrder(33001, 1)], ExpansionMode.FullyExpanded, book, book);

        Assert.Equal(new Dictionary<uint, long> { [second] = 1 }, Quantities(result));
    }

    [Fact]
    public void DemandsAreOrderedByItemId()
    {
        var book = Workshop();

        var result = RecipeExpansion.Expand([new RecipeOrder(SwordRecipe, 1), new RecipeOrder(ShieldRecipe, 1)], ExpansionMode.FullyExpanded, book, book);

        for (var index = 1; index < result.Demands.Length; index++)
        {
            Assert.True(result.Demands[index - 1].ItemId < result.Demands[index].ItemId);
        }
    }

    [Fact]
    public void HugeCountsSaturateInsteadOfOverflowing()
    {
        var recipes = new CraftingRecipe[5];
        for (var index = 0; index < recipes.Length; index++)
        {
            recipes[index] = Recipe(34001 + (uint)index, 70001 + (uint)index, 1, (70002 + (uint)index, 255));
        }

        var book = new FakeRecipeBook(recipes, []);

        var result = RecipeExpansion.Expand([new RecipeOrder(34001, int.MaxValue)], ExpansionMode.FullyExpanded, book, book);

        Assert.Single(result.Demands);
        Assert.Equal(70006u, result.Demands[0].ItemId);
        Assert.True(result.Demands[0].Quantity > int.MaxValue);
    }
}
