using System.Collections.Immutable;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Pricing.Model;

namespace AutoShopper.Core.Execution.Model;

// The estimates a run plans what is left on: its own copy, taken from the snapshot it was authorized with. A refresh
// merges into this copy and never into the snapshot the windows show, so the Plan page keeps every world after a run.
internal sealed class RunEstimates
{
    public required ImmutableArray<PlanOffer> Offers { get; init; }

    public required DateTime FetchedAtUtc { get; init; }

    public static RunEstimates From(PriceSnapshot snapshot) => new()
    {
        Offers = snapshot.Offers.IsDefault ? ImmutableArray<PlanOffer>.Empty : snapshot.Offers,
        FetchedAtUtc = snapshot.FetchedAtUtc,
    };

    // World by world: the offers of an item the refresh answered for on a world it covered are replaced by the
    // refresh's. An item whose request failed keeps what the copy held, and so does every world the refresh left out.
    public RunEstimates MergedWith(PriceSnapshot refresh)
    {
        var answered = AnsweredItems(refresh.Items);
        if (answered.Count == 0)
        {
            return this;
        }

        var refreshed = refresh.Offers.IsDefault ? ImmutableArray<PlanOffer>.Empty : refresh.Offers;
        var merged = ImmutableArray.CreateBuilder<PlanOffer>(Offers.Length + refreshed.Length);
        for (var index = 0; index < Offers.Length; index++)
        {
            var offer = Offers[index];
            if (!answered.Contains(offer.ItemId) || PriceSnapshot.IndexOfWorld(refresh.Worlds, offer.WorldId) < 0)
            {
                merged.Add(offer);
            }
        }

        for (var index = 0; index < refreshed.Length; index++)
        {
            if (answered.Contains(refreshed[index].ItemId))
            {
                merged.Add(refreshed[index]);
            }
        }

        return new RunEstimates
        {
            Offers = merged.ToImmutable(),
            FetchedAtUtc = refresh.FetchedAtUtc,
        };
    }

    private static HashSet<uint> AnsweredItems(ImmutableArray<ItemPrices> items)
    {
        var answered = new HashSet<uint>();
        if (items.IsDefault)
        {
            return answered;
        }

        for (var index = 0; index < items.Length; index++)
        {
            if ((items[index].Coverage & PriceCoverageGroups.Failed) == PriceCoverage.None)
            {
                answered.Add(items[index].ItemId);
            }
        }

        return answered;
    }
}
