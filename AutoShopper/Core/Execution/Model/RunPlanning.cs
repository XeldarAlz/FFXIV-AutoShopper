using System.Collections.Immutable;
using System.Runtime.InteropServices;
using AutoShopper.Core.Planning;

namespace AutoShopper.Core.Execution.Model;

internal static class RunPlanning
{
    // The plan's demand with duplicate lines merged and lines that need nothing dropped, the shape an authorization
    // freezes.
    public static ImmutableArray<PlanDemand> AuthorizedDemands(ImmutableArray<PlanDemand> planDemands)
    {
        var open = DemandNormalizer.Normalize(planDemands).OpenLines();
        return ImmutableCollectionsMarshal.AsImmutableArray(open);
    }

    // The authorized demand of one item, the lines a single search of that item serves.
    public static ImmutableArray<PlanDemand> DemandsOfItem(RunAuthorization authorization, uint itemId)
    {
        var demands = authorization.Demands;
        var count = 0;
        for (var index = 0; index < demands.Length; index++)
        {
            if (demands[index].ItemId == itemId)
            {
                count++;
            }
        }

        var found = new PlanDemand[count];
        var write = 0;
        for (var index = 0; index < demands.Length; index++)
        {
            if (demands[index].ItemId == itemId)
            {
                found[write] = demands[index];
                write++;
            }
        }

        return ImmutableCollectionsMarshal.AsImmutableArray(found);
    }

    public static PlanRequest RemainingRequest(
        RunAuthorization authorization,
        RunTally tally,
        ImmutableArray<PlanOffer> offers,
        ImmutableArray<PlanWorld> worlds,
        PlanSettings settings,
        ImmutableArray<string> excludedListingKeys)
    {
        var outstanding = ImmutableArray.CreateBuilder<PlanDemand>(authorization.Demands.Length);
        for (var index = 0; index < authorization.Demands.Length; index++)
        {
            var demand = tally.Outstanding(authorization.Demands[index].Key);
            if (demand.RequiredQuantity > 0)
            {
                outstanding.Add(demand);
            }
        }

        return new PlanRequest
        {
            Demands = outstanding.ToImmutable(),
            Offers = offers.IsDefault ? [] : offers,
            Worlds = AuthorizedWorlds(authorization, worlds),
            Settings = settings,
            ExcludedListingKeys = excludedListingKeys.IsDefault ? [] : excludedListingKeys,
        };
    }

    public static ImmutableArray<PlanWorld> AuthorizedWorlds(RunAuthorization authorization, ImmutableArray<PlanWorld> worlds)
    {
        var allowed = ImmutableArray.CreateBuilder<PlanWorld>(worlds.IsDefault ? 0 : worlds.Length);
        if (!worlds.IsDefault)
        {
            for (var index = 0; index < worlds.Length; index++)
            {
                if (authorization.AllowsWorld(worlds[index].WorldId))
                {
                    allowed.Add(worlds[index]);
                }
            }
        }

        return allowed.ToImmutable();
    }

    public static ImmutableArray<uint> ItemsOf(PlanStop stop)
    {
        var items = new List<uint>(stop.Lines.Length);
        for (var index = 0; index < stop.Lines.Length; index++)
        {
            var itemId = stop.Lines[index].Demand.ItemId;
            if (!items.Contains(itemId))
            {
                items.Add(itemId);
            }
        }

        return [.. items];
    }
}
