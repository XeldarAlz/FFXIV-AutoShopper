using System.Collections.Immutable;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Pricing.Model;

namespace AutoShopper.Core.Preview.Model;

// Everything one preview reads, captured on the framework thread and never changed afterwards, so the planning
// thread works on a copy the window and the game can no longer touch.
internal sealed record PreviewInput
{
    public required long Generation { get; init; }

    public required Guid ListId { get; init; }

    public required DemandSet Demands { get; init; }

    public required PriceSnapshot? Prices { get; init; }

    public required ImmutableArray<PlanWorld> Worlds { get; init; }

    public required PlanSettings Settings { get; init; }
}

internal sealed class PreviewResult
{
    public required PreviewInput Input { get; init; }

    // Indexed by PlanMode; the plan for the chosen mode and its two alternatives on the same data.
    public required ImmutableArray<PurchasePlan> Plans { get; init; }

    public required DateTime PlannedAtUtc { get; init; }

    public long Generation => Input.Generation;

    public PlanMode Mode => Input.Settings.Mode;

    public PurchasePlan Selected => Plans[(int)Input.Settings.Mode];

    public PurchasePlan For(PlanMode mode) => Plans[(int)mode];
}
