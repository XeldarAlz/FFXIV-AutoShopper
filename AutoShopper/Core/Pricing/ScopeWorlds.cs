using System.Collections.Immutable;
using System.Runtime.InteropServices;
using AutoShopper.Core.Pricing.Model;
using AutoShopper.Core.Travel;

namespace AutoShopper.Core.Pricing;

// Bridges the game's world sheet to the scope the planner and the price fetch read. Framework thread only.
internal static class ScopeWorlds
{
    public static ScopeWorld From(in WorldInfo world)
        => new(world.Id, world.Name, world.DataCenterId, world.DataCenterName, (byte)world.Region);

    public static ResolvedScope? Resolve(Configuration configuration)
    {
        if (!Worlds.TryCurrent(out var current) || !Worlds.TryHome(out var home))
        {
            return null;
        }

        return ResolvedScope.Create(
            configuration.ScopePreset,
            From(current),
            From(home),
            Candidates(home.Region, current.Region),
            configuration.AllowedWorlds,
            configuration.ExcludedWorlds,
            configuration.AllowJapaneseDataCenters);
    }

    // A character visiting another region (Japan and Oceania share travel) may also shop where it stands. The sheet
    // interleaves data centers, so the worlds are sorted to keep each data center together.
    private static ImmutableArray<ScopeWorld> Candidates(WorldRegion homeRegion, WorldRegion currentRegion)
    {
        var home = Worlds.InRegion(homeRegion);
        var visited = currentRegion == homeRegion ? [] : Worlds.InRegion(currentRegion);
        var candidates = new ScopeWorld[home.Length + visited.Length];
        for (var index = 0; index < home.Length; index++)
        {
            candidates[index] = From(home[index]);
        }

        for (var index = 0; index < visited.Length; index++)
        {
            candidates[home.Length + index] = From(visited[index]);
        }

        Array.Sort(candidates, CompareForPicker);
        return ImmutableCollectionsMarshal.AsImmutableArray(candidates);
    }

    private static int CompareForPicker(ScopeWorld left, ScopeWorld right)
    {
        var order = left.RegionId.CompareTo(right.RegionId);
        if (order != 0)
        {
            return order;
        }

        order = left.DataCenterId.CompareTo(right.DataCenterId);
        return order != 0 ? order : string.CompareOrdinal(left.Name, right.Name);
    }
}
