using System.Collections.Immutable;
using System.Threading;

namespace AutoShopper.Core.Planning;

internal sealed class Planner
{
    private readonly TimeProvider timeProvider;

    public Planner()
        : this(TimeProvider.System)
    {
    }

    public Planner(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
    }

    public PurchasePlan Plan(PlanRequest request, CancellationToken cancellationToken = default)
    {
        return Build(request, request.Demands, cancellationToken);
    }

    public PurchasePlan Replan(
        PurchasePlan previousPlan,
        ImmutableArray<ConfirmedPurchase> confirmedPurchases,
        ImmutableArray<string> excludedListingKeys,
        PlanWorld currentWorld,
        ImmutableArray<PlanOffer> freshOffers,
        CancellationToken cancellationToken = default)
    {
        var previousRequest = previousPlan.Request;
        var authorized = previousPlan.AuthorizedDemands;
        var request = previousRequest with
        {
            Demands = OutstandingDemands(authorized, confirmedPurchases),
            Offers = freshOffers.IsDefault ? ImmutableArray<PlanOffer>.Empty : freshOffers,
            ExcludedListingKeys = MergeExclusions(previousRequest.ExcludedListingKeys, excludedListingKeys, confirmedPurchases),
            Settings = previousRequest.Settings with { CurrentWorld = currentWorld },
            PlanVersion = previousPlan.Version + 1,
        };
        return Build(request, authorized, cancellationToken);
    }

    private PurchasePlan Build(PlanRequest request, ImmutableArray<PlanDemand> authorizedDemands, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = request.Settings;
        var normalized = DemandNormalizer.Normalize(request.Demands);
        var problem = PlanningProblemBuilder.Build(
            normalized.OpenLines(),
            request.Offers,
            request.Worlds,
            request.ExcludedListingKeys,
            settings.EffectiveOffersPerWorldLimit,
            settings.CurrentWorld.WorldId);
        var budget = new SearchBudget(timeProvider, settings.EffectiveTimeBudget, settings.EffectiveEvaluationLimit, cancellationToken);
        var selector = new WorldSelector(problem, settings, budget);
        var selection = selector.Select();
        var context = new AssemblyContext(
            request,
            authorizedDemands.IsDefault ? ImmutableArray<PlanDemand>.Empty : authorizedDemands,
            normalized,
            problem,
            selector,
            selection,
            budget.Evaluations,
            timeProvider.GetUtcNow().UtcDateTime);
        return PlanAssembler.Assemble(context);
    }

    private static ImmutableArray<PlanDemand> OutstandingDemands(ImmutableArray<PlanDemand> authorized, ImmutableArray<ConfirmedPurchase> confirmedPurchases)
    {
        var normalized = DemandNormalizer.Normalize(authorized);
        var acquired = AcquiredByDemand(confirmedPurchases);
        var outstanding = ImmutableArray.CreateBuilder<PlanDemand>(normalized.Lines.Length + normalized.Invalid.Length);
        for (var index = 0; index < normalized.Lines.Length; index++)
        {
            var line = normalized.Lines[index];
            var reduced = acquired.TryGetValue(line.Key, out var quantity) ? line.ReducedBy((int)Math.Min(quantity, int.MaxValue)) : line;
            if (reduced.RequiredQuantity > 0)
            {
                outstanding.Add(reduced);
            }
        }

        for (var index = 0; index < normalized.Invalid.Length; index++)
        {
            outstanding.Add(normalized.Invalid[index]);
        }

        return outstanding.ToImmutable();
    }

    private static Dictionary<DemandKey, long> AcquiredByDemand(ImmutableArray<ConfirmedPurchase> confirmedPurchases)
    {
        var acquired = new Dictionary<DemandKey, long>();
        if (confirmedPurchases.IsDefault)
        {
            return acquired;
        }

        var countedListings = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < confirmedPurchases.Length; index++)
        {
            var purchase = confirmedPurchases[index];
            if (purchase.Quantity <= 0)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(purchase.ListingKey) && !countedListings.Add(purchase.ListingKey))
            {
                continue;
            }

            acquired.TryGetValue(purchase.Demand, out var total);
            acquired[purchase.Demand] = total + purchase.Quantity;
        }

        return acquired;
    }

    private static ImmutableArray<string> MergeExclusions(
        ImmutableArray<string> previous,
        ImmutableArray<string> added,
        ImmutableArray<ConfirmedPurchase> confirmedPurchases)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        AddKeys(keys, previous);
        AddKeys(keys, added);
        if (!confirmedPurchases.IsDefault)
        {
            for (var index = 0; index < confirmedPurchases.Length; index++)
            {
                var key = confirmedPurchases[index].ListingKey;
                if (!string.IsNullOrEmpty(key))
                {
                    keys.Add(key);
                }
            }
        }

        var sorted = new string[keys.Count];
        keys.CopyTo(sorted);
        Array.Sort(sorted, StringComparer.Ordinal);
        return [.. sorted];
    }

    private static void AddKeys(HashSet<string> keys, ImmutableArray<string> source)
    {
        if (source.IsDefault)
        {
            return;
        }

        for (var index = 0; index < source.Length; index++)
        {
            if (!string.IsNullOrEmpty(source[index]))
            {
                keys.Add(source[index]);
            }
        }
    }
}
