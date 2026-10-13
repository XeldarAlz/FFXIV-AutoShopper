using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Game.Ops;
using AutoShopper.Core.Ipc;
using AutoShopper.Core.Travel;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using System.Numerics;
using System.Threading.Tasks;

namespace AutoShopper.Core.Tasks;

public abstract partial class AutoCommon
{
    // A same data center world change gets this long, congestion included.
    private const int WorldChangeBudgetMs = 720_000;
    // Lifestream retries a data center transfer through congestion for up to an hour before it gives up itself.
    private const int DataCenterTransferBudgetMs = 65 * 60_000;
    // A hop Lifestream accepted shows as busy within a frame; one it refused, such as a transfer with data center travel
    // disabled in its settings, never does.
    private const int HopStartWindowMs = 30_000;
    // Lifestream idle on the wrong world this long after a hop started means it gave the hop up.
    private const int HopAbandonedIdleMs = 5_000;
    private const int HopPollMs = 500;
    private const int HopProgressLogMs = 15_000;
    private const int LifestreamBusyBeforeHopMs = 30_000;
    private const int LifestreamIdleWindowMs = 3_000;
    private const int LifestreamIdleBudgetMs = 60_000;
    private const int JourneyTeleportWatchdogMs = 60_000;
    private const float AtAetheryteMeters = 20f;
    // Lifestream switches instances at an aetheryte within 11 metres; the walk stops well inside that.
    private const float InstanceAetheryteReachMeters = 4f;
    // Right after a zone load Lifestream still reads the character as occupied for a moment.
    private const int InstanceSwitcherSettleMs = 8_000;
    private const int InstanceSwitcherReadyMs = 10_000;
    private const int InstanceChangeAttempts = 3;
    private const int InstanceChangeTimeoutMs = 30_000;
    private const int InstanceRetryBackoffMs = 5_000;
    private const int JourneyPollFrames = 10;
    private const string JourneyLabel = "Journey";
    private const string ReturnLabel = "Journey home";
    private const string NoWorld = "none";

    // When the hop under way was asked for, so a congestion message from an earlier trip is not taken for this one.
    private long hopRequestedAtMs;

    protected Task<JourneyOutcome> TravelHome()
    {
        if (Worlds.TryHome(out var home))
        {
            return TravelToWorld(JourneyPlan.Home(home.Name));
        }

        return Task.FromResult(Refuse(ReturnLabel, "the home world could not be read from the character"));
    }

    // Everything needed to stand at a named aetheryte on a named world and instance, from anywhere in the region. The
    // plan stays in the configuration until the journey ends, so a login can pick it up when the task does not live
    // through the relog a data center transfer brings; a resumed journey never asks for that transfer again.
    // A data center transfer gives up at the earlier of its own budget and giveUpAtUtc, when one is given.
    protected async Task<JourneyOutcome> TravelToWorld(JourneyPlan plan, bool resumed = false, DateTime? giveUpAtUtc = null)
    {
        JourneyOutcome outcome;
        try
        {
            outcome = await RunJourney(plan, resumed, plan.ReturnTrip ? ReturnLabel : JourneyLabel, giveUpAtUtc);
        }
        finally
        {
            TravelOwnership.Release(TravelOwner);
        }

        if (outcome != JourneyOutcome.Cancelled)
        {
            Plugin.Instance.Configuration.ClearPendingJourney();
        }

        return outcome;
    }

    // Who a world change this task asks Lifestream for belongs to, so a Stop aborts only travel of its own.
    protected virtual object TravelOwner => this;

    // A task that must carry on after the relog a data center transfer brings returns what the login should check.
    private protected virtual TransferContinuation? ContinuationFor(in JourneyPlan plan, in WorldInfo target) => null;

