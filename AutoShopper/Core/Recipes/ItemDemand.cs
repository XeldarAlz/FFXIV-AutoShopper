namespace AutoShopper.Core.Recipes;

public readonly record struct ItemDemand(uint ItemId, long Quantity, bool IsMarketable);
