namespace AutoShopper.Core.Recipes;

public interface IRecipeBook
{
    bool TryGetRecipe(uint recipeId, out CraftingRecipe recipe);

    // When several recipes make the item, one per crafting class, the lowest recipe id, so the choice never changes.
    bool TryGetRecipeForItem(uint itemId, out CraftingRecipe recipe);
}
