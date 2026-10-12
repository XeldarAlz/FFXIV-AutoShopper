using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace AutoShopper.Core.Planning;

internal static class RouteOrderer
{
    public static ImmutableArray<PlanWorld> Order(ReadOnlySpan<PlanWorld> stops, PlanWorld currentWorld, PlanWorld homeWorld, bool returnHome)
    {
        if (stops.IsEmpty)
        {
            return ImmutableArray<PlanWorld>.Empty;
        }

        var ordered = stops.ToArray();
        Array.Sort(ordered, new StopOrder(currentWorld, homeWorld, returnHome));
        return ImmutableCollectionsMarshal.AsImmutableArray(ordered);
    }

    public static int CountDataCenterChanges(ReadOnlySpan<PlanWorld> orderedStops, PlanWorld currentWorld, PlanWorld homeWorld, bool returnHome)
    {
        var changes = 0;
        var last = currentWorld.DataCenterId;
        for (var index = 0; index < orderedStops.Length; index++)
        {
            var dataCenterId = orderedStops[index].DataCenterId;
            if (dataCenterId == last)
            {
                continue;
            }

            changes++;
            last = dataCenterId;
        }

        if (returnHome && homeWorld.DataCenterId != last)
        {
            changes++;
        }

        return changes;
    }

    public static int CountWorldChanges(ReadOnlySpan<PlanWorld> orderedStops, PlanWorld currentWorld, PlanWorld homeWorld, bool returnHome)
    {
        var changes = 0;
        var last = currentWorld.WorldId;
        for (var index = 0; index < orderedStops.Length; index++)
        {
            var worldId = orderedStops[index].WorldId;
            if (worldId == last)
            {
                continue;
            }

            changes++;
            last = worldId;
        }

        if (returnHome && homeWorld.WorldId != last)
        {
            changes++;
        }

        return changes;
    }

    public static int CountDataCenterChanges(ReadOnlySpan<uint> distinctDataCenters, uint currentDataCenterId, uint homeDataCenterId, bool returnHome)
    {
        var others = 0;
        for (var index = 0; index < distinctDataCenters.Length; index++)
        {
            var dataCenterId = distinctDataCenters[index];
            if (dataCenterId == currentDataCenterId || (returnHome && dataCenterId == homeDataCenterId))
            {
                continue;
            }

            others++;
        }

        if (!returnHome)
        {
            return others;
        }

        if (homeDataCenterId != currentDataCenterId)
        {
            return others + 1;
        }

        return others == 0 ? 0 : others + 1;
    }

    private sealed class StopOrder(PlanWorld currentWorld, PlanWorld homeWorld, bool returnHome) : IComparer<PlanWorld>
    {
        public int Compare(PlanWorld left, PlanWorld right)
        {
            var order = GroupRank(left).CompareTo(GroupRank(right));
            if (order != 0)
            {
                return order;
            }

            order = RegionRank(left).CompareTo(RegionRank(right));
            if (order != 0)
            {
                return order;
            }

            order = left.RegionId.CompareTo(right.RegionId);
            if (order != 0)
            {
                return order;
            }

            order = left.DataCenterId.CompareTo(right.DataCenterId);
            if (order != 0)
            {
                return order;
            }

            order = WorldRank(left).CompareTo(WorldRank(right));
            if (order != 0)
            {
                return order;
            }

            return left.WorldId.CompareTo(right.WorldId);
        }

        // Returning home ends in the home data center anyway, so its stops wait until last even when it is also the current one.
        private int GroupRank(PlanWorld world)
        {
            if (world.WorldId == currentWorld.WorldId)
            {
                return 0;
            }

            if (returnHome && world.DataCenterId == homeWorld.DataCenterId)
            {
                return 2;
            }

            if (world.DataCenterId == currentWorld.DataCenterId)
            {
                return 0;
            }

            return 1;
        }

        private int RegionRank(PlanWorld world)
        {
            return world.RegionId == currentWorld.RegionId ? 0 : 1;
        }

        private int WorldRank(PlanWorld world)
        {
            if (world.WorldId == currentWorld.WorldId)
            {
                return 0;
            }

            if (returnHome && world.WorldId == homeWorld.WorldId)
            {
                return 2;
            }

            return 1;
        }
    }
}
