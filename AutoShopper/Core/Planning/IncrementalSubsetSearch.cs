using AutoShopper.Core.Shared;

namespace AutoShopper.Core.Planning;

// Visits every world subset depth first; a child extends its parent's knapsack by one world instead of re-solving all offers.
internal sealed class IncrementalSubsetSearch
{
    private const long Unreachable = long.MaxValue;

    private readonly WorldSelector selector;
    private readonly SearchBudget budget;
    private readonly List<SubsetEvaluation> evaluations;
    private readonly KnapsackItem[] knapsackItems;
    private readonly int[] allocatedItems;
    private readonly long[][][] levels;
    private readonly int candidateCount;
    private readonly int stopLimit;
    private bool exhausted;

    public IncrementalSubsetSearch(WorldSelector selector, PlanningProblem problem, SearchBudget budget, int stopLimit, List<SubsetEvaluation> evaluations)
    {
        this.selector = selector;
        this.budget = budget;
        this.stopLimit = stopLimit;
        this.evaluations = evaluations;
        candidateCount = problem.Candidates.Length;
        var knapsack = new List<KnapsackItem>();
        var allocated = new List<int>();
        for (var itemIndex = 0; itemIndex < problem.Items.Length; itemIndex++)
        {
            var item = problem.Items[itemIndex];
            if (item.Lines.Length == 1)
            {
                knapsack.Add(KnapsackItem.From(item, candidateCount));
            }
            else
            {
                allocated.Add(itemIndex);
            }
        }

        knapsackItems = knapsack.ToArray();
        allocatedItems = allocated.ToArray();
        levels = new long[stopLimit + 1][][];
    }

    public void Run()
    {
        var root = Level(0);
        for (var index = 0; index < knapsackItems.Length; index++)
        {
            var costs = root[index];
            costs[0] = 0;
            Array.Fill(costs, Unreachable, 1, costs.Length - 1);
        }

        Visit(0, 0, UInt128.Zero);
    }

    private void Visit(int depth, int firstWorld, UInt128 mask)
    {
        if (depth > 0)
        {
            if (!budget.TryCharge())
            {
                exhausted = true;
                return;
            }

            evaluations.Add(Evaluate(depth, mask));
        }

        if (depth == stopLimit)
        {
            return;
        }

        for (var worldIndex = firstWorld; worldIndex < candidateCount && !exhausted; worldIndex++)
        {
            Extend(depth, worldIndex);
            Visit(depth + 1, worldIndex + 1, mask | WorldMasks.Bit(worldIndex));
        }
    }

    private void Extend(int depth, int worldIndex)
    {
        var parent = Level(depth);
        var child = Level(depth + 1);
        for (var index = 0; index < knapsackItems.Length; index++)
        {
            var item = knapsackItems[index];
            var costs = child[index];
            Array.Copy(parent[index], costs, costs.Length);
            var quantities = item.Quantities[worldIndex];
            var payables = item.Payables[worldIndex];
            var ceiling = costs.Length - 1;
            for (var offerIndex = 0; offerIndex < quantities.Length; offerIndex++)
            {
                var quantity = quantities[offerIndex];
                var price = payables[offerIndex];
                var headroom = long.MaxValue - price;
                for (var reached = ceiling; reached >= quantity; reached--)
                {
                    var before = costs[reached - quantity];
                    if (before > headroom)
                    {
                        continue;
                    }

                    var total = before + price;
                    if (total < costs[reached])
                    {
                        costs[reached] = total;
                    }
                }
            }
        }
    }

    private SubsetEvaluation Evaluate(int depth, UInt128 mask)
    {
        long missing = 0;
        long payable = 0;
        var level = Level(depth);
        for (var index = 0; index < knapsackItems.Length; index++)
        {
            var (lineMissing, lineCost) = Read(level[index], knapsackItems[index].Required);
            missing += lineMissing;
            payable = Gil.SaturatingAdd(payable, lineCost);
        }

        for (var index = 0; index < allocatedItems.Length; index++)
        {
            var outcome = selector.OutcomeFor(allocatedItems[index], mask);
            missing += outcome.MissingQuantity;
            payable = Gil.SaturatingAdd(payable, outcome.Payable);
        }

        return selector.Score(mask, mask, missing, payable);
    }

    private static (int Missing, long Cost) Read(long[] costs, int required)
    {
        var ceiling = costs.Length - 1;
        var best = Unreachable;
        for (var reached = required; reached <= ceiling; reached++)
        {
            if (costs[reached] < best)
            {
                best = costs[reached];
            }
        }

        if (best != Unreachable)
        {
            return (0, best);
        }

        for (var reached = Math.Min(required - 1, ceiling); reached > 0; reached--)
        {
            if (costs[reached] != Unreachable)
            {
                return (required - reached, costs[reached]);
            }
        }

        return (required, 0);
    }

    private long[][] Level(int depth)
    {
        var level = levels[depth];
        if (level is not null)
        {
            return level;
        }

        level = new long[knapsackItems.Length][];
        for (var index = 0; index < knapsackItems.Length; index++)
        {
            level[index] = new long[knapsackItems[index].Ceiling + 1];
        }

        levels[depth] = level;
        return level;
    }

    private sealed class KnapsackItem
    {
        public required int Required { get; init; }

        public required int Ceiling { get; init; }

        public required int[][] Quantities { get; init; }

        public required long[][] Payables { get; init; }

        public static KnapsackItem From(ItemProblem item, int candidateCount)
        {
            var line = item.Lines[0];
            var eligible = item.Eligible[0];
            var quantities = new List<int>[candidateCount];
            var payables = new List<long>[candidateCount];
            for (var worldIndex = 0; worldIndex < candidateCount; worldIndex++)
            {
                quantities[worldIndex] = new List<int>();
                payables[worldIndex] = new List<long>();
            }

            long supply = 0;
            for (var offerIndex = 0; offerIndex < item.Offers.Length; offerIndex++)
            {
                if (!eligible[offerIndex])
                {
                    continue;
                }

                var worldIndex = item.WorldIndexes[offerIndex];
                quantities[worldIndex].Add(item.Offers[offerIndex].Quantity);
                payables[worldIndex].Add(item.Payables[offerIndex]);
                supply += item.Offers[offerIndex].Quantity;
            }

            var quantityArrays = new int[candidateCount][];
            var payableArrays = new long[candidateCount][];
            for (var worldIndex = 0; worldIndex < candidateCount; worldIndex++)
            {
                quantityArrays[worldIndex] = quantities[worldIndex].ToArray();
                payableArrays[worldIndex] = payables[worldIndex].ToArray();
            }

            return new KnapsackItem
            {
                Required = line.RequiredQuantity,
                Ceiling = (int)Math.Min(line.MaximumTotal, supply),
                Quantities = quantityArrays,
                Payables = payableArrays,
            };
        }
    }
}
