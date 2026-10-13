using AutoShopper.Core.Game.Ops;
using AutoShopper.Core.Ipc;
using AutoShopper.Core.Market;
using AutoShopper.Core.Market.Model;
using AutoShopper.Core.Travel;
using Dalamud.Game.ClientState.Conditions;
using ECommons;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoShopper.Core.Tasks;

public abstract partial class AutoCommon
{
    private const int BoardWindowsCloseMs = 5_000;
    private const int BoardWindowsPollFrames = 2;
    private const int BoardTeleportWatchdogMs = 60_000;
    private const int BoardSettleMs = 30_000;
    // Lifestream picks up the aetheryte the character landed beside within a few frames of the load.
    private const int BoardActiveAetheryteSettleMs = 3_000;
    private const int BoardActiveAetheryteWaitMs = 5_000;
    // The menu, the hop and its load take well under this.
    private const int BoardAethernetRideMs = 45_000;
    private const int BoardOpenPollFrames = 1;
    // A ride sets the character down beside the stop, and a stop's map position is a few metres off at most.
    private const float BoardAethernetArrivalMeters = 25f;
    // Where a player stands to use a board, well inside the 4 m interaction reach.
    private const float BoardStandOffMeters = 2.5f;
    private const float BoardWalkArriveWithinMeters = BoardStandOffMeters - WalkArrivalSlackMeters;
    // Lifestream takes a shard as the one in use only from a few metres out.
    private const float BoardNodeStandOffMeters = 3.5f;
    private const float BoardNodeArriveWithinMeters = BoardNodeStandOffMeters - WalkArrivalSlackMeters;
    private const float BoardNodeMatchMeters = 10f;
    // The second walk heads for the board object itself, in case the catalog's position is off.
    private const int MaxBoardWalks = 2;

    // Leaves the board's window open. The caller picks the city, usually through MarketCityChoice.
    private protected async Task<BoardArrival> GoToMarketBoard(MarketCity city, string scope)
    {
        var place = MarketBoardCatalog.Of(city);
        if (BoardHereServes(place) && (MarketBoardDriver.IsSearchWindowReady() || BoardInReach(out _)))
        {
            Diag($"{scope}: already beside a market board");
            return await OpenBoard(scope);
        }

        Diag($"{scope}: going to a market board in {place.Name} from {TerritoryNames.Of(Svc.ClientState.TerritoryType)} ({ConditionTag()})");
        await CloseMarketWindows(scope);
        if (!await WaitForPlayerReady())
        {
            return BoardUnreached(BoardArrivalFailure.Stalled, scope);
        }

        if (Svc.ClientState.TerritoryType != place.TerritoryId)
        {
            var entry = await EnterBoardTerritory(place, scope);
            if (entry != BoardArrivalFailure.None)
            {
                return BoardUnreached(entry, scope);
            }
        }

        var walk = await WalkToBoard(place, scope);
        if (walk != BoardArrivalFailure.None)
        {
            return BoardUnreached(walk, scope);
        }

        return await OpenBoard(scope);
    }

    private protected async Task<BoardOpenState> OpenMarketBoard(MarketBoardDriver driver)
    {
        var state = driver.BeginOpen();
        while (state.Status == BoardOpenStatus.Opening)
        {
            if (CancelToken.IsCancellationRequested)
            {
                driver.ResetOpen();
                return driver.State;
            }

            await NextFrame(BoardOpenPollFrames);
            state = driver.PollOpen();
        }

        return state;
    }

    // A board in another catalog city is not the one asked for; one outside the catalog, such as a housing ward's,
    // serves as well as any.
    private static bool BoardHereServes(in MarketBoardCity place)
    {
        var territoryId = Svc.ClientState.TerritoryType;
        return territoryId == place.TerritoryId || !MarketBoardCatalog.TryFindCity(territoryId, out _);
    }

    private static bool BoardInReach(out MarketBoardSighting board)
        => MarketBoards.TryFindNearestBoard(out board) && MarketBoardDriver.IsWithinReach(board);

    private protected async Task CloseMarketWindows(string scope)
    {
        if (!MarketBoardDriver.AnyMarketWindowOpen())
        {
            return;
        }

        Diag($"{scope}: closing the market windows before leaving");
        var deadline = Environment.TickCount64 + BoardWindowsCloseMs;
        while (!MarketBoardDriver.PollCloseAll())
        {
            if (Environment.TickCount64 >= deadline)
            {
                Warn($"{scope}: the market windows did not close within {BoardWindowsCloseMs / TimeUnits.MillisecondsPerSecond}s; leaving anyway");
                return;
            }

            await NextFrame(BoardWindowsPollFrames);
        }
    }

