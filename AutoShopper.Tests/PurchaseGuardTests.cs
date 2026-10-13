using AutoShopper.Core.Execution.Model;
using AutoShopper.Core.Planning;
using static AutoShopper.Tests.ExecutionFixtures;

namespace AutoShopper.Tests;

public sealed class PurchaseGuardTests
{
    private static readonly Dictionary<GuardRejection, Func<GuardInput, GuardInput>> Breakers = new()
    {
        [GuardRejection.ExecutionUnavailable] = input => input with { ExecutionAvailable = false },
        [GuardRejection.WrongRun] = input => input with { ActiveRunId = Guid.NewGuid() },
        [GuardRejection.WrongCharacter] = input => input with { CharacterContentId = 9 },
        [GuardRejection.WorldNotAllowed] = input => input with
        {
            CurrentWorldId = 7,
            Selected = input.Selected with { WorldId = 7 },
            Current = input.Current with { WorldId = 7 },
        },
        [GuardRejection.WrongWorld] = input => input with { Current = input.Current with { WorldId = OtherAllowedWorld } },
        [GuardRejection.StaleObservation] = input => input with { SearchGeneration = SearchGeneration + 1 },
        [GuardRejection.PurchasePending] = input => input with { Spend = new SpendState(MaxSpend, 0, 5_050, Guid.NewGuid()) },
        [GuardRejection.InvalidListing] = input => input with { Current = input.Current with { Quantity = 0 } },
        [GuardRejection.Mannequin] = input => input with { Current = input.Current with { IsMannequin = true } },
        [GuardRejection.SetListing] = input => input with { Current = input.Current with { IsSellingAsSet = true } },
        [GuardRejection.OwnRetainer] = input => input with { Current = input.Current with { RetainerId = OwnRetainer } },
        [GuardRejection.ItemMismatch] = input => input with
        {
            Selected = input.Selected with { ItemId = Ore + 1 },
            Current = input.Current with { ItemId = Ore + 1 },
        },
        [GuardRejection.QualityMismatch] = input => input with { Current = input.Current with { IsHq = true } },
        [GuardRejection.QuantityMismatch] = input => input with { Current = input.Current with { Quantity = 6 } },
        [GuardRejection.ListingChanged] = input => input with { Current = input.Current with { UnitPrice = 1_001 } },
        [GuardRejection.DemandNotAuthorized] = input => input with
        {
            Demand = new DemandKey(Ore, DemandQuality.HighQuality),
            Selected = input.Selected with { IsHq = true },
            Current = input.Current with { IsHq = true },
        },
        [GuardRejection.DemandExhausted] = input => input with { ConfirmedQuantity = 10 },
        [GuardRejection.ExcessExceeded] = input => input with { ConfirmedQuantity = 8 },
        [GuardRejection.ArithmeticOverflow] = input => input with
        {
            Selected = input.Selected with { UnitPrice = long.MaxValue / 2 },
            Current = input.Current with { UnitPrice = long.MaxValue / 2 },
        },
        [GuardRejection.UnitCapExceeded] = input => input with
        {
            Selected = Listing(unitPrice: 1_100, totalTax: 55),
            Current = Listing(unitPrice: 1_100, totalTax: 55),
        },
        [GuardRejection.BudgetExceeded] = input => input with { Spend = new SpendState(MaxSpend, 96_000, 0, Guid.Empty) },
        [GuardRejection.ReserveBreached] = input => input with { CurrentGil = 15_000 },
        [GuardRejection.UniqueAlreadyOwned] = input => input with { UniqueItemAlreadyOwned = true },
        [GuardRejection.InventoryFull] = input => input with { FreeInventorySlots = 0 },
    };

    [Fact]
    public void ValidPurchaseIsAllowedWithTheLivePayable()
    {
        var verdict = PurchaseGuard.Evaluate(ValidInput());

        Assert.True(verdict.IsAllowed);
        Assert.Equal(5 * 1_000 + 50, verdict.Payable);
    }

    [Fact]
    public void EveryRejectionReasonHasACase()
    {
        foreach (var rejection in Enum.GetValues<GuardRejection>())
        {
            if (rejection != GuardRejection.None)
            {
                Assert.True(Breakers.ContainsKey(rejection), $"no case for {rejection}");
            }
        }
    }

