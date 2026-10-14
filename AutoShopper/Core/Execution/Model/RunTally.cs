using System.Collections.Immutable;
using System.Runtime.InteropServices;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Shared;

namespace AutoShopper.Core.Execution.Model;

internal readonly record struct TallyLine(PlanDemand Demand, int Acquired, long Spent)
{
    public int Missing => Math.Max(0, Demand.RequiredQuantity - Acquired);

    public bool Satisfied => Acquired >= Demand.RequiredQuantity;
}

// Progress of one run against its frozen demand. A listing counts once, however often its confirmation arrives.
internal sealed class RunTally
{
    private readonly PlanDemand[] demands;
    private readonly int[] confirmed;
    private readonly long[] spent;
    private readonly List<ConfirmedPurchase> acquisitions = [];
    private readonly HashSet<string> countedListings = new(StringComparer.Ordinal);

    public RunTally(ImmutableArray<PlanDemand> demands)
    {
        this.demands = demands.IsDefault ? [] : demands.AsSpan().ToArray();
        confirmed = new int[this.demands.Length];
        spent = new long[this.demands.Length];
    }

    public int Count => demands.Length;

    // Rises with every change, so text built from the tally is rebuilt only then.
    public int Revision { get; private set; }

    public long ConfirmedSpend { get; private set; }

    public int ConfirmedUnits { get; private set; }

    public int Purchases { get; private set; }

    public bool AllSatisfied
    {
        get
        {
            for (var index = 0; index < demands.Length; index++)
            {
                if (confirmed[index] < demands[index].RequiredQuantity)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public static RunTally Restore(ImmutableArray<PlanDemand> demands, ImmutableArray<ConfirmedPurchase> purchases)
    {
        var tally = new RunTally(demands);
        if (purchases.IsDefault)
        {
            return tally;
        }

        for (var index = 0; index < purchases.Length; index++)
        {
            var purchase = purchases[index];
            tally.RecordConfirmed(purchase.Demand, purchase.ListingKey, purchase.Quantity, purchase.Payable);
        }

        return tally;
    }

    public TallyLine LineAt(int index) => new(demands[index], confirmed[index], spent[index]);

    public int Acquired(DemandKey key)
    {
        var index = IndexOf(key);
        return index < 0 ? 0 : confirmed[index];
    }

    public bool IsSatisfied(DemandKey key)
    {
        var index = IndexOf(key);
        return index < 0 || confirmed[index] >= demands[index].RequiredQuantity;
    }

    public PlanDemand Outstanding(DemandKey key)
    {
        var index = IndexOf(key);
        return index < 0 ? default : demands[index].ReducedBy(confirmed[index]);
    }

    public bool RecordConfirmed(DemandKey key, string listingKey, int quantity, long payable)
    {
        var index = IndexOf(key);
        if (index < 0 || quantity <= 0 || payable < 0)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(listingKey) && !countedListings.Add(listingKey))
        {
            return false;
        }

        confirmed[index] += quantity;
        ConfirmedUnits += quantity;
        ConfirmedSpend = Gil.TryAdd(ConfirmedSpend, payable, out var confirmedSpend) ? confirmedSpend : long.MaxValue;
        spent[index] = Gil.TryAdd(spent[index], payable, out var lineSpend) ? lineSpend : long.MaxValue;
        acquisitions.Add(new ConfirmedPurchase(key, listingKey, quantity, payable));
        Purchases++;
        Revision++;
        return true;
    }

    // Every acquisition so far, as the planner reads them when it plans what is left.
    public ImmutableArray<ConfirmedPurchase> Acquisitions()
        => acquisitions.Count == 0 ? ImmutableArray<ConfirmedPurchase>.Empty : ImmutableCollectionsMarshal.AsImmutableArray(acquisitions.ToArray());

    private int IndexOf(DemandKey key)
    {
        for (var index = 0; index < demands.Length; index++)
        {
            if (demands[index].Key == key)
            {
                return index;
            }
        }

        return -1;
    }
}
