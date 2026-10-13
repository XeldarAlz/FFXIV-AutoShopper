using System.Collections.Immutable;

namespace AutoShopper.Core.Pricing.Model;

// A preset names the worlds a run may shop on; the picker then removes worlds the preset holds and adds worlds it
// does not, so a world shows as included exactly when it is in the resolved scope.
internal static class ScopeResolver
{
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

    public static ImmutableArray<ScopeWorld> Resolve(
        ScopePreset preset,
        in ScopeWorld current,
        in ScopeWorld home,
        ReadOnlySpan<ScopeWorld> candidates,
        IReadOnlyList<uint> added,
        IReadOnlyList<uint> excluded)
    {
        var resolved = ImmutableArray.CreateBuilder<ScopeWorld>(candidates.Length);
        for (var index = 0; index < candidates.Length; index++)
        {
            var world = candidates[index];
            if (!world.IsKnown || !IsIncluded(preset, world, current, home, added, excluded))
            {
                continue;
            }

            resolved.Add(world);
        }

        return resolved.ToImmutable();
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
