using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using AutoShopper.Core.Planning;

namespace AutoShopper.Tests;

internal static class Fixtures
{
    public const uint PrimaryDataCenter = 10;
    public const uint SecondaryDataCenter = 20;
    public const uint TertiaryDataCenter = 30;
    public const uint Region = 1;

    public static readonly PlanWorld Alpha = new(1, PrimaryDataCenter, Region, "Alpha");
    public static readonly PlanWorld Beta = new(2, PrimaryDataCenter, Region, "Beta");
    public static readonly PlanWorld Gamma = new(3, PrimaryDataCenter, Region, "Gamma");
    public static readonly PlanWorld Delta = new(4, SecondaryDataCenter, Region, "Delta");
    public static readonly PlanWorld Epsilon = new(5, SecondaryDataCenter, Region, "Epsilon");
    public static readonly PlanWorld Zeta = new(6, TertiaryDataCenter, Region, "Zeta");

    public static readonly ImmutableArray<PlanWorld> AllWorlds = [Alpha, Beta, Gamma, Delta, Epsilon, Zeta];

    public static readonly DateTime ObservedAt = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    public static PlanOffer Offer(PlanWorld world, uint itemId, int quantity, long unitPrice, bool isHq = false, string? key = null)
    {
        var listingKey = key ?? string.Create(
            CultureInfo.InvariantCulture,
            $"{world.Name}-{itemId}-{(isHq ? "hq" : "nq")}-{quantity}x{unitPrice}");
        Assert.True(PlanOffer.TryCreateEstimate(world.WorldId, world.DataCenterId, itemId, isHq, quantity, unitPrice, listingKey, ObservedAt, out var offer));
        return offer;
    }

    public static PlanSettings Settings(PlanMode mode = PlanMode.Cheapest, PlanWorld? current = null, PlanWorld? home = null, bool returnHome = false)
    {
        var currentWorld = current ?? Alpha;
        return new PlanSettings
        {
            Mode = mode,
            CurrentWorld = currentWorld,
            HomeWorld = home ?? currentWorld,
            ReturnHome = returnHome,
            SearchTimeBudget = TimeSpan.FromMinutes(5),
        };
    }

    public static PlanRequest Request(PlanSettings settings, ImmutableArray<PlanDemand> demands, params PlanOffer[] offers)
    {
        return new PlanRequest
        {
            Demands = demands,
            Offers = [.. offers],
            Worlds = AllWorlds,
            Settings = settings,
        };
    }

    public static Planner NewPlanner()
    {
        return new Planner(new FixedTimeProvider());
    }

    public static IEnumerable<PlanLine> Lines(PurchasePlan plan)
    {
        for (var stopIndex = 0; stopIndex < plan.Stops.Length; stopIndex++)
        {
            var lines = plan.Stops[stopIndex].Lines;
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                yield return lines[lineIndex];
            }
        }
    }

    public static int PlannedQuantity(PurchasePlan plan, DemandKey demand)
    {
        var total = 0;
        foreach (var line in Lines(plan))
        {
            if (line.Demand == demand)
            {
                total += line.Offer.Quantity;
            }
        }

        return total;
    }

    public static uint[] StopWorldIds(PurchasePlan plan)
    {
        var ids = new uint[plan.Stops.Length];
        for (var index = 0; index < ids.Length; index++)
        {
            ids[index] = plan.Stops[index].World.WorldId;
        }

        return ids;
    }

    public static UnresolvedDemand Unresolved(PurchasePlan plan, DemandKey demand)
    {
        for (var index = 0; index < plan.Unresolved.Length; index++)
        {
            if (plan.Unresolved[index].Demand == demand)
            {
                return plan.Unresolved[index];
            }
        }

        throw new Xunit.Sdk.XunitException($"No unresolved entry for {demand}");
    }

    public static string Describe(PurchasePlan plan)
    {
        var builder = new StringBuilder();
        for (var stopIndex = 0; stopIndex < plan.Stops.Length; stopIndex++)
        {
            var stop = plan.Stops[stopIndex];
            builder.Append(CultureInfo.InvariantCulture, $"stop {stop.World.WorldId} {stop.PurchaseEstimate}\n");
            for (var lineIndex = 0; lineIndex < stop.Lines.Length; lineIndex++)
            {
                var line = stop.Lines[lineIndex];
                builder.Append(CultureInfo.InvariantCulture, $"  {line.Demand.ItemId}/{line.Demand.Quality} {line.Offer.ListingKey} {line.Offer.Quantity} {line.Payable}\n");
            }
        }

        for (var index = 0; index < plan.Unresolved.Length; index++)
        {
            var entry = plan.Unresolved[index];
            builder.Append(CultureInfo.InvariantCulture, $"missing {entry.Demand.ItemId}/{entry.Demand.Quality} {entry.CoveredQuantity}+{entry.MissingQuantity} {entry.Reason}\n");
        }

        builder.Append(CultureInfo.InvariantCulture, $"totals {plan.Totals}\n");
        return builder.ToString();
    }

    public static ImmutableArray<T> Shuffled<T>(ImmutableArray<T> source, Random random)
    {
        var copy = source.AsSpan().ToArray();
        random.Shuffle(copy);
        return [.. copy];
    }
}

internal sealed class FixedTimeProvider : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        return new DateTimeOffset(Fixtures.ObservedAt);
    }

    public override long GetTimestamp()
    {
        return 0;
    }
}

internal sealed class SteppingTimeProvider(TimeSpan step) : TimeProvider
{
    private long now;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow()
    {
        return new DateTimeOffset(Fixtures.ObservedAt);
    }

    public override long GetTimestamp()
    {
        now += step.Ticks;
        return now;
    }
}
