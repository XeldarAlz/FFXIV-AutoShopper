namespace AutoShopper.Core.Planning;

internal readonly record struct PlanLine(DemandKey Demand, PlanOffer Offer, long Payable);
