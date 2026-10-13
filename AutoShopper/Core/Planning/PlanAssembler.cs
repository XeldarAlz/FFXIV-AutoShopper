using System.Collections.Immutable;
using System.Runtime.InteropServices;
using AutoShopper.Core.Shared;

namespace AutoShopper.Core.Planning;

internal sealed record AssemblyContext(
    PlanRequest Request,
    ImmutableArray<PlanDemand> AuthorizedDemands,
    NormalizedDemands Normalized,
    PlanningProblem Problem,
    WorldSelector Selector,
    WorldSelection Selection,
    int Evaluations,
    DateTime PlannedAtUtc);

internal static class PlanAssembler
{
    public static PurchasePlan Assemble(AssemblyContext context)
    {
        var problem = context.Problem;
        var selection = context.Selection;
        var settings = context.Request.Settings;
        var linesByWorld = new List<PlanLine>?[problem.Candidates.Length];
        var unresolved = new List<UnresolvedDemand>();
        var limits = problem.Limits | selection.Limits;
        var missing = AddInvalidDemands(context.Normalized.Invalid, unresolved);
        var invalidMissing = missing;
        var planned = 0;
        var excess = 0;
        for (var itemIndex = 0; itemIndex < problem.Items.Length; itemIndex++)
        {
            var item = problem.Items[itemIndex];
            var outcome = selection.Outcomes[itemIndex];
            if (outcome.UsedGreedyFill)
            {
                limits |= PlanSearchLimits.GreedyAllocation;
            }

            if (outcome.Overflowed)
            {
                limits |= PlanSearchLimits.ArithmeticOverflow;
            }

            ItemOutcome? everyWorldOutcome = null;
            for (var lineIndex = 0; lineIndex < item.Lines.Length; lineIndex++)
            {
                var demand = item.Lines[lineIndex];
                var line = outcome.Lines[lineIndex];
                CollectLines(item, demand, line, linesByWorld);
                planned += line.PlannedQuantity;
                excess += Math.Max(0, line.PlannedQuantity - demand.RequiredQuantity);
                if (line.MissingQuantity == 0)
                {
                    continue;
                }

                missing += line.MissingQuantity;
                everyWorldOutcome ??= context.Selector.OutcomeFor(itemIndex, context.Selector.AllCandidates);
                var reason = ReasonFor(demand, item.Scopes[lineIndex], line, everyWorldOutcome.Lines[lineIndex], settings, selection.Chosen);
                var covered = Math.Min(line.PlannedQuantity, demand.RequiredQuantity);
                unresolved.Add(new UnresolvedDemand(demand.Key, demand.RequiredQuantity, covered, line.MissingQuantity, reason));
            }
        }

        unresolved.Sort(static (left, right) => left.Demand.CompareTo(right.Demand));
        var stops = BuildStops(problem.Candidates, linesByWorld, settings, out var orderedWorlds);
        var totals = BuildTotals(stops, orderedWorlds, settings, planned, excess, missing);
        return new PurchasePlan
        {
            Version = context.Request.PlanVersion,
            Mode = settings.Mode,
            Provenance = BuildProvenance(context, stops),
            Stops = stops,
            Unresolved = [.. unresolved],
            Totals = totals,
            Limits = limits,
            CheapestBaselineEstimate = selection.Cheapest.Payable,
            BaselineIsPartial = selection.Cheapest.MissingQuantity > 0 || invalidMissing > 0,
            Request = context.Request,
            AuthorizedDemands = context.AuthorizedDemands,
        };
    }

    private static int AddInvalidDemands(PlanDemand[] invalid, List<UnresolvedDemand> unresolved)
    {
        var missing = 0;
        for (var index = 0; index < invalid.Length; index++)
        {
            var demand = invalid[index];
            var required = Math.Max(0, demand.RequiredQuantity);
            unresolved.Add(new UnresolvedDemand(demand.Key, required, 0, required, UnresolvedReason.InvalidDemand));
            missing += required;
        }

        return missing;
    }

    private static void CollectLines(ItemProblem item, PlanDemand demand, LineOutcome line, List<PlanLine>?[] linesByWorld)
    {
        var chosen = line.ChosenOffers;
        for (var chosenIndex = 0; chosenIndex < chosen.Length; chosenIndex++)
        {
            var offerIndex = chosen[chosenIndex];
            var worldIndex = item.WorldIndexes[offerIndex];
            var lines = linesByWorld[worldIndex] ??= new List<PlanLine>();
            lines.Add(new PlanLine(demand.Key, item.Offers[offerIndex], item.Payables[offerIndex]));
        }
    }

