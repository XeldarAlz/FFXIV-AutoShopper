using AutoShopper.Core.Shared;

namespace AutoShopper.Core.Planning;

internal readonly record struct SubsetEvaluation(
    UInt128 Requested,
    UInt128 Used,
    long MissingQuantity,
    long Payable,
    int Stops,
    int TravelStops,
    int DataCenterChanges,
    long BalancedScore);

internal readonly struct SelectionObjective
{
    private readonly PlanMode mode;
    private readonly long allowedPayable;

    private SelectionObjective(PlanMode mode, long allowedPayable)
    {
        this.mode = mode;
        this.allowedPayable = allowedPayable;
    }

    public static SelectionObjective Cheapest => new(PlanMode.Cheapest, long.MaxValue);

    public static SelectionObjective Balanced => new(PlanMode.Balanced, long.MaxValue);

    public static SelectionObjective FewestStops(SubsetEvaluation baseline, int premiumPercent)
    {
        var allowed = Gil.TryPercentFloor(baseline.Payable, premiumPercent, out var premium)
            ? Gil.SaturatingAdd(baseline.Payable, premium)
            : long.MaxValue;
        return new SelectionObjective(PlanMode.FewestStops, allowed);
    }

    public int Compare(in SubsetEvaluation left, in SubsetEvaluation right)
    {
        var order = left.MissingQuantity.CompareTo(right.MissingQuantity);
        if (order != 0)
        {
            return order;
        }

        order = mode switch
        {
            PlanMode.Balanced => CompareBalanced(left, right),
            PlanMode.FewestStops => CompareFewestStops(left, right),
            _ => CompareCheapest(left, right),
        };
        if (order != 0)
        {
            return order;
        }

        order = left.Used.CompareTo(right.Used);
        if (order != 0)
        {
            return order;
        }

        return left.Requested.CompareTo(right.Requested);
    }

    private static int CompareCheapest(in SubsetEvaluation left, in SubsetEvaluation right)
    {
        var order = left.Payable.CompareTo(right.Payable);
        if (order != 0)
        {
            return order;
        }

        return CompareTravel(left, right);
    }

    private static int CompareBalanced(in SubsetEvaluation left, in SubsetEvaluation right)
    {
        var order = left.BalancedScore.CompareTo(right.BalancedScore);
        if (order != 0)
        {
            return order;
        }

        return CompareCheapest(left, right);
    }

    private int CompareFewestStops(in SubsetEvaluation left, in SubsetEvaluation right)
    {
        var leftOutside = left.Payable > allowedPayable;
        var rightOutside = right.Payable > allowedPayable;
        var order = leftOutside.CompareTo(rightOutside);
        if (order != 0)
        {
            return order;
        }

        order = left.Stops.CompareTo(right.Stops);
        if (order != 0)
        {
            return order;
        }

        return CompareCheapest(left, right);
    }

    private static int CompareTravel(in SubsetEvaluation left, in SubsetEvaluation right)
    {
        var order = left.TravelStops.CompareTo(right.TravelStops);
        if (order != 0)
        {
            return order;
        }

        order = left.DataCenterChanges.CompareTo(right.DataCenterChanges);
        if (order != 0)
        {
            return order;
        }

        return left.Stops.CompareTo(right.Stops);
    }
}
