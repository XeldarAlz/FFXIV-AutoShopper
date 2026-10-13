using System.Collections.Immutable;
using AutoShopper.Core.Planning;

namespace AutoShopper.Core.Execution.Model;

internal enum RouteStatus : byte
{
    Ahead = 0,
    Current = 1,
    Done = 2,
    Skipped = 3,
}

internal enum RouteSkip : byte
{
    None = 0,
    // A replan covered its lines elsewhere or found nothing left to buy there.
    NotNeeded = 1,
    TravelFailed = 2,
    TravelHalted = 3,
    StopLimit = 4,
}

internal readonly record struct RouteEntry(PlanStop Stop, RouteStatus Status, RouteSkip Skip, TravelFailure Failure)
{
    public PlanWorld World => Stop.World;

    public bool IsAhead => Status is RouteStatus.Ahead or RouteStatus.Current;
}

// The stops of a run in visit order: the ones behind it, done or skipped with their reason, then the one under way,
// then the ones the newest plan still holds. A replan replaces only what lies ahead; the stops behind keep their place.
internal sealed class RunRoute
{
    private readonly List<RouteEntry> entries = [];

    private RunRoute()
    {
    }

    public int Revision { get; private set; }

    public int Count => entries.Count;

    public RouteEntry this[int index] => entries[index];

    public static RunRoute Start(ImmutableArray<PlanStop> stops)
    {
        var route = new RunRoute();
        route.AppendAhead(stops);
        return route;
    }

    // The stop under way or, when none is, the first one ahead; -1 when nothing is left.
    public int NextIndex()
    {
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].Status == RouteStatus.Current)
            {
                return index;
            }
        }

        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].Status == RouteStatus.Ahead)
            {
                return index;
            }
        }

        return -1;
    }

    // The first stop after the one under way, -1 when none follows.
    public int FollowingIndex()
    {
        var next = NextIndex();
        if (next < 0)
        {
            return -1;
        }

        for (var index = next + 1; index < entries.Count; index++)
        {
            if (entries[index].Status == RouteStatus.Ahead)
            {
                return index;
            }
        }

        return -1;
    }

    public void Begin(int index) => SetStatus(index, RouteStatus.Current, RouteSkip.None, TravelFailure.None);

    public void Complete(int index) => SetStatus(index, RouteStatus.Done, RouteSkip.None, TravelFailure.None);

    public void Skip(int index, RouteSkip skip, TravelFailure failure) => SetStatus(index, RouteStatus.Skipped, skip, failure);

    // Every stop still ahead is given up for the same reason.
    public void SkipAhead(RouteSkip skip, TravelFailure failure)
    {
        var changed = false;
        for (var index = 0; index < entries.Count; index++)
        {
            if (!entries[index].IsAhead)
            {
                continue;
            }

            entries[index] = entries[index] with { Status = RouteStatus.Skipped, Skip = skip, Failure = failure };
            changed = true;
        }

        if (changed)
        {
            Revision++;
        }
    }

    // What lies ahead becomes the new plan's stops in the new plan's order. A stop ahead that the new plan dropped stays
    // in view as skipped, no longer needed, unless a later plan brings its world back.
    public void ReplaceAhead(ImmutableArray<PlanStop> stops)
    {
        var kept = new List<RouteEntry>(entries.Count + stops.Length);
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.Status is RouteStatus.Done or RouteStatus.Skipped)
            {
                if (entry.Skip == RouteSkip.NotNeeded && Contains(stops, entry.World.WorldId))
                {
                    continue;
                }

                kept.Add(entry);
            }
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.IsAhead && !Contains(stops, entry.World.WorldId))
            {
                kept.Add(entry with { Status = RouteStatus.Skipped, Skip = RouteSkip.NotNeeded, Failure = TravelFailure.None });
            }
        }

        entries.Clear();
        entries.AddRange(kept);
        AppendAhead(stops);
    }

    public int CountWithStatus(RouteStatus status)
    {
        var count = 0;
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].Status == status)
            {
                count++;
            }
        }

        return count;
    }

    public bool IsAheadOn(uint worldId)
    {
        for (var index = 0; index < entries.Count; index++)
        {
            if (entries[index].IsAhead && entries[index].World.WorldId == worldId)
            {
                return true;
            }
        }

        return false;
    }

    private void AppendAhead(ImmutableArray<PlanStop> stops)
    {
        if (!stops.IsDefault)
        {
            for (var index = 0; index < stops.Length; index++)
            {
                entries.Add(new RouteEntry(stops[index], RouteStatus.Ahead, RouteSkip.None, TravelFailure.None));
            }
        }

        Revision++;
    }

    private void SetStatus(int index, RouteStatus status, RouteSkip skip, TravelFailure failure)
    {
        if ((uint)index >= (uint)entries.Count)
        {
            return;
        }

        entries[index] = entries[index] with { Status = status, Skip = skip, Failure = failure };
        Revision++;
    }

    private static bool Contains(ImmutableArray<PlanStop> stops, uint worldId)
    {
        if (stops.IsDefault)
        {
            return false;
        }

        for (var index = 0; index < stops.Length; index++)
        {
            if (stops[index].World.WorldId == worldId)
            {
                return true;
            }
        }

        return false;
    }
}
