using System.Collections.Immutable;

namespace AutoShopper.Core.Pricing.Model;

// A preset names the worlds a run may shop on; the picker then removes worlds the preset holds and adds worlds it
// does not, so a world shows as included exactly when it is in the resolved scope.
internal static class ScopeResolver
{
    // The game's region column numbers Japan 1.
    public const byte JapanRegion = 1;

    public static bool InPreset(ScopePreset preset, in ScopeWorld world, in ScopeWorld current, in ScopeWorld home) => preset switch
    {
        ScopePreset.CurrentWorld => world.WorldId == current.WorldId,
        ScopePreset.CurrentDataCenter => world.DataCenterId == current.DataCenterId,
        ScopePreset.Region => world.RegionId == home.RegionId,
        _ => world.DataCenterId == home.DataCenterId,
    };

    public static bool IsIncluded(
        ScopePreset preset,
        in ScopeWorld world,
        in ScopeWorld current,
        in ScopeWorld home,
        IReadOnlyList<uint> added,
        IReadOnlyList<uint> excluded)
    {
        return InPreset(preset, world, current, home)
            ? !Contains(excluded, world.WorldId)
            : Contains(added, world.WorldId);
    }

    // World Visit between Japanese data centers changed on 13 October 2026 and that route is untested, so while the
    // setting is off a Japanese world outside the character's own data center stays out of every scope.
    public static bool IsLeftOut(in ScopeWorld world, in ScopeWorld current, bool allowJapanese)
        => !allowJapanese && world.RegionId == JapanRegion && world.DataCenterId != current.DataCenterId;

    // Travel never leaves the region the character stands in, so no run can reach a world of another region.
    public static bool IsOutsideRegion(in ScopeWorld world, in ScopeWorld current) => world.RegionId != current.RegionId;

    public static ImmutableArray<ScopeWorld> Resolve(
        ScopePreset preset,
        in ScopeWorld current,
        in ScopeWorld home,
        ReadOnlySpan<ScopeWorld> candidates,
        IReadOnlyList<uint> added,
        IReadOnlyList<uint> excluded,
        bool allowJapanese)
    {
        var resolved = ImmutableArray.CreateBuilder<ScopeWorld>(candidates.Length);
        for (var index = 0; index < candidates.Length; index++)
        {
            var world = candidates[index];
            if (!world.IsKnown || IsOutsideRegion(world, current) || IsLeftOut(world, current, allowJapanese) || !IsIncluded(preset, world, current, home, added, excluded))
            {
                continue;
            }

            resolved.Add(world);
        }

        return resolved.ToImmutable();
    }

    // The worlds the picker would include that IsLeftOut keeps out.
    public static int CountLeftOut(
        ScopePreset preset,
        in ScopeWorld current,
        in ScopeWorld home,
        ReadOnlySpan<ScopeWorld> candidates,
        IReadOnlyList<uint> added,
        IReadOnlyList<uint> excluded,
        bool allowJapanese)
    {
        var count = 0;
        for (var index = 0; index < candidates.Length; index++)
        {
            var world = candidates[index];
            if (world.IsKnown && !IsOutsideRegion(world, current) && IsLeftOut(world, current, allowJapanese) && IsIncluded(preset, world, current, home, added, excluded))
            {
                count++;
            }
        }

        return count;
    }

    // A world sits on at most one of the two lists, so the last click decides it whatever preset is chosen later.
    // True when either list changed.
    public static bool SetIncluded(List<uint> added, List<uint> excluded, uint worldId, bool inPreset, bool included)
    {
        var list = inPreset ? excluded : added;
        var wantListed = inPreset ? !included : included;
        if (!wantListed)
        {
            return RemoveEvery(list, worldId);
        }

        var changed = RemoveEvery(inPreset ? added : excluded, worldId);
        if (Contains(list, worldId))
        {
            return changed;
        }

        list.Add(worldId);
        return true;
    }

    private static bool RemoveEvery(List<uint> worldIds, uint worldId)
    {
        var removed = false;
        for (var index = worldIds.Count - 1; index >= 0; index--)
        {
            if (worldIds[index] != worldId)
            {
                continue;
            }

            worldIds.RemoveAt(index);
            removed = true;
        }

        return removed;
    }

    public static bool Contains(IReadOnlyList<uint> worldIds, uint worldId)
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
