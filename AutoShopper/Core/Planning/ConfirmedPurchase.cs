namespace AutoShopper.Core.Planning;

internal readonly record struct ConfirmedPurchase(DemandKey Demand, string ListingKey, int Quantity, long Payable);
