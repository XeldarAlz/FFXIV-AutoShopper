using System.Collections.Immutable;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Shared;

namespace AutoShopper.Core.Vendors.Model;

internal readonly record struct VendorLine(PlanDemand Demand, int Quantity, long UnitPrice)
{
    public long Payable => Gil.TryMultiply(UnitPrice, Quantity, out var payable) ? payable : long.MaxValue;
}

internal readonly record struct VendorSplitResult(ImmutableArray<PlanDemand> Market, ImmutableArray<VendorLine> Vendor, long VendorTotal)
{
    public static VendorSplitResult MarketOnly(ImmutableArray<PlanDemand> demands)
        => new(demands.IsDefault ? ImmutableArray<PlanDemand>.Empty : demands, ImmutableArray<VendorLine>.Empty, 0);
}