    [Fact]
    public void EachBrokenCheckIsRejectedWithItsOwnReason()
    {
        foreach (var (expected, breaker) in Breakers)
        {
            var verdict = PurchaseGuard.Evaluate(breaker(ValidInput()));

            Assert.False(verdict.IsAllowed, $"{expected} was allowed");
            Assert.Equal(expected, verdict.Rejection);
            Assert.Equal(0, verdict.Payable);
        }
    }

    [Fact]
    public void SellerContentIdMatchingThePlayerIsRefused()
    {
        var input = ValidInput();
        var verdict = PurchaseGuard.Evaluate(input with { Current = input.Current with { SellerContentId = Character } });

        Assert.Equal(GuardRejection.OwnRetainer, verdict.Rejection);
    }

    [Fact]
    public void TravelCommitmentCountsAgainstTheReserve()
    {
        var input = ValidInput() with { CurrentGil = 20_000 };

        Assert.True(PurchaseGuard.Evaluate(input).IsAllowed);
        Assert.Equal(GuardRejection.ReserveBreached, PurchaseGuard.Evaluate(input with { TravelCommitment = 5_000 }).Rejection);
    }

    [Fact]
    public void ExcessAllowanceIsHonouredExactly()
    {
        var input = ValidInput();

        Assert.True(PurchaseGuard.Evaluate(input with { ConfirmedQuantity = 7 }).IsAllowed);
        Assert.Equal(GuardRejection.ExcessExceeded, PurchaseGuard.Evaluate(input with { ConfirmedQuantity = 8 }).Rejection);
    }

    [Fact]
    public void InventoryRoomUsesTheStackSize()
    {
        var input = ValidInput() with { ItemStackSize = 2 };

        Assert.Equal(GuardRejection.InventoryFull, PurchaseGuard.Evaluate(input with { FreeInventorySlots = 2 }).Rejection);
        Assert.True(PurchaseGuard.Evaluate(input with { FreeInventorySlots = 3 }).IsAllowed);
    }

    [Fact]
    public void AnyOutstandingReservationBlocksANewPurchase()
    {
        var input = ValidInput() with { Spend = new SpendState(MaxSpend, 0, 1, Guid.Empty) };

        Assert.Equal(GuardRejection.PurchasePending, PurchaseGuard.Evaluate(input).Rejection);
    }

    [Fact]
    public void BudgetBoundaryIsInclusive()
    {
        var exact = ValidInput() with { Spend = new SpendState(MaxSpend, MaxSpend - 5_050, 0, Guid.Empty) };

        Assert.True(PurchaseGuard.Evaluate(exact).IsAllowed);
        Assert.Equal(
            GuardRejection.BudgetExceeded,
            PurchaseGuard.Evaluate(exact with { Spend = new SpendState(MaxSpend, MaxSpend - 5_049, 0, Guid.Empty) }).Rejection);
    }

    [Fact]
    public void CrystalThatFitsItsPouchStackNeedsNoBagSlot()
    {
        var input = ValidInput() with { IsCrystal = true, ItemStackSize = 9_999, OwnedQuantity = 9_000, FreeInventorySlots = 0 };

        Assert.True(PurchaseGuard.Evaluate(input).IsAllowed);
    }

    [Fact]
    public void CrystalThatWouldOverflowItsPouchStackIsRefused()
    {
        var input = ValidInput() with { IsCrystal = true, ItemStackSize = 9_999, OwnedQuantity = 9_996, FreeInventorySlots = 50 };

        Assert.Equal(GuardRejection.InventoryFull, PurchaseGuard.Evaluate(input).Rejection);
    }

    [Fact]
    public void StackNeedsOneFreeSlotPerStackSizeItSpans()
    {
        var oversized = Listing(quantity: 5);
        var input = ValidInput() with { ItemStackSize = 2, FreeInventorySlots = 2 };

        Assert.Equal(GuardRejection.InventoryFull, PurchaseGuard.Evaluate(input with { Selected = oversized, Current = oversized }).Rejection);
        Assert.True(PurchaseGuard.Evaluate(input with { FreeInventorySlots = 3 }).IsAllowed);
    }
}
