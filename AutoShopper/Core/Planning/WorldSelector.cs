using AutoShopper.Core.Shared;

namespace AutoShopper.Core.Planning;

internal sealed record WorldSelection(SubsetEvaluation Chosen, SubsetEvaluation Cheapest, ItemOutcome[] Outcomes, PlanSearchLimits Limits);

internal sealed class WorldSelector
{
    private readonly PlanningProblem problem;
    private readonly PlanSettings settings;
    private readonly SearchBudget budget;
    private readonly StackAllocator allocator = new();
    private readonly Dictionary<UInt128, ItemOutcome>[] outcomeCache;
    private readonly uint[] dataCenterScratch;
    private readonly int candidateCount;
    private readonly int stopLimit;
    private SubsetEvaluation cheapestSeen;

    public WorldSelector(PlanningProblem problem, PlanSettings settings, SearchBudget budget)
    {
        this.problem = problem;
        this.settings = settings;
        this.budget = budget;
        candidateCount = problem.Candidates.Length;
        stopLimit = settings.StopLimitFor(candidateCount);
        AllCandidates = WorldMasks.All(candidateCount);
        dataCenterScratch = new uint[Math.Max(1, candidateCount)];
        outcomeCache = new Dictionary<UInt128, ItemOutcome>[problem.Items.Length];
        for (var index = 0; index < outcomeCache.Length; index++)
        {
            outcomeCache[index] = new Dictionary<UInt128, ItemOutcome>();
        }
    }

    public UInt128 AllCandidates { get; }

    public WorldSelection Select()
    {
        cheapestSeen = Evaluate(UInt128.Zero);
        if (candidateCount <= settings.EffectiveExactWorldLimit)
        {
            return SelectExactly();
        }

        return SelectHeuristically();
    }

    public ItemOutcome OutcomeFor(int itemIndex, UInt128 mask)
    {
        var item = problem.Items[itemIndex];
        var key = mask & item.WorldMask;
        var cache = outcomeCache[itemIndex];
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var outcome = allocator.Allocate(item, key);
        cache.Add(key, outcome);
        return outcome;
    }

    public SubsetEvaluation Score(UInt128 requested, UInt128 used, long missing, long payable)
    {
        var stops = WorldMasks.Count(used);
        var currentIndex = problem.CurrentWorldIndex;
        var travelStops = currentIndex >= 0 && WorldMasks.Contains(used, currentIndex) ? stops - 1 : stops;
        var changes = CountDataCenterChanges(used);
        var score = Gil.SaturatingAdd(payable, Gil.SaturatingMultiply(settings.EffectiveStopPenalty, travelStops));
        score = Gil.SaturatingAdd(score, Gil.SaturatingMultiply(settings.EffectiveCrossDataCenterPenalty, changes));
        return new SubsetEvaluation(requested, used, missing, payable, stops, travelStops, changes, score);
    }

    private WorldSelection SelectExactly()
    {
        var evaluations = new List<SubsetEvaluation> { cheapestSeen };
        new IncrementalSubsetSearch(this, problem, budget, stopLimit, evaluations).Run();
        var cheapest = Best(evaluations, SelectionObjective.Cheapest);
        var chosen = Best(evaluations, ObjectiveFor(cheapest));
        return Finish(chosen, cheapest, PlanSearchLimits.None);
    }

    private WorldSelection SelectHeuristically()
    {
        var cheapest = Search(SelectionObjective.Cheapest, null);
        if (settings.Mode == PlanMode.Cheapest)
        {
            return Finish(cheapest, cheapest, PlanSearchLimits.HeuristicSearch);
        }

        var chosen = Search(ObjectiveFor(cheapest), cheapest);
        return Finish(chosen, Better(SelectionObjective.Cheapest, cheapest, cheapestSeen), PlanSearchLimits.HeuristicSearch);
    }

    private SelectionObjective ObjectiveFor(SubsetEvaluation cheapest)
    {
        return settings.Mode switch
        {
            PlanMode.Balanced => SelectionObjective.Balanced,
            PlanMode.FewestStops => SelectionObjective.FewestStops(cheapest, settings.EffectivePremiumPercent),
            _ => SelectionObjective.Cheapest,
        };
    }

    private WorldSelection Finish(SubsetEvaluation chosen, SubsetEvaluation cheapest, PlanSearchLimits limits)
    {
        var outcomes = new ItemOutcome[problem.Items.Length];
        for (var index = 0; index < outcomes.Length; index++)
        {
            outcomes[index] = OutcomeFor(index, chosen.Requested);
        }

        return new WorldSelection(chosen, cheapest, outcomes, limits | budget.Reached);
    }

    private static SubsetEvaluation Best(List<SubsetEvaluation> evaluations, SelectionObjective objective)
    {
        var best = evaluations[0];
        for (var index = 1; index < evaluations.Count; index++)
        {
            var candidate = evaluations[index];
            if (objective.Compare(candidate, best) < 0)
            {
                best = candidate;
            }
        }

        return best;
    }

    private SubsetEvaluation Search(SelectionObjective objective, SubsetEvaluation? seed)
    {
        var best = Descend(objective, GreedyAdd(objective));
        if (TryConsolidate(objective, out var consolidated))
        {
            best = Better(objective, best, Descend(objective, consolidated));
        }

        if (seed is { } start)
        {
            best = Better(objective, best, Descend(objective, start));
        }

        return best;
    }

    private static SubsetEvaluation Better(SelectionObjective objective, SubsetEvaluation left, SubsetEvaluation right)
    {
        return objective.Compare(right, left) < 0 ? right : left;
    }

