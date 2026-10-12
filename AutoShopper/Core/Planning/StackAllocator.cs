using System.Collections.Immutable;
using System.Runtime.InteropServices;
using AutoShopper.Core.Shared;

namespace AutoShopper.Core.Planning;

internal sealed class StackAllocator
{
    private const long Unreachable = long.MaxValue;
    private const int BitsPerWord = 64;

    // 2^28 decision bits is 32 MiB of reconstruction table; a larger search falls back to the cheapest-unit-first fill.
    private const long MaximumDecisionBits = 1L << 28;

    private readonly List<int> picked = new();
    private long[] costs = [];
    private ulong[] decisions = [];
    private int[] candidates = [];
    private bool[] used = [];

    public static ImmutableArray<LineAllocation> AllocateAll(ImmutableArray<PlanDemand> demands, ImmutableArray<PlanOffer> offers)
    {
        var lines = DemandNormalizer.Normalize(demands).OpenLines();
        var worlds = WorldsOf(offers);
        var problem = PlanningProblemBuilder.Build(lines, offers, worlds, ImmutableArray<string>.Empty, int.MaxValue, 0);
        var allocator = new StackAllocator();
        var everyWorld = WorldMasks.All(problem.Candidates.Length);
        var allocations = new List<LineAllocation>(lines.Length);
        for (var itemIndex = 0; itemIndex < problem.Items.Length; itemIndex++)
        {
            var item = problem.Items[itemIndex];
            var outcome = allocator.Allocate(item, everyWorld);
            for (var lineIndex = 0; lineIndex < item.Lines.Length; lineIndex++)
            {
                var line = outcome.Lines[lineIndex];
                var chosen = new PlanOffer[line.ChosenOffers.Length];
                for (var chosenIndex = 0; chosenIndex < chosen.Length; chosenIndex++)
                {
                    chosen[chosenIndex] = item.Offers[line.ChosenOffers[chosenIndex]];
                }

                allocations.Add(new LineAllocation(
                    item.Lines[lineIndex],
                    ImmutableCollectionsMarshal.AsImmutableArray(chosen),
                    line.PlannedQuantity,
                    line.MissingQuantity,
                    line.Payable));
            }
        }

        allocations.Sort(static (left, right) => left.Demand.Key.CompareTo(right.Demand.Key));
        return [.. allocations];
    }

    public ItemOutcome Allocate(ItemProblem item, UInt128 worldMask)
    {
        var offerCount = item.Offers.Length;
        EnsureCapacity(ref used, offerCount);
        Array.Clear(used, 0, offerCount);
        var lineCount = item.Lines.Length;
        var lines = new LineOutcome[lineCount];
        long payable = 0;
        long fee = 0;
        long missing = 0;
        var usedWorlds = UInt128.Zero;
        var overflowed = false;
        var greedy = false;
        for (var lineIndex = 0; lineIndex < lineCount; lineIndex++)
        {
            var line = AllocateLine(item, lineIndex, worldMask, ref overflowed, ref greedy);
            lines[lineIndex] = line;
            payable = AddOrSaturate(payable, line.Payable, ref overflowed);
            fee = AddOrSaturate(fee, line.Fee, ref overflowed);
            missing += line.MissingQuantity;
            var chosen = line.ChosenOffers;
            for (var chosenIndex = 0; chosenIndex < chosen.Length; chosenIndex++)
            {
                usedWorlds |= WorldMasks.Bit(item.WorldIndexes[chosen[chosenIndex]]);
            }
        }

        return new ItemOutcome
        {
            Lines = lines,
            Payable = payable,
            Fee = fee,
            MissingQuantity = missing,
            UsedWorlds = usedWorlds,
            Overflowed = overflowed,
            UsedGreedyFill = greedy,
        };
    }

    private LineOutcome AllocateLine(ItemProblem item, int lineIndex, UInt128 worldMask, ref bool overflowed, ref bool greedy)
    {
        var demand = item.Lines[lineIndex];
        var required = demand.RequiredQuantity;
        picked.Clear();
        if (required <= 0)
        {
            return Commit(item, demand, false, ref overflowed);
        }

        var eligible = item.Eligible[lineIndex];
        var offers = item.Offers;
        var worldIndexes = item.WorldIndexes;
        EnsureCapacity(ref candidates, offers.Length);
        var candidateCount = 0;
        long supplyBeforeStricter = 0;
        long supply = 0;
        for (var offerIndex = 0; offerIndex < offers.Length; offerIndex++)
        {
            if (!eligible[offerIndex] || !WorldMasks.Contains(worldMask, worldIndexes[offerIndex]))
            {
                continue;
            }

            var quantity = offers[offerIndex].Quantity;
            supplyBeforeStricter += quantity;
            if (used[offerIndex])
            {
                continue;
            }

            candidates[candidateCount] = offerIndex;
            candidateCount++;
            supply += quantity;
        }

        if (supply <= required)
        {
            TakeAll(candidateCount);
        }
        else if (!TrySolve(item, demand, candidateCount, supply, ref overflowed))
        {
            greedy = true;
            FillCheapestFirst(item, demand, candidateCount);
        }

        var limitedByStricter = supplyBeforeStricter >= required && supply < required;
        return Commit(item, demand, limitedByStricter, ref overflowed);
    }

    private void TakeAll(int candidateCount)
    {
        for (var index = 0; index < candidateCount; index++)
        {
            picked.Add(candidates[index]);
        }
    }

