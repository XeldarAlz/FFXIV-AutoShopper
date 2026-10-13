namespace AutoShopper.Core.Tasks;

// What the running panel shows of a run's trip between stops.
internal enum TravelPhase : byte
{
    None = 0,
    ChangingWorld = 1,
    TransferringDataCenter = 2,
    Settling = 3,
    GoingToBoard = 4,
    WaitingToRetry = 5,
    ReturningHome = 6,
}

// The pending journey as it stood at the logout of a planned transfer. It is taken then because the journey clears
// its plan as soon as it arrives, which may come before or after the login event in the same frame.
internal readonly record struct TransferSnapshot(string World, DateTime StartedAtUtc);
