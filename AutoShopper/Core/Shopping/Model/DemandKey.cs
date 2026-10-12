namespace AutoShopper.Core.Shopping.Model;

public readonly record struct DemandKey(uint ItemId, ItemQuality Quality, QuantityMode Mode);
