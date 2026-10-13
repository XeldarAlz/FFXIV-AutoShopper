using System.Collections.Immutable;
using System.Threading;
using AutoShopper.Core.Planning;

namespace AutoShopper.Core.Execution.Model;

internal sealed record RouteReplanInput
{
    public required PurchasePlan RunPlan { get; init; }

    public required RunAuthorization Authorization { get; init; }

    public required ImmutableArray<ConfirmedPurchase> Acquisitions { get; init; }

    public required ImmutableArray<string> ExcludedListingKeys { get; init; }

    public required ImmutableArray<PlanOffer> Offers { get; init; }

    // The worlds the run planned on when it was authorized; a replan never reaches past them.
    public required ImmutableArray<PlanWorld> RunWorlds { get; init; }

    public required RunRoute Route { get; init; }

    // Every world the run has shopped, before a restart included.
    public required IReadOnlyList<uint> CompletedWorldIds { get; init; }

    // Stops shopped so far, revisits counted again.
    public required int StopsMade { get; init; }

    public required TravelExclusions Exclusions { get; init; }

    public required PlanWorld CurrentWorld { get; init; }

    // How many times this run may still go back to a world it already shopped.
    public required int RevisitsLeft { get; init; }

    // The run's most stops setting, 0 for no limit.
    public required int MaxStops { get; init; }
}

// Everything a replan reads, copied out of the run on the framework thread so the planner can run off it.
internal sealed record PreparedReplan(
    PurchasePlan RunPlan,
    RunAuthorization Authorization,
    ImmutableArray<ConfirmedPurchase> Acquisitions,
    ImmutableArray<string> ExcludedListingKeys,
    ImmutableArray<PlanOffer> Offers,
    ImmutableArray<PlanWorld> Eligible,
    ImmutableArray<PlanWorld> Ahead,
    PlanWorld CurrentWorld,
    int StopsLeft);

// Plans what is left of a run from where the character stands, between stops. It plans the outstanding demand of the
// frozen authorization on the run's own price snapshot, so quantities, caps, qualities and worlds can only shrink. The
// stops already ahead are tried first; a wider plan wins only by leaving fewer units uncovered.
internal static class RouteReplanning
{
    public static PurchasePlan? Replan(Planner planner, RouteReplanInput input, CancellationToken cancellationToken = default)
        => Prepare(input) is { } prepared ? Plan(planner, prepared, cancellationToken) : null;

    // Null when no stop is left in the most stops setting or no world is left to plan on.
    public static PreparedReplan? Prepare(RouteReplanInput input)
    {
        if (!TryStopsLeft(input.MaxStops, input.StopsMade, out var stopsLeft))
        {
            return null;
        }

        var eligible = EligibleWorlds(input.RunWorlds, input.CompletedWorldIds, input.Exclusions, input.RevisitsLeft, input.Authorization);
        if (eligible.IsEmpty)
        {
            return null;
        }

        var ahead = AheadWorlds(eligible, input.Route, input.CurrentWorld.WorldId);
        return new PreparedReplan(input.RunPlan, input.Authorization, input.Acquisitions, input.ExcludedListingKeys, input.Offers, eligible, ahead, input.CurrentWorld, stopsLeft);
    }

    public static PurchasePlan Plan(Planner planner, PreparedReplan prepared, CancellationToken cancellationToken = default)
    {
        var ahead = prepared.Ahead;
        var preferred = ahead.IsEmpty ? null : PlanOver(planner, prepared, ahead, cancellationToken);
        if (preferred is not null && (preferred.IsComplete || ahead.Length == prepared.Eligible.Length))
        {
            return preferred;
        }

        var wider = PlanOver(planner, prepared, prepared.Eligible, cancellationToken);
        return preferred is null ? wider : Prefer(preferred, wider);
    }

    // The run's worlds less the ones travel gave up on, and less the ones already shopped unless revisits remain.
    public static ImmutableArray<PlanWorld> EligibleWorlds(ImmutableArray<PlanWorld> runWorlds, IReadOnlyList<uint> completedWorldIds, TravelExclusions exclusions, int revisitsLeft, RunAuthorization authorization)
    {
        if (runWorlds.IsDefaultOrEmpty)
        {
            return ImmutableArray<PlanWorld>.Empty;
        }

        var eligible = ImmutableArray.CreateBuilder<PlanWorld>(runWorlds.Length);
        for (var index = 0; index < runWorlds.Length; index++)
        {
            var world = runWorlds[index];
            if (!authorization.AllowsWorld(world.WorldId) || exclusions.IsExcluded(world))
            {
                continue;
            }

            if (revisitsLeft <= 0 && Contains(completedWorldIds, world.WorldId))
            {
                continue;
            }

            eligible.Add(world);
        }

        return eligible.ToImmutable();
    }

    // The eligible worlds the route already heads for, and the world the character stands on, since staying adds no stop.
    public static ImmutableArray<PlanWorld> AheadWorlds(ImmutableArray<PlanWorld> eligible, RunRoute route, uint currentWorldId)
    {
        var ahead = ImmutableArray.CreateBuilder<PlanWorld>(eligible.Length);
        for (var index = 0; index < eligible.Length; index++)
        {
            var world = eligible[index];
            if (world.WorldId == currentWorldId || route.IsAheadOn(world.WorldId))
            {
                ahead.Add(world);
            }
        }

        return ahead.ToImmutable();
    }

    public static PurchasePlan Prefer(PurchasePlan ahead, PurchasePlan wider)
        => wider.Totals.MissingQuantity < ahead.Totals.MissingQuantity ? wider : ahead;

    // stopsLeft is the value for the planner's most stops setting, 0 meaning no limit; false once the limit is used up.
    public static bool TryStopsLeft(int maxStops, int stopsMade, out int stopsLeft)
    {
        if (maxStops <= 0)
        {
            stopsLeft = 0;
            return true;
        }

        stopsLeft = maxStops - Math.Max(0, stopsMade);
        return stopsLeft > 0;
    }

    private static PurchasePlan PlanOver(Planner planner, PreparedReplan input, ImmutableArray<PlanWorld> worlds, CancellationToken cancellationToken)
    {
        var runPlan = input.RunPlan;
        var demands = input.Authorization.Demands;
        var previous = runPlan with
        {
            AuthorizedDemands = demands,
            Request = runPlan.Request with
            {
                Demands = demands,
                Offers = [],
                Worlds = worlds,
                Settings = runPlan.Request.Settings with { MaxStops = input.StopsLeft },
                ExcludedListingKeys = [],
            },
        };
        return planner.Replan(previous, input.Acquisitions, input.ExcludedListingKeys, input.CurrentWorld, input.Offers, cancellationToken);
    }

    private static bool Contains(IReadOnlyList<uint> worldIds, uint worldId)
    {
        for (var index = 0; index < worldIds.Count; index++)
        {
            if (worldIds[index] == worldId)
            {
                return true;
            }
        }

        return false;
    }
}