    private SubsetEvaluation GreedyAdd(SelectionObjective objective)
    {
        var current = Evaluate(UInt128.Zero);
        while (WorldMasks.Count(current.Requested) < stopLimit)
        {
            var next = current;
            if (!TryBestAddition(objective, current.Requested, ref next))
            {
                return next;
            }

            if (next.Requested == current.Requested)
            {
                return current;
            }

            current = next;
        }

        return current;
    }

    private bool TryConsolidate(SelectionObjective objective, out SubsetEvaluation result)
    {
        result = default;
        if (!TryEvaluate(AllCandidates, out var current))
        {
            return false;
        }

        while (WorldMasks.Count(current.Requested) > 0)
        {
            var mustDrop = WorldMasks.Count(current.Requested) > stopLimit;
            var next = current;
            var hasNext = false;
            for (var worldIndex = 0; worldIndex < candidateCount; worldIndex++)
            {
                if (!WorldMasks.Contains(current.Requested, worldIndex))
                {
                    continue;
                }

                if (!TryEvaluate(current.Requested & ~WorldMasks.Bit(worldIndex), out var candidate))
                {
                    result = current;
                    return !mustDrop;
                }

                if (!hasNext || objective.Compare(candidate, next) < 0)
                {
                    next = candidate;
                    hasNext = true;
                }
            }

            if (!hasNext || (!mustDrop && objective.Compare(next, current) >= 0))
            {
                break;
            }

            current = next;
        }

        result = current;
        return WorldMasks.Count(current.Requested) <= stopLimit;
    }

    // Variable neighbourhood descent: the cheap drop and add moves are exhausted before the quadratic swap neighbourhood.
    private SubsetEvaluation Descend(SelectionObjective objective, SubsetEvaluation start)
    {
        var current = start;
        while (true)
        {
            var next = current;
            var completed = TryBestDrop(objective, current.Requested, ref next);
            if (completed && objective.Compare(next, current) >= 0 && WorldMasks.Count(current.Requested) < stopLimit)
            {
                completed = TryBestAddition(objective, current.Requested, ref next);
            }

            if (completed && objective.Compare(next, current) >= 0)
            {
                completed = TryBestSwap(objective, current.Requested, ref next);
            }

            if (objective.Compare(next, current) >= 0)
            {
                return current;
            }

            current = next;
            if (!completed)
            {
                return current;
            }
        }
    }

    private bool TryBestDrop(SelectionObjective objective, UInt128 requested, ref SubsetEvaluation best)
    {
        for (var inside = 0; inside < candidateCount; inside++)
        {
            if (WorldMasks.Contains(requested, inside) && !TryImprove(objective, requested & ~WorldMasks.Bit(inside), ref best))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryBestAddition(SelectionObjective objective, UInt128 requested, ref SubsetEvaluation best)
    {
        for (var outside = 0; outside < candidateCount; outside++)
        {
            if (!WorldMasks.Contains(requested, outside) && !TryImprove(objective, requested | WorldMasks.Bit(outside), ref best))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryBestSwap(SelectionObjective objective, UInt128 requested, ref SubsetEvaluation best)
    {
        for (var inside = 0; inside < candidateCount; inside++)
        {
            if (!WorldMasks.Contains(requested, inside))
            {
                continue;
            }

            var without = requested & ~WorldMasks.Bit(inside);
            for (var outside = 0; outside < candidateCount; outside++)
            {
                if (!WorldMasks.Contains(requested, outside) && !TryImprove(objective, without | WorldMasks.Bit(outside), ref best))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private bool TryImprove(SelectionObjective objective, UInt128 mask, ref SubsetEvaluation best)
    {
        if (!TryEvaluate(mask, out var candidate))
        {
            return false;
        }

        if (objective.Compare(candidate, best) < 0)
        {
            best = candidate;
        }

        return true;
    }

    private bool TryEvaluate(UInt128 mask, out SubsetEvaluation evaluation)
    {
        if (!budget.TryCharge())
        {
            evaluation = default;
            return false;
        }

        evaluation = Evaluate(mask);
        if (SelectionObjective.Cheapest.Compare(evaluation, cheapestSeen) < 0)
        {
            cheapestSeen = evaluation;
        }

        return true;
    }

    private SubsetEvaluation Evaluate(UInt128 mask)
    {
        long missing = 0;
        long payable = 0;
        var used = UInt128.Zero;
        var items = problem.Items;
        for (var itemIndex = 0; itemIndex < items.Length; itemIndex++)
        {
            var outcome = OutcomeFor(itemIndex, mask);
            missing += outcome.MissingQuantity;
            payable = Gil.SaturatingAdd(payable, outcome.Payable);
            used |= outcome.UsedWorlds;
        }

        return Score(mask, used, missing, payable);
    }

    private int CountDataCenterChanges(UInt128 used)
    {
        var distinctCount = 0;
        var candidates = problem.Candidates;
        for (var worldIndex = 0; worldIndex < candidateCount; worldIndex++)
        {
            if (!WorldMasks.Contains(used, worldIndex))
            {
                continue;
            }

            var dataCenterId = candidates[worldIndex].DataCenterId;
            if (ContainsDataCenter(distinctCount, dataCenterId))
            {
                continue;
            }

            dataCenterScratch[distinctCount] = dataCenterId;
            distinctCount++;
        }

        return RouteOrderer.CountDataCenterChanges(
            dataCenterScratch.AsSpan(0, distinctCount),
            settings.CurrentWorld.DataCenterId,
            settings.HomeWorld.DataCenterId,
            settings.ReturnHome);
    }

    private bool ContainsDataCenter(int count, uint dataCenterId)
    {
        for (var index = 0; index < count; index++)
        {
            if (dataCenterScratch[index] == dataCenterId)
            {
                return true;
            }
        }

        return false;
    }
}
