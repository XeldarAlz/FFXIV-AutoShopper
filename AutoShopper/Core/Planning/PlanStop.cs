using System.Collections.Immutable;

namespace AutoShopper.Core.Planning;

internal sealed record PlanStop(PlanWorld World, ImmutableArray<PlanLine> Lines, long PurchaseEstimate, long FeeEstimate, int Quantity);
