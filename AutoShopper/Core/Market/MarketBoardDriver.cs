using AutoShopper.Core.Game.Ops;
using ECommons;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AutoShopper.Core.Market;

internal enum BoardOpenStatus : byte
{
    Idle = 0,
    Opening = 1,
    Open = 2,
    Failed = 3,
}

internal enum BoardOpenFailure : byte
{
    None = 0,
    NotLoggedIn = 1,
    NoBoardNearby = 2,
    OutOfReach = 3,
    BoardLost = 4,
    PlayerBusy = 5,
    WindowTimeout = 6,
}

internal readonly record struct BoardOpenState(BoardOpenStatus Status, BoardOpenFailure Failure, MarketBoardSighting Board);

// Opens the market board window from beside a board and closes every market window before travel. Framework
// thread only: the run task polls it once per frame.
internal sealed unsafe class MarketBoardDriver
{
    public const string ItemSearchAddon = "ItemSearch";
    public const string ItemSearchResultAddon = "ItemSearchResult";
    public const string SelectYesnoAddon = "SelectYesno";

    // Event objects answer an interaction a little beyond 4 y; the walk to a board stops at 2.5 to 3 y anyway.
    public const float InteractReachMeters = 4f;

    private const int WindowTimeoutMs = 8_000;
    // A target set this frame is not yet the game's target for an interaction issued in the same frame.
    private const int TargetSettleMs = 200;
    private const int InteractRetryMs = 2_500;
    private const int MaximumInteractAttempts = 3;
    private const int CloseThrottleMs = 500;
    private const int CancelCallback = -1;
    private const string CloseThrottleKey = "ASHOP.Market.Close";

    private BoardOpenState state;
    private long deadlineMs;
    private long nextInteractAtMs;
    private int interactAttempts;
    private bool sawPlayerBusy;

    public BoardOpenState State => state;

    public static bool IsWithinReach(in MarketBoardSighting board) => board.Distance <= InteractReachMeters;

    public static bool IsSearchWindowReady()
        => GenericHelpers.TryGetAddonByName<AtkUnitBase>(ItemSearchAddon, out var addon) && GenericHelpers.IsAddonReady(addon);

    public static bool AnyMarketWindowOpen()
        => AddonPresent(ItemSearchAddon) || AddonPresent(ItemSearchResultAddon);

    public BoardOpenState BeginOpen()
    {
        interactAttempts = 0;
        sawPlayerBusy = false;
        nextInteractAtMs = 0;
        if (IsSearchWindowReady())
        {
            return state = new BoardOpenState(BoardOpenStatus.Open, BoardOpenFailure.None, default);
        }

        if (Svc.Objects.LocalPlayer is null)
        {
            return Fail(BoardOpenFailure.NotLoggedIn, default);
        }

        if (!MarketBoards.TryFindNearestBoard(out var board))
        {
            return Fail(BoardOpenFailure.NoBoardNearby, default);
        }

        if (!IsWithinReach(board))
        {
            return Fail(BoardOpenFailure.OutOfReach, board);
        }

        deadlineMs = Environment.TickCount64 + WindowTimeoutMs;
        RunLog.Info($"Market: opening the board {board.BaseId} at {board.Distance:F1} y.");
        return state = new BoardOpenState(BoardOpenStatus.Opening, BoardOpenFailure.None, board);
    }

    public BoardOpenState PollOpen()
    {
        if (state.Status != BoardOpenStatus.Opening)
        {
            return state;
        }

        if (IsSearchWindowReady())
        {
            RunLog.Info($"Market: the board window is open after {interactAttempts} interaction(s).");
            return state = state with { Status = BoardOpenStatus.Open };
        }

        var now = Environment.TickCount64;
        if (now >= deadlineMs)
        {
            return Fail(interactAttempts == 0 && sawPlayerBusy ? BoardOpenFailure.PlayerBusy : BoardOpenFailure.WindowTimeout, state.Board);
        }

        // The window is already on its way; interacting again could close it.
        if (AddonPresent(ItemSearchAddon) || now < nextInteractAtMs || interactAttempts >= MaximumInteractAttempts)
        {
            return state;
        }

        if (!NpcInteraction.PlayerReady())
        {
            sawPlayerBusy = true;
            return state;
        }

        var board = Svc.Objects.SearchById(state.Board.GameObjectId);
        if (board is null || !board.IsTargetable || !MarketBoards.IsBoardBaseId(board.BaseId))
        {
            return Fail(BoardOpenFailure.BoardLost, state.Board);
        }

        if (!NpcInteraction.IsTargeted(board))
        {
            NpcInteraction.Target(board);
            nextInteractAtMs = now + TargetSettleMs;
            return state;
        }

        interactAttempts++;
        var result = NpcInteraction.Interact(board);
        nextInteractAtMs = now + InteractRetryMs;
        RunLog.Info($"Market: interaction {interactAttempts} with the board returned {result}.");
        return state;
    }

    public void ResetOpen()
    {
        state = default;
    }

    // One window per call, the purchase prompt first, so each close lands before the next; true once the market
    // windows are gone. A prompt is only answered while a market window is up, so another plugin's is never touched.
    public static bool PollCloseAll()
    {
        if (!AnyMarketWindowOpen())
        {
            return true;
        }

        if (TryCloseByCallback(SelectYesnoAddon) || TryCloseByCallback(ItemSearchResultAddon) || !AddonPresent(ItemSearchAddon))
        {
            return false;
        }

        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>(ItemSearchAddon, out var search)
            && GenericHelpers.IsAddonReady(search)
            && EzThrottler.Throttle(CloseThrottleKey, CloseThrottleMs))
        {
            RunLog.Debug("Market: closing ItemSearch.");
            search->Close(true);
        }

        return false;
    }

    // True while the window is present, whether or not it was closed on this call.
    private static bool TryCloseByCallback(string addonName)
    {
        if (!AddonPresent(addonName))
        {
            return false;
        }

        if (GenericHelpers.TryGetAddonByName<AtkUnitBase>(addonName, out var addon)
            && GenericHelpers.IsAddonReady(addon)
            && EzThrottler.Throttle(CloseThrottleKey, CloseThrottleMs))
        {
            RunLog.Debug($"Market: closing {addonName}.");
            Callback.Fire(addon, true, CancelCallback);
        }

        return true;
    }

    private static bool AddonPresent(string addonName)
    {
        var addon = Svc.GameGui.GetAddonByName(addonName);
        return !addon.IsNull && addon.IsVisible;
    }

    private BoardOpenState Fail(BoardOpenFailure failure, MarketBoardSighting board)
    {
        RunLog.Info($"Market: the board did not open ({failure}{(board.GameObjectId == 0 ? string.Empty : $", board at {board.Distance:F1} y")}).");
        return state = new BoardOpenState(BoardOpenStatus.Failed, failure, board);
    }
}
