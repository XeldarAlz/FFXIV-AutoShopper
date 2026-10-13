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
            Candidates(current.Region),
            configuration.AllowedWorlds,
            configuration.ExcludedWorlds,
            configuration.AllowJapaneseDataCenters);
    }

    // Travel never leaves the region the character stands in, so the picker offers only that region's worlds, also
    // while the character visits a region other than its home's. The sheet interleaves data centers, so the worlds are
    // sorted to keep each data center together.
    private static ImmutableArray<ScopeWorld> Candidates(WorldRegion currentRegion)
    {
        var worlds = Worlds.InRegion(currentRegion);
        var candidates = new ScopeWorld[worlds.Length];
        for (var index = 0; index < worlds.Length; index++)
        {
            candidates[index] = From(worlds[index]);
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
