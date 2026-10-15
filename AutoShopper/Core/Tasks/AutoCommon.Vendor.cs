using System.Numerics;
using System.Threading.Tasks;
using AutoShopper.Core.Game.Ops;
using AutoShopper.Core.Ipc;
using AutoShopper.Core.Travel;
using AutoShopper.Core.Vendors;
using AutoShopper.Core.Vendors.Model;
using clib.TaskSystem;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;

namespace AutoShopper.Core.Tasks;

internal enum VendorReach : byte
{
    Reached = 0,
    TravelFailed = 1,
    VendorMissing = 2,
}

public abstract partial class AutoCommon
{
    private const float VendorArrivalMeters = 3f;
    private const float VendorReachMeters = 4f;
    private const float VendorStepToleranceMeters = 2.5f;
    private const int VendorApproachAttempts = 2;
    private const int VendorApproachWatchdogMs = 25_000;
    private const int VendorSearchMs = 6_000;
    private const int VendorPollMs = 250;
    private const int WardArrivalTimeoutMs = 180_000;
    private const int PlayerReadyTimeoutMs = 30_000;
    private const int ShopOpenAttempts = 3;
    private const int ShopOpenTimeoutMs = 6_000;
    private const int ShopCloseTimeoutMs = 3_000;
    private const int FirstWard = 1;
    private const int FirstPlot = 1;
    private const string SelectIconStringAddon = "SelectIconString";
    private const string SelectStringAddon = "SelectString";

    private protected async Task<VendorReach> GoToVendor(VendorSpot spot, Vector3[] positions, string label)
    {
        if (spot.Zone == VendorZone.Residential && Svc.ClientState.TerritoryType != spot.TerritoryId && !await EnterResidentialDistrict(spot.TerritoryId, label))
        {
            return VendorReach.TravelFailed;
        }

        if (CancelToken.IsCancellationRequested)
        {
            return VendorReach.TravelFailed;
        }

        var destination = NearestPosition(spot, positions);
        Diag($"{label}: going to vendor {spot.NpcId} in territory {spot.TerritoryId} at {destination}.");
        var arrived = await TravelTo(spot.TerritoryId, destination, VendorArrivalMeters);
        if (CancelToken.IsCancellationRequested)
        {
            return VendorReach.TravelFailed;
        }

        if (!arrived && Svc.ClientState.TerritoryType != spot.TerritoryId)
        {
            Warn($"{label}: territory {spot.TerritoryId} of vendor {spot.NpcId} could not be reached.");
            return VendorReach.TravelFailed;
        }

        if (Svc.Condition[ConditionFlag.Mounted])
        {
            await SafeDismount($"{label}-dismount");
        }

        return await StepUpToVendor(spot.NpcId, label);
    }

    private protected async Task<bool> OpenVendorShop(uint npcId, uint shopId, string label)
    {
        if (VendorShop.IsOpen(shopId))
        {
            return true;
        }

        await CloseVendorShop(label);
        for (var attempt = 1; attempt <= ShopOpenAttempts && !CancelToken.IsCancellationRequested; attempt++)
        {
            if (!await WaitUntilTimed(NpcInteraction.PlayerReady, PlayerReadyTimeoutMs, $"{label}-ready"))
            {
                Warn($"{label}: the character is not free to talk to the vendor ({NpcInteraction.DescribeBlockers()}).");
                continue;
            }

            if (NpcInteraction.FindNearest(npcId) is not { } vendor)
            {
                Warn($"{label}: vendor {npcId} is no longer in sight.");
                return false;
            }

            var result = VendorShop.Open(vendor, shopId);
            Diag($"{label}: asked vendor {npcId} for shop {shopId}: {result} (attempt {attempt}).");
            if (result == VendorOpenResult.ShopMissing)
            {
                Warn($"{label}: vendor {npcId} does not offer shop {shopId} here.");
                CloseVendorMenus();
                return false;
            }

            if (await WaitForShop(shopId))
            {
                return true;
            }

            CloseVendorMenus();
        }

        Warn($"{label}: shop {shopId} of vendor {npcId} did not open.");
        return false;
    }

    private protected async Task CloseVendorShop(string label)
    {
        if (!VendorShop.IsAnyOpen())
        {
            return;
        }

        VendorShop.Close();
        await WaitUntilTimed(static () => !VendorShop.IsAnyOpen(), ShopCloseTimeoutMs, $"{label}-close");
    }

