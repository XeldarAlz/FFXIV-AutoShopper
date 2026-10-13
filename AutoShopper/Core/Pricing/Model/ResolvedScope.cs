using System.Collections.Immutable;
using AutoShopper.Core.Planning;

namespace AutoShopper.Core.Pricing.Model;

// The character's worlds and the worlds the settings allow, read once per change on the framework thread.
internal sealed class ResolvedScope
{
    public required ScopeWorld Current { get; init; }

    public required ScopeWorld Home { get; init; }

    // Every world the picker offers, grouped by data center.
    public required ImmutableArray<ScopeWorld> Candidates { get; init; }

    public required ImmutableArray<ScopeWorld> Allowed { get; init; }

    public required ImmutableArray<PlanWorld> PlanWorlds { get; init; }

    public static ResolvedScope Create(
        ScopePreset preset,
        in ScopeWorld current,
        in ScopeWorld home,
        ImmutableArray<ScopeWorld> candidates,
        IReadOnlyList<uint> added,
        IReadOnlyList<uint> excluded)
    {
        var allowed = ScopeResolver.Resolve(preset, current, home, candidates.AsSpan(), added, excluded);
        var planWorlds = ImmutableArray.CreateBuilder<PlanWorld>(allowed.Length);
        for (var index = 0; index < allowed.Length; index++)
        {
            planWorlds.Add(allowed[index].ToPlanWorld());
        }

        return new ResolvedScope
        {
            Current = current,
            Home = home,
            Candidates = candidates,
            Allowed = allowed,
            PlanWorlds = planWorlds.MoveToImmutable(),
        };
    }
}
