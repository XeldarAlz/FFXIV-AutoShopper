namespace AutoShopper.Core.Recipes;

public readonly record struct RecipeIngredient(uint ItemId, int Amount);

// Yield is how many items one craft makes. Ingredients include the crystals.
public readonly record struct CraftingRecipe(uint RecipeId, uint ResultItemId, int Yield, RecipeIngredient[] Ingredients);
