namespace AutoShopper.Core.Execution.Model;

internal enum RunState : byte
{
    Idle = 0,
    Preflight = 1,
    FetchingPrices = 2,
    Planning = 3,
    Ready = 4,
    Traveling = 5,
    GoingToBoard = 6,
    Searching = 7,
    Inspecting = 8,
    Guarding = 9,
    PersistingIntent = 10,
    Submitting = 11,
    AwaitingResult = 12,
    Reconciling = 13,
    Paused = 14,
    NeedsReconciliation = 15,
    Completed = 16,
    CompletedPartial = 17,
    Stopped = 18,
    Failed = 19,
}
