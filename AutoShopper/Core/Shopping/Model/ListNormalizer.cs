namespace AutoShopper.Core.Shopping.Model;

public static class ListNormalizer
{
    public static NormalizedList Normalize(IReadOnlyList<ShoppingLine> lines)
    {
        var demands = new List<NormalizedDemand>(lines.Count);
        var demandIndexByKey = new Dictionary<DemandKey, int>(lines.Count);
        var demandIndexByLine = new int[lines.Count];
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            if (!IsUsable(line))
            {
                demandIndexByLine[lineIndex] = NormalizedList.NoDemand;
                continue;
            }

            var key = line.Key();
            if (demandIndexByKey.TryGetValue(key, out var demandIndex))
            {
                demands[demandIndex] = Merge(demands[demandIndex], line);
                demandIndexByLine[lineIndex] = demandIndex;
                continue;
            }

            demandIndexByKey[key] = demands.Count;
            demandIndexByLine[lineIndex] = demands.Count;
            demands.Add(new NormalizedDemand(line.ItemId, line.Quality, line.Mode, line.Quantity, line.MaxUnitPrice, line.ExcessAllowance, 1));
        }

        return new NormalizedList(demands.ToArray(), demandIndexByLine);
    }

    // Units a stricter ReachInventory target of the same item is going to buy also count toward an Any target of that
    // item, so the two targets never ask for the same units twice.
    public static int Shortfall(ReadOnlySpan<NormalizedDemand> demands, int demandIndex, OwnedCount owned)
    {
        var demand = demands[demandIndex];
        if (demand.Mode == QuantityMode.Buy)
        {
            return demand.Quantity;
        }

        if (demand.Quality != ItemQuality.Any)
        {
            return Math.Max(0, demand.Quantity - owned.Of(demand.Quality));
        }

        var covered = (long)owned.Total;
        for (var index = 0; index < demands.Length; index++)
        {
            var other = demands[index];
            if (index == demandIndex || other.ItemId != demand.ItemId || other.Mode != QuantityMode.ReachInventory || other.Quality == ItemQuality.Any)
            {
                continue;
            }

            covered += Math.Max(0, other.Quantity - owned.Of(other.Quality));
        }

        return (int)Math.Max(0L, demand.Quantity - covered);
    }

    public static ListValidation Validate(IReadOnlyList<ShoppingLine> lines, IItemTraits? traits)
    {
        var problems = new LineProblems[lines.Count];
        var firstLineByKey = new Dictionary<DemandKey, int>(lines.Count);
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var lineProblems = ValueProblems(line);
            if (traits is not null)
            {
                lineProblems |= ItemProblems(line, traits);
            }

            var key = line.Key();
            if (firstLineByKey.TryGetValue(key, out var firstLine))
            {
                lineProblems |= LineProblems.Duplicate;
                problems[firstLine] |= LineProblems.Duplicate;
            }
            else
            {
                firstLineByKey[key] = lineIndex;
            }

            problems[lineIndex] |= lineProblems;
        }

        return new ListValidation(problems, traits is not null);
    }

    // Two Buy lines ask for two purchases, so they add up; two ReachInventory lines describe the same stock, so only
    // the larger target counts. Merged lines keep the strictest unit cap and the smallest excess allowance.
    private static NormalizedDemand Merge(NormalizedDemand demand, ShoppingLine line)
    {
        var quantity = demand.Mode == QuantityMode.Buy
            ? (int)Math.Min((long)demand.Quantity + line.Quantity, ShoppingLimits.MaximumQuantity)
            : Math.Max(demand.Quantity, line.Quantity);

        return demand with
        {
            Quantity = quantity,
            MaxUnitPrice = StricterCap(demand.MaxUnitPrice, line.MaxUnitPrice),
            ExcessAllowance = Math.Min(demand.ExcessAllowance, line.ExcessAllowance),
            LineCount = demand.LineCount + 1,
        };
    }

    private static long StricterCap(long left, long right)
    {
        if (left == 0)
        {
            return right;
        }

        return right == 0 ? left : Math.Min(left, right);
    }

    private static bool IsUsable(ShoppingLine line)
        => line.ItemId != 0 && ValueProblems(line) == LineProblems.None;

    private static LineProblems ValueProblems(ShoppingLine line)
    {
        var problems = LineProblems.None;
        if (!ShoppingLimits.IsValidQuantity(line.Quantity))
        {
            problems |= LineProblems.InvalidQuantity;
        }

        if (!ShoppingLimits.IsValidUnitPrice(line.MaxUnitPrice))
        {
            problems |= LineProblems.InvalidUnitCap;
        }

        if (!ShoppingLimits.IsValidExcess(line.ExcessAllowance))
        {
            problems |= LineProblems.InvalidExcess;
        }

        return problems;
    }

    private static LineProblems ItemProblems(ShoppingLine line, IItemTraits traits)
    {
        if (!traits.IsMarketable(line.ItemId))
        {
            return LineProblems.NotMarketable;
        }

        return line.Quality == ItemQuality.HighQuality && !traits.CanBeHighQuality(line.ItemId)
            ? LineProblems.QualityUnavailable
            : LineProblems.None;
    }
}