    private async Task<BoardArrivalFailure> EnterBoardTerritory(MarketBoardCity place, string scope)
    {
        var inHub = Svc.ClientState.TerritoryType == place.TeleportTerritoryId;
        if (!inHub && !ZoneAetherytes.IsAttuned(place.TeleportAetheryteId))
        {
            Warn($"{scope}: the aetheryte of {place.Name} ({place.TeleportAetheryteId}) is not attuned on this character");
            return BoardArrivalFailure.NotAttuned;
        }

        var hasNode = TryFindApproachNode(place, out var node);
        if (place.EntersThroughAethernet && !hasNode)
        {
            Warn($"{scope}: no aethernet shard in {place.Name} is attuned on this character, so the aethernet cannot reach its boards");
            return BoardArrivalFailure.NotAttuned;
        }

        if (!inHub)
        {
            var teleport = await TeleportToBoardCity(place, scope);
            if (teleport != BoardArrivalFailure.None)
            {
                return teleport;
            }
        }

        if (place.EntersThroughAethernet)
        {
            return await RideAethernet(node, place.TerritoryId, scope);
        }

        if (!hasNode || !AethernetSavesWalking(place, node))
        {
            return BoardArrivalFailure.None;
        }

        // The ride only saves walking here, so a failed one leaves the walk from wherever the character stands.
        var ride = await RideAethernet(node, place.TerritoryId, scope);
        if (ride == BoardArrivalFailure.None || Svc.ClientState.TerritoryType == place.TerritoryId)
        {
            return BoardArrivalFailure.None;
        }

        return ride;
    }

    private async Task<BoardArrivalFailure> TeleportToBoardCity(MarketBoardCity place, string scope)
    {
        if (!ZoneAetherytes.TryFindById(place.TeleportAetheryteId, out _, out var aetheryte))
        {
            Warn($"{scope}: aetheryte {place.TeleportAetheryteId} of {place.Name} is not a teleport target");
            return BoardArrivalFailure.TeleportFailed;
        }

        var label = $"{scope}-teleport";
        var reached = false;
        await RunWithStatusPinned($"Teleporting to {aetheryte.Name}", async () =>
        {
            if (await HumanDelay(HumanAction.Teleport, label))
            {
                reached = await TeleportToTerritory(place.TeleportTerritoryId, aetheryte.Position, label, BoardTeleportWatchdogMs);
            }
        });

        if (!reached)
        {
            if (!CancelToken.IsCancellationRequested)
            {
                Warn($"{scope}: could not teleport to {aetheryte.Name} (still in territory {Svc.ClientState.TerritoryType})");
            }

            return BoardArrivalFailure.TeleportFailed;
        }

        if (await WaitUntilTimed(ArrivalSettled, BoardSettleMs, $"{label}-settle"))
        {
            return BoardArrivalFailure.None;
        }

        if (!CancelToken.IsCancellationRequested)
        {
            Warn($"{scope}: the screen did not settle after the teleport to {aetheryte.Name} ({ConditionTag()})");
        }

        return BoardArrivalFailure.TeleportFailed;
    }

    // The catalog's stop when it is attuned, otherwise the attuned stop nearest the city's first board.
    private static bool TryFindApproachNode(in MarketBoardCity place, out ZoneAetheryte node)
    {
        if (ZoneAetherytes.TryFindNode(place.TerritoryId, place.ApproachNodeId, out node) && ZoneAetherytes.IsAttuned(node.Id))
        {
            return true;
        }

        return ZoneAetherytes.TryFindNearestUsableNode(place.TerritoryId, place.BoardPositions[0], out node);
    }