    private static UnresolvedReason ReasonFor(
        PlanDemand demand,
        LineScope scope,
        LineOutcome chosen,
        LineOutcome everyWorld,
        PlanSettings settings,
        SubsetEvaluation selected)
    {
        var required = demand.RequiredQuantity;
        if (scope.MatchingQuantity == 0)
        {
            return UnresolvedReason.NoListings;
        }

        if (scope.WithinCapQuantity < required && scope.MatchingQuantity > scope.WithinCapQuantity)
        {
            return UnresolvedReason.AboveUnitCap;
        }

        if (everyWorld.MissingQuantity < chosen.MissingQuantity)
        {
            var stopLimited = settings.MaxStops > 0 && WorldMasks.Count(selected.Requested) >= settings.MaxStops;
            return stopLimited ? UnresolvedReason.StopLimit : UnresolvedReason.SearchLimit;
        }

        if (chosen.LimitedByStricterDemand)
        {
            return UnresolvedReason.TakenByStricterDemand;
        }

        if (scope.WithinCapQuantity >= required)
        {
            return UnresolvedReason.NoFittingStacks;
        }

        return UnresolvedReason.InsufficientStock;
    }

    private static ImmutableArray<PlanStop> BuildStops(
        PlanWorld[] candidates,
        List<PlanLine>?[] linesByWorld,
        PlanSettings settings,
        out ImmutableArray<PlanWorld> orderedWorlds)
    {
        var stopWorlds = new List<PlanWorld>();
        for (var worldIndex = 0; worldIndex < candidates.Length; worldIndex++)
        {
            if (linesByWorld[worldIndex] is not null)
            {
                stopWorlds.Add(candidates[worldIndex]);
            }
        }

        orderedWorlds = RouteOrderer.Order(CollectionsMarshal.AsSpan(stopWorlds), settings.CurrentWorld, settings.HomeWorld, settings.ReturnHome);
        var stops = new PlanStop[orderedWorlds.Length];
        for (var stopIndex = 0; stopIndex < stops.Length; stopIndex++)
        {
            var world = orderedWorlds[stopIndex];
            var lines = linesByWorld[PlanningProblemBuilder.IndexOf(candidates, world.WorldId)]!;
            lines.Sort(OfferOrder.CompareLines);
            long purchase = 0;
            long fee = 0;
            var quantity = 0;
            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                var line = lines[lineIndex];
                purchase = Gil.SaturatingAdd(purchase, line.Payable);
                fee = Gil.SaturatingAdd(fee, line.Offer.Fee);
                quantity += line.Offer.Quantity;
            }

            stops[stopIndex] = new PlanStop(world, [.. lines], purchase, fee, quantity);
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(stops);
    }

    private static PlanTotals BuildTotals(
        ImmutableArray<PlanStop> stops,
        ImmutableArray<PlanWorld> orderedWorlds,
        PlanSettings settings,
        int planned,
        int excess,
        int missing)
    {
        long purchase = 0;
        long fee = 0;
        for (var index = 0; index < stops.Length; index++)
        {
            purchase = Gil.SaturatingAdd(purchase, stops[index].PurchaseEstimate);
            fee = Gil.SaturatingAdd(fee, stops[index].FeeEstimate);
        }

        var route = orderedWorlds.AsSpan();
        return new PlanTotals(
            purchase,
            fee,
            stops.Length,
            RouteOrderer.CountDataCenterChanges(route, settings.CurrentWorld, settings.HomeWorld, settings.ReturnHome),
            RouteOrderer.CountWorldChanges(route, settings.CurrentWorld, settings.HomeWorld, settings.ReturnHome),
            planned,
            excess,
            missing);
    }

    private static PlanProvenance BuildProvenance(AssemblyContext context, ImmutableArray<PlanStop> stops)
    {
        var oldest = DateTime.MaxValue;
        var newest = DateTime.MinValue;
        var estimated = 0;
        for (var stopIndex = 0; stopIndex < stops.Length; stopIndex++)
        {
            var lines = stops[stopIndex].Lines;
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var offer = lines[lineIndex].Offer;
                if (offer.IsEstimate)
                {
                    estimated++;
                }

                // An offer of unknown age has no observed time, so it widens neither end of the range.
                if (offer.ObservedAtUtc == default)
                {
                    continue;
                }

                if (offer.ObservedAtUtc < oldest)
                {
                    oldest = offer.ObservedAtUtc;
                }

                if (offer.ObservedAtUtc > newest)
                {
                    newest = offer.ObservedAtUtc;
                }
            }
        }

        if (oldest > newest)
        {
            oldest = default;
            newest = default;
        }

        var offers = context.Request.Offers;
        return new PlanProvenance(
            context.Request.SourceLabel,
            context.PlannedAtUtc,
            oldest,
            newest,
            offers.IsDefault ? 0 : offers.Length,
            context.Problem.UsableOfferCount,
            estimated,
            context.Problem.Candidates.Length,
            context.Evaluations);
    }
}