    private async Task<JourneyOutcome> RunJourney(JourneyPlan plan, bool resumed, string label, DateTime? giveUpAtUtc)
    {
        if (!LifestreamIPC.Instance.IsAvailable)
        {
            return Refuse(label, "Lifestream is not loaded; install or enable it from the Plugins page");
        }

        if (!Worlds.TryFind(plan.World, out var target))
        {
            return Refuse(label, $"'{plan.World}' is not a world this client knows");
        }

        if (!await WaitForPlayerReady())
        {
            return Unreached(JourneyOutcome.WorldUnreached);
        }

        if (!Worlds.TryCurrent(out var current))
        {
            return Refuse(label, "the current world could not be read from the character");
        }

        if (!Worlds.SameRegion(current, target))
        {
            return Refuse(label, $"{target.Name} is in {Worlds.RegionLabel(target.Region)} and the character is in {Worlds.RegionLabel(current.Region)}; travel stays within a region");
        }

        Diag($"{label}: {current.Name} ({current.DataCenterName}) to {target.Name} ({target.DataCenterName}), aetheryte {plan.AetheryteId}, instance {plan.Instance}{(resumed ? ", resumed after login" : string.Empty)}");
        if (current.Id == target.Id)
        {
            Diag($"{label}: already on {target.Name}");
        }
        else
        {
            var hop = await HopToWorld(current, target, plan, resumed, label, giveUpAtUtc);
            if (hop != JourneyOutcome.Arrived)
            {
                return hop;
            }
        }

        if (plan.AetheryteId != 0 && !await TravelToPlannedAetheryte(plan.AetheryteId, label))
        {
            return Unreached(JourneyOutcome.AetheryteUnreached);
        }

        if (plan.Instance > 0 && !await SwitchToInstance(plan.Instance, label))
        {
            return Unreached(JourneyOutcome.InstanceUnreached);
        }

        Status = plan.ReturnTrip ? "Back home" : $"Arrived on {target.Name}";
        Diag($"{label}: complete on {target.Name} in {TerritoryNames.Of(Svc.ClientState.TerritoryType)}, instance {LifestreamIPC.Instance.CurrentInstance()} ({ConditionTag()})");
        return JourneyOutcome.Arrived;
    }

    private async Task<JourneyOutcome> HopToWorld(WorldInfo current, WorldInfo target, JourneyPlan plan, bool resumed, string label, DateTime? giveUpAtUtc)
    {
        var lifestream = LifestreamIPC.Instance;
        var crossDataCenter = !Worlds.SameDataCenter(current, target);
        var kind = crossDataCenter ? "data center transfer" : "world change";
        var transferDeadlineUtc = crossDataCenter ? giveUpAtUtc : null;
        if (transferDeadlineUtc is { } deadlineUtc && DateTime.UtcNow >= deadlineUtc)
        {
            return AbandonTransfer(target, label);
        }

        hopRequestedAtMs = Environment.TickCount64;
        var reachable = crossDataCenter ? lifestream.CanVisitCrossDataCenter(target.Name) : lifestream.CanVisitSameDataCenter(target.Name);
        if (!reachable)
        {
            return Refuse(label, $"Lifestream cannot take the character from {current.Name} to {target.Name} by {kind}");
        }

        if (resumed)
        {
            if (!lifestream.IsBusy())
            {
                Warn($"{label}: resumed on {current.Name} with Lifestream idle, so the {kind} to {target.Name} did not go through; giving up");
                Svc.Chat.PrintError($"{AshopConstants.LogPrefix} The trip to {target.Name} did not go through; the character is on {current.Name}.");
                return JourneyOutcome.WorldUnreached;
            }

            Diag($"{label}: resumed on {current.Name} with Lifestream still busy; waiting for it to reach {target.Name}");
        }
        else
        {
            if (crossDataCenter && !await PrepareDataCenterTransfer(target, label))
            {
                return Unreached(JourneyOutcome.Blocked);
            }

            var start = await StartHop(target, crossDataCenter, plan, kind, label);
            if (start != JourneyOutcome.Arrived)
            {
                return Unreached(start);
            }
        }

        var budgetMs = crossDataCenter ? DataCenterTransferBudgetMs : WorldChangeBudgetMs;
        var arrival = await WaitForWorldArrival(target, budgetMs, transferDeadlineUtc, kind, label);
        if (arrival != JourneyOutcome.Arrived)
        {
            return Unreached(arrival);
        }

        await WaitForLifestreamIdle(label);
        if (!await WaitForPlayerReady())
        {
            return Unreached(JourneyOutcome.WorldUnreached);
        }

        Diag($"{label}: on {target.Name} in {TerritoryNames.Of(Svc.ClientState.TerritoryType)} ({ConditionTag()})");
        return JourneyOutcome.Arrived;
    }