    private static bool AethernetSavesWalking(in MarketBoardCity place, ZoneAetheryte node)
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return false;
        }

        var here = player.Position;
        var walk = GroundDistance.Between(here, MarketBoardCatalog.NearestBoard(place, here));
        var ride = GroundDistance.Between(node.Position, MarketBoardCatalog.NearestBoard(place, node.Position)) + ZoneAetherytes.AethernetOverheadMeters;
        return ride < walk;
    }

    private async Task<BoardArrivalFailure> RideAethernet(ZoneAetheryte node, uint territoryId, string scope)
    {
        var lifestream = LifestreamIPC.Instance;
        if (!lifestream.IsAvailable)
        {
            Warn($"{scope}: Lifestream is not loaded, so the aethernet to {node.Name} cannot be ridden");
            return BoardArrivalFailure.AethernetFailed;
        }

        Status = $"Riding the aethernet to {node.Name}";
        if (!await StandAtAethernet(scope))
        {
            return BoardArrivalFailure.AethernetFailed;
        }

        if (lifestream.IsBusy() && !await WaitUntilTimed(static () => !LifestreamIPC.Instance.IsBusy(), LifestreamBusyBeforeHopMs, $"{scope}-lifestream-idle"))
        {
            if (!CancelToken.IsCancellationRequested)
            {
                Warn($"{scope}: Lifestream stayed busy with something else, so the aethernet ride to {node.Name} was not requested");
            }

            return BoardArrivalFailure.AethernetFailed;
        }

        var from = lifestream.ActiveAetheryte();
        if (!lifestream.AethernetTeleportById(node.Id))
        {
            Warn($"{scope}: Lifestream refused the aethernet ride from aetheryte {from} to {node.Name} ({node.Id})");
            return BoardArrivalFailure.AethernetFailed;
        }

        // Lifestream was idle before the request, so busy now means the ride is this trip's to abort; otherwise it is not.
        var owned = lifestream.IsBusy();
        Diag($"{scope}: riding the aethernet from aetheryte {from} to {node.Name} ({node.Id}){(owned ? string.Empty : "; Lifestream does not show busy, so the ride is not this trip's to abort")}");
        bool arrived;
        using (owned ? CancelToken.Register(AbortLifestreamIfBusy) : default)
        {
            arrived = await WaitForAethernetArrival(node, territoryId, scope);
        }

        if (arrived)
        {
            await WaitForNavmeshReady(TravelNavmeshWaitMs, TravelNavmeshPollFrames);
            return BoardArrivalFailure.None;
        }

        if (owned && lifestream.IsBusy())
        {
            Diag($"{scope}: aborting the aethernet ride this trip requested");
            lifestream.Abort();
        }

        return BoardArrivalFailure.AethernetFailed;
    }

    // Lifestream rides the aethernet only from beside an aetheryte or a shard it sees as the one in use.
    private async Task<bool> StandAtAethernet(string scope)
    {
        if (await WaitUntilTimed(StandsAtAethernet, BoardActiveAetheryteSettleMs, $"{scope}-aethernet-in-reach"))
        {
            return true;
        }

        var territoryId = Svc.ClientState.TerritoryType;
        if (CancelToken.IsCancellationRequested
            || Svc.Objects.LocalPlayer is not { } player
            || !ZoneAetherytes.TryFindNearestUsableNode(territoryId, player.Position, out var stop))
        {
            if (!CancelToken.IsCancellationRequested)
            {
                Warn($"{scope}: no attuned aetheryte or shard to board the aethernet at in {TerritoryNames.Of(territoryId)}");
            }

            return false;
        }

        var target = LiveNodePosition(stop.Position);
        Diag($"{scope}: walking {DistanceTo(target):F0}m to {stop.Name} to board the aethernet");
        if (!await TravelTo(territoryId, target, BoardNodeArriveWithinMeters))
        {
            if (!CancelToken.IsCancellationRequested)
            {
                Warn($"{scope}: could not reach {stop.Name} to board the aethernet");
            }

            return false;
        }

        if (await WaitUntilTimed(StandsAtAethernet, BoardActiveAetheryteWaitMs, $"{scope}-aethernet-in-reach"))
        {
            return true;
        }

        if (!CancelToken.IsCancellationRequested)
        {
            Warn($"{scope}: Lifestream does not see {stop.Name} as in reach from {DistanceTo(target):F1}m");
        }

        return false;
    }

    private async Task<bool> WaitForAethernetArrival(ZoneAetheryte node, uint territoryId, string scope)
    {
        var lifestream = LifestreamIPC.Instance;
        var startedAt = Environment.TickCount64;
        var deadline = startedAt + BoardAethernetRideMs;
        var idleSinceMs = startedAt;
        var sawBusy = false;
        while (!CancelToken.IsCancellationRequested)
        {
            var now = Environment.TickCount64;
            var busy = lifestream.IsBusy();
            if (busy)
            {
                sawBusy = true;
                idleSinceMs = now;
            }
            else if (AtAethernetStop(node, territoryId))
            {
                Diag($"{scope}: reached {node.Name} after {(now - startedAt) / TimeUnits.MillisecondsPerSecond}s");
                return true;
            }
            else if (sawBusy && PlayerReady() && now - idleSinceMs >= HopAbandonedIdleMs)
            {
                Warn($"{scope}: Lifestream went idle without reaching {node.Name} ({TerritoryNames.Of(Svc.ClientState.TerritoryType)}, {ConditionTag()})");
                return false;
            }

            if (now >= deadline)
            {
                Warn($"{scope}: {node.Name} not reached within {BoardAethernetRideMs / TimeUnits.MillisecondsPerSecond}s (Lifestream busy {busy}, {TerritoryNames.Of(Svc.ClientState.TerritoryType)}, {ConditionTag()})");
                return false;
            }

            await DelayMs(HopPollMs);
        }

        return false;
    }

    private async Task<BoardArrivalFailure> WalkToBoard(MarketBoardCity place, string scope)
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return BoardArrivalFailure.Stalled;
        }

        var target = FirstBoardTarget(place, player.Position);
        for (var walk = 1; ; walk++)
        {
            if (BoardInReach(out _))
            {
                return BoardArrivalFailure.None;
            }

            await WaitForNavmeshReady(TravelNavmeshWaitMs, TravelNavmeshPollFrames);
            if (await RouteFallsShort(target, scope))
            {
                return BoardArrivalFailure.NoPath;
            }

            Diag($"{scope}: walking {DistanceTo(target):F0}m to the board at {FormatPosition(target)} in {place.Name}");
            var arrived = await TravelTo(place.TerritoryId, target, BoardWalkArriveWithinMeters);
            if (CancelToken.IsCancellationRequested)
            {
                return BoardArrivalFailure.Cancelled;
            }

            if (BoardInReach(out _))
            {
                return BoardArrivalFailure.None;
            }

            if (!arrived)
            {
                Warn($"{scope}: the walk ended {DistanceTo(target):F1}m short of the board at {FormatPosition(target)}");
                return BoardArrivalFailure.Stalled;
            }

            if (!MarketBoards.TryFindNearestBoard(out var live))
            {
                Warn($"{scope}: no market board in sight at {FormatPosition(target)} in {place.Name}");
                return BoardArrivalFailure.BoardNotFound;
            }

            if (walk >= MaxBoardWalks)
            {
                Warn($"{scope}: the board in sight stays {live.Distance:F1}m away, out of reach");
                return BoardArrivalFailure.Stalled;
            }

            Diag($"{scope}: the board in sight stands {live.Distance:F1}m away at {FormatPosition(live.Position)}; walking to it");
            target = live.Position;
        }
    }

    // Once a board object is in sight its own position beats the catalog's, unless a catalog board is nearer.
    private static Vector3 FirstBoardTarget(in MarketBoardCity place, Vector3 from)
    {
        var listed = MarketBoardCatalog.NearestBoard(place, from);
        return MarketBoards.TryFindNearestBoard(out var live) && live.Distance <= Vector3.Distance(from, listed) ? live.Position : listed;
    }

    private async Task<bool> RouteFallsShort(Vector3 target, string scope)
    {
        if (Svc.Objects.LocalPlayer is not { } player)
        {
            return false;
        }

        var route = await ProbeGroundRoute(player.Position, target);
        if (route.Kind != GroundRouteKind.Partial || route.ShortfallMeters <= MarketBoardDriver.InteractReachMeters)
        {
            return false;
        }

        Warn($"{scope}: the ground route ends {route.ShortfallMeters:F0}m short of the board at {FormatPosition(target)}");
        return true;
    }

    private async Task<BoardArrival> OpenBoard(string scope)
    {
        if (Svc.Condition[ConditionFlag.Mounted])
        {
            await SafeDismount($"{scope}-dismount");
        }

        Status = "Opening the market board";
        var state = await OpenMarketBoard(Plugin.Instance.Market.Driver);
        if (state.Status == BoardOpenStatus.Open)
        {
            Diag($"{scope}: the market board is open");
            return new BoardArrival(BoardArrivalFailure.None, state.Board);
        }

        if (!CancelToken.IsCancellationRequested)
        {
            Warn($"{scope}: the market board did not open ({state.Failure}; {NpcInteraction.DescribeBlockers()})");
        }

        return BoardUnreached(BoardArrivalFailure.OpenFailed, scope);
    }

    private BoardArrival BoardUnreached(BoardArrivalFailure failure, string scope)
    {
        var reason = CancelToken.IsCancellationRequested ? BoardArrivalFailure.Cancelled : failure;
        Diag($"{scope}: no market board opened ({reason})");
        return BoardArrival.Fail(reason);
    }

    // Registered only while a ride this trip requested is under way, so the abort never lands on someone else's travel.
    private static void AbortLifestreamIfBusy()
    {
        var lifestream = LifestreamIPC.Instance;
        if (lifestream.IsBusy())
        {
            lifestream.Abort();
        }
    }

    private static bool StandsAtAethernet() => LifestreamIPC.Instance.ActiveAetheryte() != 0;

    private static bool ArrivalSettled() => PlayerReady() && GenericHelpers.IsScreenReady();

    private static bool AtAethernetStop(ZoneAetheryte node, uint territoryId)
        => Svc.ClientState.TerritoryType == territoryId
        && ArrivalSettled()
        && Svc.Objects.LocalPlayer is { } player
        && GroundDistance.Between(player.Position, node.Position) <= BoardAethernetArrivalMeters;

    private static Vector3 LiveNodePosition(Vector3 mapPosition)
        => FindNearestAetheryteObject(mapPosition) is { } live && GroundDistance.Between(live.Position, mapPosition) <= BoardNodeMatchMeters
            ? live.Position
            : mapPosition;
}
