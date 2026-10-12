namespace AutoShopper.Core.Planning;

internal readonly record struct PlanTotals(
    long PurchaseEstimate,
    long FeeEstimate,
    int StopCount,
    int DataCenterChanges,
    int WorldChanges,
    int PlannedQuantity,
    int ExcessQuantity,
    int MissingQuantity);