    private async Task<bool> EnterResidentialDistrict(uint territoryId, string label)
    {
        var lifestream = LifestreamIPC.Instance;
        if (!lifestream.IsAvailable || !ResidentialDistricts.TryGetLifestreamName(territoryId, out var district) || !Worlds.TryCurrent(out var world))
        {
            Warn($"{label}: Lifestream cannot take the character into residential district {territoryId}.");
            return false;
        }

        if (!await WaitUntilTimed(() => !lifestream.IsBusy(), PlayerReadyTimeoutMs, $"{label}-lifestream-idle"))
        {
            Warn($"{label}: Lifestream is busy with another trip; residential district {territoryId} is not entered.");
            return false;
        }

        await WaitUntilTimed(NpcInteraction.PlayerReady, PlayerReadyTimeoutMs, $"{label}-ward-ready");
        Diag($"{label}: entering ward {FirstWard} of {district} on {world.Name} through Lifestream.");
        if (!lifestream.GoToWard(world.Name, district, FirstWard, FirstPlot))
        {
            Warn($"{label}: Lifestream did not accept the address of ward {FirstWard} of {district}.");
            return false;
        }

        if (lifestream.IsBusy() || await BusyNextFrame())
        {
            TravelOwnership.Claim(TravelOwner);
        }

        try
        {
            return await WaitForWard(territoryId, label);
        }
        finally
        {
            TravelOwnership.AbortIfOwnedBy(TravelOwner, "the trip into the residential district is over");
            TravelOwnership.Release(TravelOwner);
        }
    }

    private async Task<bool> WaitForWard(uint territoryId, string label)
    {
        var lifestream = LifestreamIPC.Instance;
        var deadline = Environment.TickCount64 + WardArrivalTimeoutMs;
        var sawBusy = false;
        while (Environment.TickCount64 < deadline && !CancelToken.IsCancellationRequested)
        {
            if (Svc.ClientState.TerritoryType == territoryId)
            {
                TravelOwnership.AbortIfOwnedBy(TravelOwner, "the character is in the residential district");
                return await WaitUntilTimed(NpcInteraction.PlayerReady, PlayerReadyTimeoutMs, $"{label}-ward-arrival");
            }

            var busy = lifestream.IsBusy();
            sawBusy |= busy;
            if (sawBusy && !busy && !Svc.Condition[ConditionFlag.BetweenAreas] && !Svc.Condition[ConditionFlag.BetweenAreas51])
            {
                Warn($"{label}: Lifestream stopped before the character reached residential district {territoryId}.");
                return false;
            }

            await DelayMs(VendorPollMs);
        }

        Warn($"{label}: timed out on the way to residential district {territoryId}.");
        return false;
    }

    private async Task<VendorReach> StepUpToVendor(uint npcId, string label)
    {
        var deadline = Environment.TickCount64 + VendorSearchMs;
        var approaches = 0;
        while (!CancelToken.IsCancellationRequested)
        {
            if (NpcInteraction.FindNearest(npcId) is not { } vendor || Svc.Objects.LocalPlayer is not { } player)
            {
                if (Environment.TickCount64 >= deadline)
                {
                    Warn($"{label}: vendor {npcId} is not in sight.");
                    return VendorReach.VendorMissing;
                }

                await DelayMs(VendorPollMs);
                continue;
            }

            var distance = Vector3.Distance(player.Position, vendor.Position);
            if (distance <= VendorReachMeters)
            {
                return VendorReach.Reached;
            }

            if (approaches++ >= VendorApproachAttempts)
            {
                Warn($"{label}: could not get within {VendorReachMeters} m of vendor {npcId}; {distance:F1} m away.");
                return VendorReach.VendorMissing;
            }

            var target = vendor.Position;
            var approach = new MoveOp(move => move.MoveInZone(target, MovementConfig.Default.WithTolerance(VendorStepToleranceMeters), null));
            await RunCancellable(approach, VendorApproachWatchdogMs, $"{label}-approach", StuckDetector.MoveStallAbort($"{label}-approach"));
        }

        return VendorReach.TravelFailed;
    }

    private async Task<bool> WaitForShop(uint shopId)
    {
        var deadline = Environment.TickCount64 + ShopOpenTimeoutMs;
        while (Environment.TickCount64 < deadline && !CancelToken.IsCancellationRequested)
        {
            if (VendorShop.IsOpen(shopId))
            {
                return true;
            }

            if (NpcInteraction.TalkOpen())
            {
                NpcInteraction.ProgressTalk();
            }

            await DelayMs(VendorPollMs);
        }

        return false;
    }

    private static void CloseVendorMenus()
    {
        NpcInteraction.Close(SelectIconStringAddon);
        NpcInteraction.Close(SelectStringAddon);
    }

    private static Vector3 NearestPosition(VendorSpot spot, Vector3[] positions)
    {
        if (Svc.ClientState.TerritoryType != spot.TerritoryId || Svc.Objects.LocalPlayer is not { } player || positions.Length == 0)
        {
            return spot.Position;
        }

        var nearest = spot.Position;
        var nearestDistance = Vector3.DistanceSquared(player.Position, nearest);
        for (var index = 0; index < positions.Length; index++)
        {
            var distance = Vector3.DistanceSquared(player.Position, positions[index]);
            if (distance < nearestDistance)
            {
                nearest = positions[index];
                nearestDistance = distance;
            }
        }

        return nearest;
    }
}
