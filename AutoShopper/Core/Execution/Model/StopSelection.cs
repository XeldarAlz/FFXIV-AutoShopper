using System.Collections.Immutable;
using System.Threading;
using AutoShopper.Core.Market.Model;
using AutoShopper.Core.Planning;

namespace AutoShopper.Core.Execution.Model;

internal readonly record struct ListingChoice(DemandKey Demand, ObservedListing Listing, long Payable);

internal sealed class StopChoice
{
    public static readonly StopChoice Empty = new() { Choices = [], Unresolved = [] };

    // In the order the planner listed them.
    public required ImmutableArray<ListingChoice> Choices { get; init; }

    public required ImmutableArray<UnresolvedDemand> Unresolved { get; init; }
}

// Picks the listings to buy at one stop from the board's own copy of one item's listings. The planner sees only this
// world, only the demand of this item that is still open, and never a listing the run left out, so a stale Universalis
// estimate cannot bring a refused listing back. Listings the guard would refuse anyway are not offered to it.
internal static class StopSelection
{
    public static StopChoice Choose(
        Planner planner,
        PurchasePlan runPlan,
        ImmutableArray<PlanDemand> itemDemands,
        ImmutableArray<ConfirmedPurchase> acquisitions,
        ImmutableArray<string> excludedListingKeys,
        PlanWorld world,
        ImmutableArray<ObservedListing> listings,
        ImmutableArray<ListingMarks> marks,
        CancellationToken cancellationToken = default)
    {
        if (itemDemands.IsDefaultOrEmpty || listings.IsDefaultOrEmpty)
        {
            return Unresolved(itemDemands, acquisitions);
        }

        var offers = OffersFrom(itemDemands, listings, marks, world);
        var previous = runPlan with
        {
            AuthorizedDemands = itemDemands,
            Request = runPlan.Request with
            {
                Demands = itemDemands,
                Offers = [],
                Worlds = [world],
                ExcludedListingKeys = [],
            },
        };
        var plan = planner.Replan(previous, acquisitions, excludedListingKeys, world, offers, cancellationToken);
        return new StopChoice
        {
            Choices = ChoicesAt(plan, world.WorldId, listings),
            Unresolved = plan.Unresolved.IsDefault ? [] : plan.Unresolved,
        };
    }

    public static ObservedListing Normalized(in ObservedListing listing)
        => listing with { ItemId = MarketItemId.Normalize(listing.ItemId) };

    private static ImmutableArray<PlanOffer> OffersFrom(ImmutableArray<PlanDemand> itemDemands, ImmutableArray<ObservedListing> listings, ImmutableArray<ListingMarks> marks, PlanWorld world)
    {
        var offers = ImmutableArray.CreateBuilder<PlanOffer>(listings.Length);
        for (var index = 0; index < listings.Length; index++)
        {
            if (!marks.IsDefault && index < marks.Length && marks[index] != ListingMarks.None)
            {
                continue;
            }

            var listing = Normalized(listings[index]);
            if (listing.Quantity <= 0 || listing.WorldId != world.WorldId || !Wanted(itemDemands, listing.ItemId))
            {
                continue;
            }

            offers.Add(listing.ToPlanOffer(world.DataCenterId));
        }

        return offers.ToImmutable();
    }

    private static bool Wanted(ImmutableArray<PlanDemand> itemDemands, uint itemId)
    {
        for (var index = 0; index < itemDemands.Length; index++)
        {
            if (itemDemands[index].ItemId == itemId)
            {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<ListingChoice> ChoicesAt(PurchasePlan plan, uint worldId, ImmutableArray<ObservedListing> listings)
    {
        for (var stopIndex = 0; stopIndex < plan.Stops.Length; stopIndex++)
        {
            var stop = plan.Stops[stopIndex];
            if (stop.World.WorldId != worldId)
            {
                continue;
            }

            var choices = ImmutableArray.CreateBuilder<ListingChoice>(stop.Lines.Length);
            for (var lineIndex = 0; lineIndex < stop.Lines.Length; lineIndex++)
            {
                var line = stop.Lines[lineIndex];
                if (TryFind(listings, line.Offer.ListingKey, out var listing))
                {
                    choices.Add(new ListingChoice(line.Demand, listing, line.Payable));
                }
            }

            return choices.ToImmutable();
        }

        return [];
    }

    private static bool TryFind(ImmutableArray<ObservedListing> listings, string listingKey, out ObservedListing listing)
    {
        for (var index = 0; index < listings.Length; index++)
        {
            if (string.Equals(listings[index].ToListingKey(), listingKey, StringComparison.Ordinal))
            {
                listing = listings[index];
                return true;
            }
        }

        listing = default;
        return false;
    }

    private static StopChoice Unresolved(ImmutableArray<PlanDemand> itemDemands, ImmutableArray<ConfirmedPurchase> acquisitions)
    {
        if (itemDemands.IsDefaultOrEmpty)
        {
            return StopChoice.Empty;
        }

        var unresolved = ImmutableArray.CreateBuilder<UnresolvedDemand>(itemDemands.Length);
        for (var index = 0; index < itemDemands.Length; index++)
        {
            var outstanding = itemDemands[index].ReducedBy(AcquiredFor(acquisitions, itemDemands[index].Key));
            if (outstanding.RequiredQuantity > 0)
            {
                unresolved.Add(new UnresolvedDemand(outstanding.Key, outstanding.RequiredQuantity, 0, outstanding.RequiredQuantity, UnresolvedReason.NoListings));
            }
        }

        return new StopChoice { Choices = [], Unresolved = unresolved.ToImmutable() };
    }

    private static int AcquiredFor(ImmutableArray<ConfirmedPurchase> acquisitions, DemandKey key)
    {
        if (acquisitions.IsDefault)
        {
            return 0;
        }

        var total = 0;
        for (var index = 0; index < acquisitions.Length; index++)
        {
            if (acquisitions[index].Demand == key)
            {
                total += Math.Max(0, acquisitions[index].Quantity);
            }
        }

        return total;
    }
}
