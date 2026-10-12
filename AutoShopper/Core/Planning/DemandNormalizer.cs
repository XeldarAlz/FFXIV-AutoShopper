using System.Collections.Immutable;

namespace AutoShopper.Core.Planning;

internal sealed class NormalizedDemands
{
    public required PlanDemand[] Lines { get; init; }

    public required PlanDemand[] Invalid { get; init; }

    public PlanDemand[] OpenLines()
    {
        var count = 0;
        for (var index = 0; index < Lines.Length; index++)
        {
            if (Lines[index].RequiredQuantity > 0)
            {
                count++;
            }
        }

        var open = new PlanDemand[count];
        var write = 0;
        for (var index = 0; index < Lines.Length; index++)
        {
            if (Lines[index].RequiredQuantity > 0)
            {
                open[write] = Lines[index];
                write++;
            }
        }

        return open;
    }
}

internal static class DemandNormalizer
{
    public static NormalizedDemands Normalize(ImmutableArray<PlanDemand> demands)
    {
        if (demands.IsDefaultOrEmpty)
        {
            return new NormalizedDemands { Lines = [], Invalid = [] };
        }

        var valid = new List<PlanDemand>(demands.Length);
        var invalid = new List<PlanDemand>();
        for (var index = 0; index < demands.Length; index++)
        {
            var demand = demands[index];
            if (demand.IsValid)
            {
                valid.Add(demand);
            }
            else
            {
                invalid.Add(demand);
            }
        }

        valid.Sort(CompareByKey);
        var merged = new List<PlanDemand>(valid.Count);
        var groupStart = 0;
        while (groupStart < valid.Count)
        {
            var groupEnd = groupStart + 1;
            while (groupEnd < valid.Count && valid[groupEnd].Key == valid[groupStart].Key)
            {
                groupEnd++;
            }

            var combined = Combine(valid, groupStart, groupEnd);
            if (combined.IsValid)
            {
                merged.Add(combined);
            }
            else
            {
                invalid.Add(combined);
            }

            groupStart = groupEnd;
        }

        invalid.Sort(CompareByKey);
        return new NormalizedDemands { Lines = merged.ToArray(), Invalid = invalid.ToArray() };
    }

    private static PlanDemand Combine(List<PlanDemand> sorted, int start, int end)
    {
        long required = 0;
        long excess = 0;
        long cap = 0;
        for (var index = start; index < end; index++)
        {
            var demand = sorted[index];
            required += demand.RequiredQuantity;
            excess += demand.ExcessAllowance;
            cap = StricterCap(cap, demand.UnitCapIncludingFee);
        }

        var clampedRequired = (int)Math.Min(required, int.MaxValue);
        var clampedExcess = (int)Math.Min(excess, int.MaxValue);
        return new PlanDemand(sorted[start].Key, clampedRequired, clampedExcess, cap);
    }

    private static long StricterCap(long current, long candidate)
    {
        if (current <= 0)
        {
            return candidate;
        }

        if (candidate <= 0)
        {
            return current;
        }

        return Math.Min(current, candidate);
    }

    private static int CompareByKey(PlanDemand left, PlanDemand right)
    {
        var order = left.Key.CompareTo(right.Key);
        if (order != 0)
        {
            return order;
        }

        order = left.RequiredQuantity.CompareTo(right.RequiredQuantity);
        if (order != 0)
        {
            return order;
        }

        order = left.ExcessAllowance.CompareTo(right.ExcessAllowance);
        if (order != 0)
        {
            return order;
        }

        return left.UnitCapIncludingFee.CompareTo(right.UnitCapIncludingFee);
    }
}
