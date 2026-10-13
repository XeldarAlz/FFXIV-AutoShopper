using AutoShopper.Core.Shopping.Model;
using System.Runtime.InteropServices;

namespace AutoShopper.Core.Recipes;

// A crafting list is read as a flow: each craft makes its yield of the result and uses up its ingredients. What the list
// makes beyond what it uses itself are its finished items; what it uses beyond what it makes are its direct ingredients.
// A list that already holds its own pre-crafts therefore never asks for a pre-craft and the materials for it at once.
public static class RecipeExpansion
{
    // Far above anything a list line accepts, and low enough that adding two never overflows.
    private const long QuantityCeiling = 1L << 40;

    // Crafting chains in the game are a handful of steps deep; an item further down than this is bought, not crafted.
    private const int MaximumDepth = 32;

    public static ExpansionResult Expand(ReadOnlySpan<RecipeOrder> orders, ExpansionMode mode, IRecipeBook book, IItemTraits traits)
    {
        var crafts = Merge(orders, book, out var unknownRecipes);
        var produced = new Dictionary<uint, long>();
        var consumed = new Dictionary<uint, long>();
        foreach (var craft in crafts.Values)
        {
            Add(produced, craft.Recipe.ResultItemId, Multiply(craft.Count, YieldOf(craft.Recipe)));
            AddIngredients(consumed, craft.Recipe, craft.Count);
        }

        var demand = mode == ExpansionMode.FinishedItems ? Excess(produced, consumed) : Excess(consumed, produced);
        if (mode == ExpansionMode.FullyExpanded)
        {
            ExpandIntermediates(demand, book);
        }

        return new ExpansionResult(ToDemands(demand, traits), unknownRecipes);
    }

    private static Dictionary<uint, PlannedCraft> Merge(ReadOnlySpan<RecipeOrder> orders, IRecipeBook book, out uint[] unknownRecipes)
    {
        var crafts = new Dictionary<uint, PlannedCraft>();
        var unknown = new HashSet<uint>();
        for (var index = 0; index < orders.Length; index++)
        {
            var order = orders[index];
            if (order.Crafts <= 0)
            {
                continue;
            }

            if (!book.TryGetRecipe(order.RecipeId, out var recipe))
            {
                unknown.Add(order.RecipeId);
                continue;
            }

            ref var craft = ref CollectionsMarshal.GetValueRefOrAddDefault(crafts, order.RecipeId, out var exists);
            craft = exists
                ? craft with { Count = Math.Min(craft.Count + order.Crafts, QuantityCeiling) }
                : new PlannedCraft(recipe, order.Crafts);
        }

        unknownRecipes = new uint[unknown.Count];
        unknown.CopyTo(unknownRecipes);
        Array.Sort(unknownRecipes);
        return crafts;
    }

    private static Dictionary<uint, long> Excess(Dictionary<uint, long> amounts, Dictionary<uint, long> offsets)
    {
        var excess = new Dictionary<uint, long>();
        foreach (var (itemId, amount) in amounts)
        {
            offsets.TryGetValue(itemId, out var offset);
            if (amount > offset)
            {
                excess[itemId] = amount - offset;
            }
        }

        return excess;
    }

    // Each craftable item is expanded once, after every item that needs it has added its share, so its crafts are
    // rounded up on the total rather than once per consumer, and it never stays on the list next to its ingredients.
    private static void ExpandIntermediates(Dictionary<uint, long> demand, IRecipeBook book)
    {
        var recipes = new Dictionary<uint, CraftingRecipe>();
        var order = ConsumersFirst(demand, book, recipes);
        for (var index = 0; index < order.Count; index++)
        {
            var itemId = order[index];
            if (!recipes.TryGetValue(itemId, out var recipe) || !demand.Remove(itemId, out var need))
            {
                continue;
            }

            AddIngredients(demand, recipe, CeilingDivide(need, YieldOf(recipe)));
        }
    }

    private static List<uint> ConsumersFirst(Dictionary<uint, long> demand, IRecipeBook book, Dictionary<uint, CraftingRecipe> recipes)
    {
        var roots = new uint[demand.Count];
        demand.Keys.CopyTo(roots, 0);
        Array.Sort(roots);

        var visited = new HashSet<uint>();
        var finished = new List<uint>();
        for (var index = 0; index < roots.Length; index++)
        {
            Visit(roots[index], 0, book, visited, recipes, finished);
        }

        finished.Reverse();
        return finished;
    }

    // Depth first, finishing an item after every ingredient below it, so the reversed order puts each item ahead of its
    // ingredients. An ingredient already on the current path closes a loop and is not followed again; it is bought.
    private static void Visit(uint itemId, int depth, IRecipeBook book, HashSet<uint> visited, Dictionary<uint, CraftingRecipe> recipes, List<uint> finished)
    {
        if (!visited.Add(itemId))
        {
            return;
        }

        if (depth < MaximumDepth && book.TryGetRecipeForItem(itemId, out var recipe))
        {
            recipes[itemId] = recipe;
            var ingredients = recipe.Ingredients;
            for (var index = 0; index < ingredients.Length; index++)
            {
                Visit(ingredients[index].ItemId, depth + 1, book, visited, recipes, finished);
            }
        }

        finished.Add(itemId);
    }

    private static ItemDemand[] ToDemands(Dictionary<uint, long> demand, IItemTraits traits)
    {
        var itemIds = new uint[demand.Count];
        demand.Keys.CopyTo(itemIds, 0);
        Array.Sort(itemIds);

        var demands = new ItemDemand[itemIds.Length];
        for (var index = 0; index < itemIds.Length; index++)
        {
            var itemId = itemIds[index];
            demands[index] = new ItemDemand(itemId, demand[itemId], traits.IsMarketable(itemId));
        }

        return demands;
    }

    private static void AddIngredients(Dictionary<uint, long> target, CraftingRecipe recipe, long crafts)
    {
        var ingredients = recipe.Ingredients;
        for (var index = 0; index < ingredients.Length; index++)
        {
            var ingredient = ingredients[index];
            if (ingredient.ItemId == 0 || ingredient.Amount <= 0)
            {
                continue;
            }

            Add(target, ingredient.ItemId, Multiply(crafts, ingredient.Amount));
        }
    }

    private static void Add(Dictionary<uint, long> target, uint itemId, long amount)
    {
        if (amount <= 0)
        {
            return;
        }

        ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(target, itemId, out _);
        slot = Math.Min(slot + amount, QuantityCeiling);
    }

    private static long Multiply(long count, int factor)
    {
        var product = (Int128)count * factor;
        return product >= QuantityCeiling ? QuantityCeiling : (long)product;
    }

    private static long CeilingDivide(long amount, int divisor) => (amount + divisor - 1) / divisor;

    private static int YieldOf(CraftingRecipe recipe) => Math.Max(recipe.Yield, 1);

    private readonly record struct PlannedCraft(CraftingRecipe Recipe, long Count);
}