    // Arrived here means the hop started; the arrival itself is waited for afterwards.
    private async Task<JourneyOutcome> StartHop(WorldInfo target, bool crossDataCenter, JourneyPlan plan, string kind, string label)
    {
        var lifestream = LifestreamIPC.Instance;
        await PrepareForTeleport($"{label}-prepare");
        if (CancelToken.IsCancellationRequested)
        {
            return JourneyOutcome.Cancelled;
        }

        if (lifestream.IsBusy())
        {
            Diag($"{label}: Lifestream is busy; waiting up to {LifestreamBusyBeforeHopMs / TimeUnits.MillisecondsPerSecond}s for it before the {kind}");
            Status = "Waiting for Lifestream";
            if (!await WaitUntilTimed(static () => !LifestreamIPC.Instance.IsBusy(), LifestreamBusyBeforeHopMs, $"{label}-lifestream-idle"))
            {
                if (!CancelToken.IsCancellationRequested)
                {
                    Warn($"{label}: Lifestream stayed busy, so the {kind} to {target.Name} was not requested");
                    Svc.Chat.PrintError($"{AshopConstants.LogPrefix} Lifestream is busy with something else; the trip to {target.Name} was not started.");
                    return JourneyOutcome.LifestreamBusy;
                }

                return JourneyOutcome.Cancelled;
            }
        }

        if (crossDataCenter)
        {
            // Written before the request, because Lifestream logs the character out to make the transfer.
            if (ContinuationFor(plan, target) is { } continuation)
            {
                Plugin.Instance.Configuration.SetPendingJourney(plan, continuation);
            }
            else
            {
                Plugin.Instance.Configuration.SetPendingJourney(plan);
            }
        }

        Status = crossDataCenter ? $"Travelling to {target.Name} on {target.DataCenterName}" : $"Changing world to {target.Name}";
        Diag($"{label}: asking Lifestream for a {kind} to {target.Name}{(plan.Gateway > 0 ? $" through gateway {plan.Gateway}" : string.Empty)} ({ConditionTag()})");
        hopRequestedAtMs = Environment.TickCount64;
        lifestream.TeleportAndChangeWorld(target.Name, crossDataCenter, plan.Gateway);
        if (await WaitUntilTimed(() => HopStarted(target.Id), HopStartWindowMs, $"{label}-hop-start", JourneyPollFrames))
        {
            // Lifestream was idle right before the request, so busy now is this task's own operation.
            if (lifestream.IsBusy())
            {
                TravelOwnership.Claim(TravelOwner);
            }

            return JourneyOutcome.Arrived;
        }

        if (CancelToken.IsCancellationRequested)
        {
            return JourneyOutcome.Cancelled;
        }

        if (crossDataCenter)
        {
            Warn($"{label}: Lifestream showed no sign of starting the data center transfer to {target.Name} within {HopStartWindowMs / TimeUnits.MillisecondsPerSecond}s; data center travel is most likely disabled in its settings");
            Svc.Chat.PrintError($"{AshopConstants.LogPrefix} Lifestream did not start the trip to {target.Name}. Enable data center travel in Lifestream's settings (/li) and try again.");
        }
        else
        {
            Warn($"{label}: Lifestream showed no sign of starting the world change to {target.Name} within {HopStartWindowMs / TimeUnits.MillisecondsPerSecond}s");
            Svc.Chat.PrintError($"{AshopConstants.LogPrefix} Lifestream did not start the world change to {target.Name}. The log has the details.");
        }

        return JourneyOutcome.NotStarted;
    }

