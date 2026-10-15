using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;
using HousingAddress = (string Name, int World, int City, int Ward, int PropertyType, int Plot, int Apartment, bool ApartmentSubdivision, bool AliasEnabled, string Alias);

namespace AutoShopper.Core.Ipc;

// Lifestream registers through EzIPC, so every gate is "Lifestream." plus the method name. Void methods are actions,
// so it is HasAction that says whether they can be called; everything else is a function.
internal sealed class LifestreamIPC
{
    private const string GatePrefix = "Lifestream.";
    private const string ChangeWorldFailed = AshopConstants.LogPrefix + " Lifestream ChangeWorld failed";
    private const string TeleportAndChangeWorldFailed = AshopConstants.LogPrefix + " Lifestream TPAndChangeWorld failed";
    private const string CanVisitSameDataCenterFailed = AshopConstants.LogPrefix + " Lifestream CanVisitSameDC failed";
    private const string CanVisitCrossDataCenterFailed = AshopConstants.LogPrefix + " Lifestream CanVisitCrossDC failed";
    private const string IsBusyFailed = AshopConstants.LogPrefix + " Lifestream IsBusy failed";
    private const string AbortFailed = AshopConstants.LogPrefix + " Lifestream Abort failed";
    private const string TeleportFailed = AshopConstants.LogPrefix + " Lifestream Teleport failed";
    private const string CanChangeInstanceFailed = AshopConstants.LogPrefix + " Lifestream CanChangeInstance failed";
    private const string NumberOfInstancesFailed = AshopConstants.LogPrefix + " Lifestream GetNumberOfInstances failed";
    private const string ChangeInstanceFailed = AshopConstants.LogPrefix + " Lifestream ChangeInstance failed";
    private const string CurrentInstanceFailed = AshopConstants.LogPrefix + " Lifestream GetCurrentInstance failed";
    private const string RealTerritoryTypeFailed = AshopConstants.LogPrefix + " Lifestream GetRealTerritoryType failed";
    private const string AethernetTeleportByIdFailed = AshopConstants.LogPrefix + " Lifestream AethernetTeleportById failed";
    private const string ActiveAetheryteFailed = AshopConstants.LogPrefix + " Lifestream GetActiveAetheryte failed";
    private const string BuildAddressFailed = AshopConstants.LogPrefix + " Lifestream BuildAddressBookEntry failed";
    private const string GoToHousingAddressFailed = AshopConstants.LogPrefix + " Lifestream GoToHousingAddress failed";

    private static LifestreamIPC? instance;

    private readonly ICallGateSubscriber<string, bool> changeWorld;
    private readonly ICallGateSubscriber<string, bool, string?, bool, int?, bool?, bool?, object> teleportAndChangeWorld;
    private readonly ICallGateSubscriber<string, bool> canVisitSameDataCenter;
    private readonly ICallGateSubscriber<string, bool> canVisitCrossDataCenter;
    private readonly ICallGateSubscriber<bool> isBusy;
    private readonly ICallGateSubscriber<object> abort;
    private readonly ICallGateSubscriber<uint, byte, bool> teleport;
    private readonly ICallGateSubscriber<bool> canChangeInstance;
    private readonly ICallGateSubscriber<int> numberOfInstances;
    private readonly ICallGateSubscriber<int, object> changeInstance;
    private readonly ICallGateSubscriber<int> currentInstance;
    private readonly ICallGateSubscriber<uint> realTerritoryType;
    private readonly ICallGateSubscriber<uint, bool> aethernetTeleportById;
    private readonly ICallGateSubscriber<uint> activeAetheryte;
    private readonly ICallGateSubscriber<string, string, string, string, bool, bool, HousingAddress> buildAddress;
    private readonly ICallGateSubscriber<HousingAddress, object> goToHousingAddress;

    // Cached once so the polling loops of a journey do not allocate a delegate on every call.
    private readonly Func<bool> isBusyCall;
    private readonly Func<bool> canChangeInstanceCall;
    private readonly Func<int> numberOfInstancesCall;
    private readonly Func<int> currentInstanceCall;
    private readonly Func<uint> realTerritoryTypeCall;
    private readonly Func<uint> activeAetheryteCall;
    private readonly Action abortCall;

