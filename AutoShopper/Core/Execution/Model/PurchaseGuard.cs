using AutoShopper.Core.Planning;
using AutoShopper.Core.Shared;

namespace AutoShopper.Core.Execution.Model;

internal static class PurchaseGuard
{
    public static GuardVerdict Evaluate(in GuardInput input)
    {
        var rejection = CheckSession(input);
        if (rejection != GuardRejection.None)
        {
            return GuardVerdict.Rejected(rejection);
        }

        rejection = CheckListingKind(input);
        if (rejection != GuardRejection.None)
        {
            return GuardVerdict.Rejected(rejection);
        }

        rejection = CheckSelectionStillMatches(input);
        if (rejection != GuardRejection.None)
        {
            return GuardVerdict.Rejected(rejection);
        }

        if (!input.Authorization.TryGetDemand(input.Demand, out var demand))
        {
            return GuardVerdict.Rejected(GuardRejection.DemandNotAuthorized);
        }

        rejection = CheckDemand(input, demand);
        if (rejection != GuardRejection.None)
        {
            return GuardVerdict.Rejected(rejection);
        }

        if (!input.Current.TryGetPayable(out var payable))
        {
            return GuardVerdict.Rejected(GuardRejection.ArithmeticOverflow);
        }

        rejection = CheckMoney(input, demand, payable);
        if (rejection != GuardRejection.None)
        {
            return GuardVerdict.Rejected(rejection);
        }

        rejection = CheckInventory(input);
        if (rejection != GuardRejection.None)
        {
            return GuardVerdict.Rejected(rejection);
        }

        return GuardVerdict.Allowed(payable);
    }

    private static GuardRejection CheckSession(in GuardInput input)
    {
        var authorization = input.Authorization;
        if (!input.ExecutionAvailable)
        {
            return GuardRejection.ExecutionUnavailable;
        }

        if (input.ActiveRunId == Guid.Empty || input.ActiveRunId != authorization.RunId)
        {
            return GuardRejection.WrongRun;
        }

        if (input.CharacterContentId != authorization.CharacterContentId)
        {
            return GuardRejection.WrongCharacter;
        }

        if (!authorization.AllowsWorld(input.CurrentWorldId))
        {
            return GuardRejection.WorldNotAllowed;
        }

        if (input.Current.WorldId != input.CurrentWorldId || input.Selected.WorldId != input.CurrentWorldId)
        {
            return GuardRejection.WrongWorld;
        }

        if (input.Current.SearchGeneration != input.SearchGeneration || input.Selected.SearchGeneration != input.SearchGeneration)
        {
            return GuardRejection.StaleObservation;
        }

        if (input.Spend.HasPendingAttempt)
        {
            return GuardRejection.PurchasePending;
        }

        return GuardRejection.None;
    }

    private static GuardRejection CheckListingKind(in GuardInput input)
    {
        var listing = input.Current;
        if (listing.ItemId == 0 || listing.Quantity <= 0 || listing.UnitPrice < 0 || listing.TotalTax < 0)
        {
            return GuardRejection.InvalidListing;
        }

        if (listing.IsMannequin)
        {
            return GuardRejection.Mannequin;
        }

        if (listing.IsSellingAsSet)
        {
            return GuardRejection.SetListing;
        }

        if (IsOwnListing(input))
        {
            return GuardRejection.OwnRetainer;
        }

        return GuardRejection.None;
    }

    private static bool IsOwnListing(in GuardInput input)
    {
        var listing = input.Current;
        if (listing.SellerContentId != 0 && listing.SellerContentId == input.CharacterContentId)
        {
            return true;
        }

        var ownRetainers = input.OwnRetainerIds;
        if (ownRetainers.IsDefaultOrEmpty)
        {
            return false;
        }

        for (var index = 0; index < ownRetainers.Length; index++)
        {
            if (ownRetainers[index] == listing.RetainerId)
            {
                return true;
            }
        }

        return false;
    }

    private static GuardRejection CheckSelectionStillMatches(in GuardInput input)
    {
        var current = input.Current;
        var selected = input.Selected;
        if (current.ItemId != selected.ItemId || current.ItemId != input.Demand.ItemId)
        {
            return GuardRejection.ItemMismatch;
        }

        if (current.IsHq != selected.IsHq || !input.Demand.Accepts(current.IsHq))
        {
            return GuardRejection.QualityMismatch;
        }

        if (current.Quantity != selected.Quantity)
        {
            return GuardRejection.QuantityMismatch;
        }

        if (current.ListingId != selected.ListingId
            || current.RetainerId != selected.RetainerId
            || current.UnitPrice != selected.UnitPrice
            || current.TotalTax != selected.TotalTax)
        {
            return GuardRejection.ListingChanged;
        }

        return GuardRejection.None;
    }

    private static GuardRejection CheckDemand(in GuardInput input, PlanDemand demand)
    {
        if (input.ConfirmedQuantity >= demand.RequiredQuantity)
        {
            return GuardRejection.DemandExhausted;
        }

        if (!Gil.TryAdd(Math.Max(0, input.ConfirmedQuantity), input.Current.Quantity, out var afterPurchase) || afterPurchase > demand.MaximumTotal)
        {
            return GuardRejection.ExcessExceeded;
        }

        return GuardRejection.None;
    }

    private static GuardRejection CheckMoney(in GuardInput input, PlanDemand demand, long payable)
    {
        if (!demand.AllowsStackCost(payable, input.Current.Quantity))
        {
            return GuardRejection.UnitCapExceeded;
        }

        var spend = input.Spend;
        if (!Gil.TrySubtract(input.Authorization.MaxSpend, spend.Confirmed, out var afterConfirmed)
            || !Gil.TrySubtract(afterConfirmed, spend.Reserved, out var remaining))
        {
            return GuardRejection.ArithmeticOverflow;
        }

        if (payable > remaining)
        {
            return GuardRejection.BudgetExceeded;
        }

        if (!Gil.TrySubtract(input.CurrentGil, payable, out var afterPurchase)
            || !Gil.TrySubtract(afterPurchase, Math.Max(0, input.TravelCommitment), out var projected))
        {
            return GuardRejection.ArithmeticOverflow;
        }

        if (projected < input.Authorization.GilReserve || afterPurchase < 0)
        {
            return GuardRejection.ReserveBreached;
        }

        return GuardRejection.None;
    }

    private static GuardRejection CheckInventory(in GuardInput input)
    {
        if (input.UniqueItemAlreadyOwned)
        {
            return GuardRejection.UniqueAlreadyOwned;
        }

        long stackSize = Math.Max(1, input.ItemStackSize);
        if (input.IsCrystal)
        {
            return Math.Max(0L, input.OwnedQuantity) + input.Current.Quantity <= stackSize ? GuardRejection.None : GuardRejection.CrystalLimitExceeded;
        }

        var slotsNeeded = (input.Current.Quantity + stackSize - 1) / stackSize;
        if (input.FreeInventorySlots < slotsNeeded)
        {
            return GuardRejection.InventoryFull;
        }

        return GuardRejection.None;
    }
}
