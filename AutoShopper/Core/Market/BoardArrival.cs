namespace AutoShopper.Core.Market;

internal enum BoardArrivalFailure : byte
{
    None = 0,
    NotAttuned = 1,
    TeleportFailed = 2,
    AethernetFailed = 3,
    NoPath = 4,
    Stalled = 5,
    BoardNotFound = 6,
    OpenFailed = 7,
    Cancelled = 8,
}

// Board is the board that opened; it is empty when the window was already open.
internal readonly record struct BoardArrival(BoardArrivalFailure Failure, MarketBoardSighting Board)
{
    public bool Opened => Failure == BoardArrivalFailure.None;

    public static BoardArrival Fail(BoardArrivalFailure failure) => new(failure, default);
}
