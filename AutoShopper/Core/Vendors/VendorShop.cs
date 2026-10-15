using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using GameEventHandler = FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandler;

namespace AutoShopper.Core.Vendors;

internal enum VendorOpenResult : byte
{
    Interacted = 0,
    Selected = 1,
    WrongTarget = 2,
    ShopMissing = 3,
}

internal enum VendorBuyResult : byte
{
    Sent = 0,
    ShopClosed = 1,
    Busy = 2,
    NotListed = 3,
}

internal static unsafe class VendorShop
{
    private const int CloseEventValue = -1;
    private const int NotFound = -1;

    public static VendorOpenResult Open(IGameObject vendor, uint shopId) => Open((GameObject*)vendor.Address, shopId);

    public static VendorOpenResult Open(GameObject* vendor, uint shopId)
    {
        TargetSystem.Instance()->InteractWithObject(vendor, false);
        var selector = EventHandlerSelector.Instance();
        if (selector == null || selector->Target == null)
        {
            return VendorOpenResult.Interacted;
        }

        if (selector->Target != vendor)
        {
            return VendorOpenResult.WrongTarget;
        }

        for (var index = 0; index < selector->OptionsCount; index++)
        {
            var handler = selector->Options[index].Handler;
            if (handler != null && handler->Info.EventId.Id == shopId)
            {
                EventFramework.Instance()->InteractWithHandlerFromSelector(index);
                return VendorOpenResult.Selected;
            }
        }

        return VendorOpenResult.ShopMissing;
    }

    public static bool IsOpen(uint shopId)
    {
        var agent = AgentShop.Instance();
        if (agent == null || !agent->IsAgentActive() || agent->EventReceiver == null || !agent->IsAddonReady())
        {
            return false;
        }

        var handler = ShopHandler(shopId);
        return handler != null && ((ShopEventHandler.AgentProxy*)agent->EventReceiver)->Handler == handler;
    }

    public static bool IsAnyOpen()
    {
        var agent = AgentShop.Instance();
        return agent != null && agent->IsAgentActive() && agent->EventReceiver != null;
    }

    public static bool TryGetPrice(uint shopId, uint itemId, out long unitPrice)
    {
        unitPrice = 0;
        var handler = (ShopEventHandler*)ShopHandler(shopId);
        var itemIndex = handler == null ? NotFound : VisibleIndexOf(handler, itemId);
        if (itemIndex == NotFound)
        {
            return false;
        }

        unitPrice = handler->Items[itemIndex].PriceBuy;
        return true;
    }

    public static VendorBuyResult TryBuy(uint shopId, uint itemId, int count)
    {
        if (!IsOpen(shopId))
        {
            return VendorBuyResult.ShopClosed;
        }

        var handler = (ShopEventHandler*)ShopHandler(shopId);
        if (handler->WaitingForTransactionToFinish)
        {
            return VendorBuyResult.Busy;
        }

        var itemIndex = VisibleIndexOf(handler, itemId);
        if (itemIndex == NotFound)
        {
            return VendorBuyResult.NotListed;
        }

        handler->BuyItemIndex = itemIndex;
        handler->ExecuteBuy(count);
        return VendorBuyResult.Sent;
    }

    public static bool TransactionBusy(uint shopId)
    {
        var handler = (ShopEventHandler*)ShopHandler(shopId);
        return handler != null && handler->WaitingForTransactionToFinish;
    }

    public static bool Close()
    {
        var agent = AgentShop.Instance();
        if (agent == null || !agent->IsAgentActive() || agent->EventReceiver == null)
        {
            return false;
        }

        var proxy = (ShopEventHandler.AgentProxy*)agent->EventReceiver;
        if (proxy->Handler == null)
        {
            return false;
        }

        proxy->Handler->CancelInteraction();
        var result = default(AtkValue);
        var command = default(AtkValue);
        command.SetInt(CloseEventValue);
        agent->ReceiveEvent(&result, &command, 1, 0);
        return true;
    }

    private static int VisibleIndexOf(ShopEventHandler* handler, uint itemId)
    {
        for (var index = 0; index < handler->VisibleItemsCount; index++)
        {
            var itemIndex = handler->VisibleItems[index];
            if (handler->Items[itemIndex].ItemId == itemId)
            {
                return itemIndex;
            }
        }

        return NotFound;
    }

    private static GameEventHandler* ShopHandler(uint shopId)
    {
        var framework = EventFramework.Instance();
        if (framework == null || !framework->EventHandlerModule.EventHandlerMap.TryGetValuePointer(shopId, out var entry) || entry == null || entry->Value == null)
        {
            return null;
        }

        return entry->Value->Info.EventId.ContentId == EventHandlerContent.Shop ? entry->Value : null;
    }
}
