using System.Collections.Immutable;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Shopping.Model;

namespace AutoShopper.Core.Preview.Model;

internal sealed class DemandSet
{
    public static readonly DemandSet Empty = new()
    {
        Demands = ImmutableArray<PlanDemand>.Empty,
        ItemIds = ImmutableArray<uint>.Empty,
        TargetsMet = 0,
        TargetsUnknown = 0,
    };

    public required ImmutableArray<PlanDemand> Demands { get; init; }

    // Every item the list names, sorted, whether or not it still needs buying; prices are fetched for all of them.
    public required ImmutableArray<uint> ItemIds { get; init; }

    public required int TargetsMet { get; init; }

    public required int TargetsUnknown { get; init; }

    public bool SameDemands(DemandSet other)
    {
        return Demands.AsSpan().SequenceEqual(other.Demands.AsSpan())
            && ItemIds.AsSpan().SequenceEqual(other.ItemIds.AsSpan())
            && TargetsMet == other.TargetsMet
            && TargetsUnknown == other.TargetsUnknown;
    }
}

// A line's unit cap is the price of one unit including the 5% market fee, the same convention the planner checks
// stacks against.
internal static class DemandBuilder
{
    public static DemandSet Build(ReadOnlySpan<NormalizedDemand> demands, IReadOnlyDictionary<uint, OwnedCount>? owned)
    {
        var built = ImmutableArray.CreateBuilder<PlanDemand>(demands.Length);
        var itemIds = new List<uint>(demands.Length);
        var met = 0;
        var unknown = 0;
        for (var index = 0; index < demands.Length; index++)
        {
            var demand = demands[index];
            if (!itemIds.Contains(demand.ItemId))
            {
                itemIds.Add(demand.ItemId);
            }

            var quality = ToDemandQuality(demand.Quality);
            if (demand.Mode == QuantityMode.Buy)
            {
                built.Add(PlanDemand.ForPurchase(demand.ItemId, quality, demand.Quantity, demand.ExcessAllowance, demand.MaxUnitPrice));
                continue;
            }

            if (owned is null || !owned.TryGetValue(demand.ItemId, out var ownedCount))
            {
                unknown++;
                continue;
            }

            var shortfall = ListNormalizer.Shortfall(demands, index, ownedCount);
            if (shortfall <= 0)
            {
                met++;
                continue;
            }

            built.Add(PlanDemand.ForInventoryTarget(demand.ItemId, quality, demand.Quantity, demand.Quantity - shortfall, demand.ExcessAllowance, demand.MaxUnitPrice));
        }

        itemIds.Sort();
        return new DemandSet
        {
            Demands = built.ToImmutable(),
            ItemIds = [.. itemIds],
            TargetsMet = met,
            TargetsUnknown = unknown,
        };
    }

    public static DemandQuality ToDemandQuality(ItemQuality quality) => quality switch
    {
        ItemQuality.HighQuality => DemandQuality.HighQuality,
        ItemQuality.NormalQuality => DemandQuality.NormalQuality,
        _ => DemandQuality.Any,
    };
}
