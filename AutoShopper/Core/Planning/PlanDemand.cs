using AutoShopper.Core.Shared;

namespace AutoShopper.Core.Planning;

internal readonly record struct PlanDemand(DemandKey Key, int RequiredQuantity, int ExcessAllowance, long UnitCapIncludingFee)
{
    // Far above any real basket line, low enough that required + excess and the quantity search stay safe in int.
    public const int MaximumQuantity = 1_000_000;

    public uint ItemId => Key.ItemId;

    public DemandQuality Quality => Key.Quality;

    public int MaximumTotal => RequiredQuantity + ExcessAllowance;

    public bool HasUnitCap => UnitCapIncludingFee > 0;

    public bool IsValid =>
        Key.ItemId != 0
        && Key.IsDefinedQuality
        && RequiredQuantity is >= 0 and <= MaximumQuantity
        && ExcessAllowance is >= 0 and <= MaximumQuantity
        && UnitCapIncludingFee >= 0;

    public static PlanDemand ForPurchase(uint itemId, DemandQuality quality, int quantity, int excessAllowance = 0, long unitCapIncludingFee = 0)
    {
        return new PlanDemand(new DemandKey(itemId, quality), quantity, excessAllowance, unitCapIncludingFee);
    }

    public static PlanDemand ForInventoryTarget(uint itemId, DemandQuality quality, int targetQuantity, int ownedQuantity, int excessAllowance = 0, long unitCapIncludingFee = 0)
    {
        var shortfall = targetQuantity < 0 ? targetQuantity : Math.Max(0, targetQuantity - Math.Max(0, ownedQuantity));
        return new PlanDemand(new DemandKey(itemId, quality), shortfall, excessAllowance, unitCapIncludingFee);
    }

    public PlanDemand ReducedBy(int acquiredQuantity)
    {
        if (acquiredQuantity <= 0)
        {
            return this;
        }

        var required = Math.Max(0, RequiredQuantity - acquiredQuantity);
        var maximum = Math.Max(0, MaximumTotal - acquiredQuantity);
        return this with { RequiredQuantity = required, ExcessAllowance = maximum - required };
    }

    public bool AllowsStackCost(long payable, int quantity)
    {
        if (!HasUnitCap)
        {
            return true;
        }

        // A product past the long range is a limit no representable payable can reach.
        if (!Gil.TryMultiply(UnitCapIncludingFee, quantity, out var limit))
        {
            return true;
        }

        return payable <= limit;
    }
}