    // The game refuses a data center transfer to a party member, so a party is left first rather than ending the
    // journey; anything else the relog would tear down refuses the transfer. False has already said why.
    private async Task<bool> PrepareDataCenterTransfer(WorldInfo target, string label)
    {
        if (PartyOps.InParty())
        {
            Diag($"{label}: in a party before the data center transfer to {target.Name}; leaving it, since the game refuses the transfer otherwise");
            await LeaveParty($"{label}-leave-party");
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }
        }

        if (TransferGuard.Blocker() is not { } reason)
        {
            return true;
        }

        Warn($"{label}: the data center transfer to {target.Name} was not started: {reason}");
        Svc.Chat.PrintError($"{AshopConstants.LogPrefix} The trip to {target.Name} on {target.DataCenterName} was not started: {reason}.");
        return false;
    }

    private static bool HopStarted(uint targetWorldId)
        => LifestreamIPC.Instance.IsBusy()
        || !Svc.ClientState.IsLoggedIn
        || OnWorld(Svc.Objects.LocalPlayer, targetWorldId);

    private async Task<JourneyOutcome> WaitForWorldArrival(WorldInfo target, int budgetMs, DateTime? transferDeadlineUtc, string kind, string label)
    {
        var lifestream = LifestreamIPC.Instance;
        var startedAt = Environment.TickCount64;
        var deadline = startedAt + budgetMs;
        var transferDeadline = transferDeadlineUtc is { } deadlineUtc ? startedAt + Math.Max(0L, (long)(deadlineUtc - DateTime.UtcNow).TotalMilliseconds) : long.MaxValue;
        var nextLogAt = startedAt + HopProgressLogMs;
        var idleSinceMs = startedAt;
        var loggedOut = false;
        var congestion = Plugin.Instance.Congestion;
        while (!CancelToken.IsCancellationRequested)
        {
            var now = Environment.TickCount64;
            var loggedIn = Svc.ClientState.IsLoggedIn;
            var player = Svc.Objects.LocalPlayer;
            if (!loggedIn && !loggedOut)
            {
                loggedOut = true;
                Diag($"{label}: logged out for the {kind}");
            }

            if (loggedIn && OnWorld(player, target.Id) && !Svc.Condition[ConditionFlag.BetweenAreas] && !Svc.Condition[ConditionFlag.BetweenAreas51])
            {
                Diag($"{label}: arrived on {target.Name} after {(now - startedAt) / TimeUnits.MillisecondsPerSecond}s");
                return JourneyOutcome.Arrived;
            }

            if (loggedIn && congestion.SeenSince(hopRequestedAtMs))
            {
                return TurnedAway(target, kind, label);
            }

            var busy = lifestream.IsBusy();
            if (busy || !loggedIn)
            {
                idleSinceMs = now;
            }
            else if (now - idleSinceMs >= HopAbandonedIdleMs)
            {
                Warn($"{label}: Lifestream went idle on {WorldNameOf(player)} without reaching {target.Name}; the {kind} was abandoned ({ConditionTag()})");
                Svc.Chat.PrintError($"{AshopConstants.LogPrefix} Lifestream gave up the trip to {target.Name}; the character is on {WorldNameOf(player)}. The log has the details.");
                return JourneyOutcome.WorldUnreached;
            }

            if (now >= transferDeadline)
            {
                return AbandonTransfer(target, label);
            }

            if (now >= deadline)
            {
                Warn($"{label}: {target.Name} not reached within {budgetMs / TimeUnits.MillisecondsPerMinute} minutes (logged in {loggedIn}, world {WorldNameOf(player)}, Lifestream busy {busy})");
                Svc.Chat.PrintError($"{AshopConstants.LogPrefix} The trip to {target.Name} did not complete in time.");
                TravelOwnership.AbortIfOwnedBy(TravelOwner, $"{label}: the trip to {target.Name} ran out of time");
                return JourneyOutcome.TimedOut;
            }

            if (now >= nextLogAt)
            {
                nextLogAt = now + HopProgressLogMs;
                Status = loggedIn ? $"Waiting to arrive on {target.Name}" : $"Logging back in on {target.Name}";
                Diag($"{label}: waiting for {target.Name}: logged in {loggedIn}, world {WorldNameOf(player)}, Lifestream busy {busy}, {ConditionTag()}");
            }

            await DelayMs(HopPollMs);
        }

        return JourneyOutcome.Cancelled;
    }

    // The world turned the character away. Lifestream may still be retrying it, so its operation is stopped when it is
    // this task's own, and the world list left open is closed; the run then plans around the world.
    private JourneyOutcome TurnedAway(WorldInfo target, string kind, string label)
    {
        Warn($"{label}: the game reported {target.Name} congested during the {kind} ({ConditionTag()})");
        Svc.Chat.PrintError($"{AshopConstants.LogPrefix} {target.Name} is congested; the trip there is given up.");
        TravelOwnership.AbortIfOwnedBy(TravelOwner, $"{label}: {target.Name} is congested");
        WorldTravelWindow.CloseIfOpen();
        return JourneyOutcome.Congested;
    }

    // Lifestream is told to stop, so a transfer still in its queue does not carry the character off after the journey.
    private JourneyOutcome AbandonTransfer(WorldInfo target, string label)
    {
        var lifestream = LifestreamIPC.Instance;
        var busy = lifestream.IsBusy();
        Warn($"{label}: the journey to {target.Name} is past its deadline and the data center transfer is still underway; abandoning it (logged in {Svc.ClientState.IsLoggedIn}, Lifestream busy {busy})");
        Svc.Chat.PrintError($"{AshopConstants.LogPrefix} The trip to {target.Name} ran past its deadline before it finished; the trip is abandoned.");
        if (busy)
        {
            lifestream.Abort();
        }

        return JourneyOutcome.Overdue;
    }

    // Lifestream goes busy again briefly after an arrival, so the next step waits for a quiet spell.
    private async Task WaitForLifestreamIdle(string label)
    {
        var lifestream = LifestreamIPC.Instance;
        var deadline = Environment.TickCount64 + LifestreamIdleBudgetMs;
        var idleSinceMs = Environment.TickCount64;
        Status = "Settling in after the world change";
        while (!CancelToken.IsCancellationRequested && Environment.TickCount64 < deadline)
        {
            var now = Environment.TickCount64;
            if (lifestream.IsBusy())
            {
                idleSinceMs = now;
            }
            else if (now - idleSinceMs >= LifestreamIdleWindowMs)
            {
                return;
            }

            await DelayMs(HopPollMs);
        }

        if (!CancelToken.IsCancellationRequested)
        {
            Diag($"{label}: Lifestream stayed busy for {LifestreamIdleBudgetMs / TimeUnits.MillisecondsPerSecond}s after the arrival; carrying on");
        }
    }

    private async Task<bool> TravelToPlannedAetheryte(uint aetheryteId, string label)
    {
        if (!ZoneAetherytes.TryFindById(aetheryteId, out var territoryId, out var aetheryte))
        {
            Warn($"{label}: aetheryte {aetheryteId} is not a teleport target; giving up");
            Svc.Chat.PrintError($"{AshopConstants.LogPrefix} Aetheryte {aetheryteId} is not a teleport target.");
            return false;
        }

        var zoneName = TerritoryNames.Of(territoryId);
        if (!ZoneAetherytes.IsAttuned(aetheryteId))
        {
            Warn($"{label}: {aetheryte.Name} in {zoneName} is not attuned on this character; giving up");
            Svc.Chat.PrintError($"{AshopConstants.LogPrefix} {aetheryte.Name} is not attuned on this character.");
            return false;
        }

        if (Svc.ClientState.TerritoryType == territoryId && WithinReach(aetheryte.Position, AtAetheryteMeters))
        {
            Diag($"{label}: already at {aetheryte.Name} in {zoneName}");
            return true;
        }

        if (!await WaitForPlayerReady())
        {
            return false;
        }

        Diag($"{label}: teleporting to {aetheryte.Name} in {zoneName} from territory {Svc.ClientState.TerritoryType} ({ConditionTag()})");
        var reached = false;
        if (Svc.ClientState.TerritoryType == territoryId)
        {
            reached = await TeleportToAetheryte(territoryId, aetheryte, $"{label}-aetheryte");
        }
        else
        {
            await RunWithStatusPinned(
                $"Teleporting to {aetheryte.Name}",
                async () => reached = await TeleportToTerritory(territoryId, aetheryte.Position, $"{label}-teleport", JourneyTeleportWatchdogMs));
        }

        if (!reached)
        {
            if (!CancelToken.IsCancellationRequested)
            {
                Warn($"{label}: could not reach {aetheryte.Name} in {zoneName} (still in territory {Svc.ClientState.TerritoryType})");
                Svc.Chat.PrintError($"{AshopConstants.LogPrefix} Could not teleport to {aetheryte.Name}. The log has the details.");
            }

            return false;
        }

        Diag($"{label}: standing {DistanceTo(aetheryte.Position):F0}m from {aetheryte.Name} in {zoneName} ({ConditionTag()})");
        return true;
    }

    private protected async Task<bool> SwitchToInstance(int instance, string label)
    {
        var lifestream = LifestreamIPC.Instance;
        var zoneName = TerritoryNames.Of(Svc.ClientState.TerritoryType);
        var count = lifestream.NumberOfInstances();
        if (count == 0)
        {
            Diag($"{label}: {zoneName} has no instances Lifestream knows of; staying where the teleport landed");
            return true;
        }

        if (instance > count)
        {
            Warn($"{label}: instance {instance} requested, but {zoneName} has {count}; giving up");
            Svc.Chat.PrintError($"{AshopConstants.LogPrefix} {zoneName} has {count} instances, not {instance}.");
            return false;
        }

        if (lifestream.CurrentInstance() == instance)
        {
            Diag($"{label}: already in instance {instance} of {zoneName}");
            return true;
        }

        for (var attempt = 1; attempt <= InstanceChangeAttempts; attempt++)
        {
            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            var scope = $"{label}-instance#{attempt}";
            if (!await ReadyInstanceSwitcher(scope))
            {
                return false;
            }

            Status = $"Switching to instance {instance}";
            Diag($"{scope}: asking Lifestream to switch from instance {lifestream.CurrentInstance()} to {instance} of {zoneName} ({ConditionTag()})");
            lifestream.ChangeInstance(instance);
            if (await WaitUntilTimed(() => InInstance(instance), InstanceChangeTimeoutMs, scope, JourneyPollFrames))
            {
                Diag($"{label}: now in instance {instance} of {zoneName}");
                return true;
            }

            if (CancelToken.IsCancellationRequested)
            {
                return false;
            }

            Diag($"{scope}: still in instance {lifestream.CurrentInstance()}, the instance may be full; retrying after {InstanceRetryBackoffMs / TimeUnits.MillisecondsPerSecond}s");
            if (lifestream.IsBusy())
            {
                lifestream.Abort();
            }

            await DelayMs(InstanceRetryBackoffMs);
        }

        Warn($"{label}: could not switch to instance {instance} of {zoneName} in {InstanceChangeAttempts} attempts");
        Svc.Chat.PrintError($"{AshopConstants.LogPrefix} Could not switch to instance {instance}; the character stays in instance {lifestream.CurrentInstance()}.");
        return false;
    }

    // Lifestream switches instances through the aetheryte's own menu, so the character has to stand next to one.
    private async Task<bool> ReadyInstanceSwitcher(string scope)
    {
        var lifestream = LifestreamIPC.Instance;
        if (lifestream.CanChangeInstance() || await WaitUntilTimed(lifestream.CanChangeInstance, InstanceSwitcherSettleMs, $"{scope}-settle"))
        {
            return true;
        }

        if (CancelToken.IsCancellationRequested)
        {
            return false;
        }

        if (FindNearestAetheryteObject() is not { } aetheryte)
        {
            Warn($"{scope}: no aetheryte in sight to switch instances at ({ConditionTag()})");
            Svc.Chat.PrintError($"{AshopConstants.LogPrefix} No aetheryte nearby to switch instances at.");
            return false;
        }

        Status = "Walking to the aetheryte";
        Diag($"{scope}: walking {DistanceTo(aetheryte.Position):F0}m to the aetheryte to switch instances");
        if (!await TravelTo(Svc.ClientState.TerritoryType, aetheryte.Position, InstanceAetheryteReachMeters))
        {
            if (!CancelToken.IsCancellationRequested)
            {
                Warn($"{scope}: could not reach the aetheryte to switch instances");
            }

            return false;
        }

        if (await WaitUntilTimed(lifestream.CanChangeInstance, InstanceSwitcherReadyMs, $"{scope}-ready"))
        {
            return true;
        }

        if (!CancelToken.IsCancellationRequested)
        {
            Warn($"{scope}: Lifestream will not switch instances here ({DistanceTo(aetheryte.Position):F0}m from the aetheryte, {ConditionTag()}); its instance switcher may be disabled in its settings");
            Svc.Chat.PrintError($"{AshopConstants.LogPrefix} Lifestream will not switch instances here. Enable its instance switcher in Lifestream's settings (/li) and try again.");
        }

        return false;
    }

    private static bool InInstance(int instance)
        => Svc.Objects.LocalPlayer is not null
        && !Svc.Condition[ConditionFlag.BetweenAreas]
        && !Svc.Condition[ConditionFlag.BetweenAreas51]
        && LifestreamIPC.Instance.CurrentInstance() == instance
        && !LifestreamIPC.Instance.IsBusy();

    private static IGameObject? FindNearestAetheryteObject()
        => Svc.Objects.LocalPlayer is { } player ? FindNearestAetheryteObject(player.Position) : null;

    private static IGameObject? FindNearestAetheryteObject(Vector3 origin)
    {
        var objects = Svc.Objects;
        IGameObject? nearest = null;
        var bestDistance = float.MaxValue;
        for (var index = 0; index < objects.Length; index++)
        {
            if (objects[index] is not { ObjectKind: ObjectKind.Aetheryte, IsTargetable: true } candidate)
            {
                continue;
            }

            var distance = Vector3.DistanceSquared(origin, candidate.Position);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            nearest = candidate;
        }

        return nearest;
    }

    private static bool OnWorld(IPlayerCharacter? player, uint worldId)
        => player is not null && player.CurrentWorld.RowId == worldId;

    private static string WorldNameOf(IPlayerCharacter? player)
        => player?.CurrentWorld.ValueNullable?.Name.ExtractText() ?? NoWorld;

    private JourneyOutcome Unreached(JourneyOutcome outcome)
        => CancelToken.IsCancellationRequested ? JourneyOutcome.Cancelled : outcome;

    private JourneyOutcome Refuse(string label, string reason)
    {
        Warn($"{label}: refused, {reason}");
        Svc.Chat.PrintError($"{AshopConstants.LogPrefix} Cannot travel: {reason}.");
        return JourneyOutcome.Refused;
    }
}