    private bool TrySolve(ItemProblem item, PlanDemand demand, int candidateCount, long supply, ref bool overflowed)
    {
        var ceiling = (int)Math.Min(demand.MaximumTotal, supply);
        var width = ceiling + 1;
        var bitCount = (long)candidateCount * width;
        if (bitCount > MaximumDecisionBits)
        {
            return false;
        }

        EnsureCapacity(ref costs, width);
        var wordCount = (int)((bitCount + BitsPerWord - 1) / BitsPerWord);
        EnsureCapacity(ref decisions, wordCount);
        Array.Clear(decisions, 0, wordCount);
        costs[0] = 0;
        Array.Fill(costs, Unreachable, 1, ceiling);

        var offers = item.Offers;
        var payables = item.Payables;
        for (var candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
        {
            var offerIndex = candidates[candidateIndex];
            var quantity = offers[offerIndex].Quantity;
            var price = payables[offerIndex];
            var rowStart = (long)candidateIndex * width;
            var headroom = long.MaxValue - price;
            for (var reached = ceiling; reached >= quantity; reached--)
            {
                var before = costs[reached - quantity];
                if (before > headroom)
                {
                    overflowed |= before != Unreachable;
                    continue;
                }

                var total = before + price;
                if (total >= costs[reached])
                {
                    continue;
                }

                costs[reached] = total;
                var bit = rowStart + reached;
                decisions[bit / BitsPerWord] |= 1UL << (int)(bit % BitsPerWord);
            }
        }

        var target = PickTarget(demand.RequiredQuantity, ceiling);
        Reconstruct(item, candidateCount, width, target);
        return true;
    }

    private int PickTarget(int required, int ceiling)
    {
        var bestReached = -1;
        var bestCost = Unreachable;
        for (var reached = required; reached <= ceiling; reached++)
        {
            var cost = costs[reached];
            if (cost < bestCost)
            {
                bestCost = cost;
                bestReached = reached;
            }
        }

        if (bestReached >= 0)
        {
            return bestReached;
        }

        for (var reached = Math.Min(required - 1, ceiling); reached > 0; reached--)
        {
            if (costs[reached] != Unreachable)
            {
                return reached;
            }
        }

        return 0;
    }

    private void Reconstruct(ItemProblem item, int candidateCount, int width, int target)
    {
        var remaining = target;
        for (var candidateIndex = candidateCount - 1; candidateIndex >= 0 && remaining > 0; candidateIndex--)
        {
            var bit = ((long)candidateIndex * width) + remaining;
            if ((decisions[bit / BitsPerWord] & (1UL << (int)(bit % BitsPerWord))) == 0)
            {
                continue;
            }

            var offerIndex = candidates[candidateIndex];
            picked.Add(offerIndex);
            remaining -= item.Offers[offerIndex].Quantity;
        }
    }

    private void FillCheapestFirst(ItemProblem item, PlanDemand demand, int candidateCount)
    {
        var order = candidates.AsSpan(0, candidateCount);
        order.Sort(new UnitCostOrder(item));
        var total = 0;
        for (var index = 0; index < order.Length; index++)
        {
            var offerIndex = order[index];
            var quantity = item.Offers[offerIndex].Quantity;
            if (total + quantity > demand.MaximumTotal)
            {
                continue;
            }

            picked.Add(offerIndex);
            total += quantity;
            if (total >= demand.RequiredQuantity)
            {
                return;
            }
        }
    }

    private LineOutcome Commit(ItemProblem item, PlanDemand demand, bool limitedByStricter, ref bool overflowed)
    {
        var chosen = picked.ToArray();
        Array.Sort(chosen);
        var planned = 0;
        long payable = 0;
        long fee = 0;
        for (var index = 0; index < chosen.Length; index++)
        {
            var offerIndex = chosen[index];
            used[offerIndex] = true;
            planned += item.Offers[offerIndex].Quantity;
            payable = AddOrSaturate(payable, item.Payables[offerIndex], ref overflowed);
            fee = AddOrSaturate(fee, item.Offers[offerIndex].Fee, ref overflowed);
        }

        var missing = Math.Max(0, demand.RequiredQuantity - planned);
        return new LineOutcome(chosen, planned, missing, payable, fee, limitedByStricter);
    }

    private static long AddOrSaturate(long left, long right, ref bool overflowed)
    {
        if (Gil.TryAdd(left, right, out var sum))
        {
            return sum;
        }

        overflowed = true;
        return long.MaxValue;
    }

    private static void EnsureCapacity<T>(ref T[] buffer, int length)
    {
        if (buffer.Length >= length)
        {
            return;
        }

        buffer = new T[Math.Max(length, buffer.Length * 2)];
    }

    private static ImmutableArray<PlanWorld> WorldsOf(ImmutableArray<PlanOffer> offers)
    {
        if (offers.IsDefaultOrEmpty)
        {
            return ImmutableArray<PlanWorld>.Empty;
        }

        var worlds = new List<PlanWorld>();
        var seen = new HashSet<uint>();
        for (var index = 0; index < offers.Length; index++)
        {
            var offer = offers[index];
            if (seen.Add(offer.WorldId))
            {
                worlds.Add(new PlanWorld(offer.WorldId, offer.DataCenterId, 0, string.Empty));
            }
        }

        return [.. worlds];
    }

    private readonly struct UnitCostOrder(ItemProblem item) : IComparer<int>
    {
        public int Compare(int left, int right)
        {
            var order = OfferOrder.CompareUnitCost(item.Payables[left], item.Offers[left].Quantity, item.Payables[right], item.Offers[right].Quantity);
            return order != 0 ? order : left.CompareTo(right);
        }
    }
}
