namespace AutoShopper.Core.Shopping.Import;

// One entry of an imported crafting list: a recipe and how many times to craft it, not an item and a quantity to buy.
public readonly record struct CraftingImport(int SourceLine, string SourceText, uint RecipeId, int Crafts, bool IsSkipped, ImportRowProblem Problem)
{
    public bool IsValid => Problem == ImportRowProblem.None;
}
