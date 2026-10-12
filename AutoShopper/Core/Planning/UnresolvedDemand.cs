namespace AutoShopper.Core.Planning;

internal enum UnresolvedReason : byte
{
    InvalidDemand = 0,
    NoListings = 1,
    AboveUnitCap = 2,
    InsufficientStock = 3,
    NoFittingStacks = 4,
    TakenByStricterDemand = 5,
    StopLimit = 6,
    SearchLimit = 7,
}

internal readonly record struct UnresolvedDemand(DemandKey Demand, int RequiredQuantity, int CoveredQuantity, int MissingQuantity, UnresolvedReason Reason);