    private LifestreamIPC()
    {
        var pluginInterface = Svc.PluginInterface;
        changeWorld = pluginInterface.GetIpcSubscriber<string, bool>(GatePrefix + "ChangeWorld");
        teleportAndChangeWorld = pluginInterface.GetIpcSubscriber<string, bool, string?, bool, int?, bool?, bool?, object>(GatePrefix + "TPAndChangeWorld");
        canVisitSameDataCenter = pluginInterface.GetIpcSubscriber<string, bool>(GatePrefix + "CanVisitSameDC");
        canVisitCrossDataCenter = pluginInterface.GetIpcSubscriber<string, bool>(GatePrefix + "CanVisitCrossDC");
        isBusy = pluginInterface.GetIpcSubscriber<bool>(GatePrefix + "IsBusy");
        abort = pluginInterface.GetIpcSubscriber<object>(GatePrefix + "Abort");
        teleport = pluginInterface.GetIpcSubscriber<uint, byte, bool>(GatePrefix + "Teleport");
        canChangeInstance = pluginInterface.GetIpcSubscriber<bool>(GatePrefix + "CanChangeInstance");
        numberOfInstances = pluginInterface.GetIpcSubscriber<int>(GatePrefix + "GetNumberOfInstances");
        changeInstance = pluginInterface.GetIpcSubscriber<int, object>(GatePrefix + "ChangeInstance");
        currentInstance = pluginInterface.GetIpcSubscriber<int>(GatePrefix + "GetCurrentInstance");
        realTerritoryType = pluginInterface.GetIpcSubscriber<uint>(GatePrefix + "GetRealTerritoryType");
        aethernetTeleportById = pluginInterface.GetIpcSubscriber<uint, bool>(GatePrefix + "AethernetTeleportById");
        activeAetheryte = pluginInterface.GetIpcSubscriber<uint>(GatePrefix + "GetActiveAetheryte");
        buildAddress = pluginInterface.GetIpcSubscriber<string, string, string, string, bool, bool, HousingAddress>(GatePrefix + "BuildAddressBookEntry");
        goToHousingAddress = pluginInterface.GetIpcSubscriber<HousingAddress, object>(GatePrefix + "GoToHousingAddress");

        isBusyCall = isBusy.InvokeFunc;
        canChangeInstanceCall = canChangeInstance.InvokeFunc;
        numberOfInstancesCall = numberOfInstances.InvokeFunc;
        currentInstanceCall = currentInstance.InvokeFunc;
        realTerritoryTypeCall = realTerritoryType.InvokeFunc;
        activeAetheryteCall = activeAetheryte.InvokeFunc;
        abortCall = abort.InvokeAction;
    }

    public static LifestreamIPC Instance => instance ??= new LifestreamIPC();

    public bool IsAvailable => isBusy.HasFunction;

    // False when Lifestream is busy or cannot reach the world; it picks same or cross data center travel itself.
    public bool ChangeWorld(string world)
        => IpcGate.Invoke(changeWorld.HasFunction, () => changeWorld.InvokeFunc(world), false, ChangeWorldFailed);

    // No secondary teleport of Lifestream's own, no notification, no return to the gateway: the journey does the rest.
    // A gateway of 0 leaves the city the change happens at to Lifestream.
    public void TeleportAndChangeWorld(string world, bool dataCenterTransfer, int gateway)
        => IpcGate.Run(
            teleportAndChangeWorld.HasAction,
            () => teleportAndChangeWorld.InvokeAction(world, dataCenterTransfer, null, true, gateway > 0 ? gateway : null, false, false),
            TeleportAndChangeWorldFailed);

    public bool CanVisitSameDataCenter(string world)
        => IpcGate.Invoke(canVisitSameDataCenter.HasFunction, () => canVisitSameDataCenter.InvokeFunc(world), false, CanVisitSameDataCenterFailed);

    public bool CanVisitCrossDataCenter(string world)
        => IpcGate.Invoke(canVisitCrossDataCenter.HasFunction, () => canVisitCrossDataCenter.InvokeFunc(world), false, CanVisitCrossDataCenterFailed);

    public bool IsBusy()
        => IpcGate.Invoke(isBusy.HasFunction, isBusyCall, false, IsBusyFailed);

    public void Abort()
        => IpcGate.Run(abort.HasAction, abortCall, AbortFailed);

    public bool Teleport(uint aetheryteId, byte subIndex)
        => IpcGate.Invoke(teleport.HasFunction, () => teleport.InvokeFunc(aetheryteId, subIndex), false, TeleportFailed);

    // True only next to an aetheryte, outside any menu, with Lifestream idle and its instance switcher enabled.
    public bool CanChangeInstance()
        => IpcGate.Invoke(canChangeInstance.HasFunction, canChangeInstanceCall, false, CanChangeInstanceFailed);

    // 0 when the zone has no instances or Lifestream has not seen its instance menu yet.
    public int NumberOfInstances()
        => IpcGate.Invoke(numberOfInstances.HasFunction, numberOfInstancesCall, 0, NumberOfInstancesFailed);

    public void ChangeInstance(int number)
        => IpcGate.Run(changeInstance.HasAction, () => changeInstance.InvokeAction(number), ChangeInstanceFailed);

    public int CurrentInstance()
        => IpcGate.Invoke(currentInstance.HasFunction, currentInstanceCall, 0, CurrentInstanceFailed);

    public uint RealTerritoryType()
        => IpcGate.Invoke(realTerritoryType.HasFunction, realTerritoryTypeCall, 0u, RealTerritoryTypeFailed);

    // False when Lifestream is busy, the character stands at no aetheryte or shard, or the stop is on another network.
    public bool AethernetTeleportById(uint aetheryteId)
        => IpcGate.Invoke(aethernetTeleportById.HasFunction, () => aethernetTeleportById.InvokeFunc(aetheryteId), false, AethernetTeleportByIdFailed);

    // The aetheryte or shard Lifestream sees the character standing at, 0 when none.
    public uint ActiveAetheryte()
        => IpcGate.Invoke(activeAetheryte.HasFunction, activeAetheryteCall, 0u, ActiveAetheryteFailed);

    public bool GoToWard(string world, string district, int ward, int plot)
    {
        var address = IpcGate.Invoke(
            buildAddress.HasFunction,
            () => buildAddress.InvokeFunc(world, district, ward.ToString(System.Globalization.CultureInfo.InvariantCulture), plot.ToString(System.Globalization.CultureInfo.InvariantCulture), false, false),
            default,
            BuildAddressFailed);
        if (address.World == 0 || address.Ward != ward)
        {
            return false;
        }

        if (!goToHousingAddress.HasAction)
        {
            return false;
        }

        IpcGate.Run(true, () => goToHousingAddress.InvokeAction(address), GoToHousingAddressFailed);
        return true;
    }
}
