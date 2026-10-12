using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;

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

    // Cached once so the polling loops of a journey do not allocate a delegate on every call.
    private readonly Func<bool> isBusyCall;
    private readonly Func<bool> canChangeInstanceCall;
    private readonly Func<int> numberOfInstancesCall;
    private readonly Func<int> currentInstanceCall;
    private readonly Func<uint> realTerritoryTypeCall;
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

        isBusyCall = isBusy.InvokeFunc;
        canChangeInstanceCall = canChangeInstance.InvokeFunc;
        numberOfInstancesCall = numberOfInstances.InvokeFunc;
        currentInstanceCall = currentInstance.InvokeFunc;
        realTerritoryTypeCall = realTerritoryType.InvokeFunc;
        abortCall = abort.InvokeAction;
    }

    public static LifestreamIPC Instance => instance ??= new LifestreamIPC();

    public bool IsAvailable => isBusy.HasFunction;

    // False when Lifestream is busy or cannot reach the world; it picks same or cross data center travel itself.
    public bool ChangeWorld(string world)
        => IpcGate.Invoke(changeWorld.HasFunction, () => changeWorld.InvokeFunc(world), false, ChangeWorldFailed);

    // No secondary teleport of Lifestream's own, no notification, no return to the gateway: the journey does the rest.
    public void TeleportAndChangeWorld(string world, bool dataCenterTransfer)
        => IpcGate.Run(
            teleportAndChangeWorld.HasAction,
            () => teleportAndChangeWorld.InvokeAction(world, dataCenterTransfer, null, true, null, false, false),
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
}
