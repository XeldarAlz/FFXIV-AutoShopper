namespace AutoShopper.Core.Recipes;

// Demands are ordered by item id and unknown recipe ids ascend, so the same list always expands the same way.
public readonly record struct ExpansionResult(ItemDemand[] Demands, uint[] UnknownRecipes);
