using System.Collections.Immutable;
using AutoShopper.Core.Exports;
using AutoShopper.Core.Planning;
using static AutoShopper.Tests.ExportFixtures;

namespace AutoShopper.Tests;

public sealed class PlanChecklistExporterTests
{
    private static readonly DateTime ExportedAt = Fixtures.ObservedAt.AddHours(2);

    [Fact]
    public void StopsFollowTheVisitOrderWithWorldAndDataCenterHeaders()
    {
        var plan = ShoppingPlan();

        var text = Lines(PlanChecklistExporter.ToText(plan, Names(), ExportedAt));

        Assert.Equal([Fixtures.Alpha.WorldId, Fixtures.Delta.WorldId], Fixtures.StopWorldIds(plan));
        var first = Array.IndexOf(text, "Stop 1 of 2: Alpha (Light)");
        var second = Array.IndexOf(text, "Stop 2 of 2: Delta (Chaos)");
        Assert.True(first > 0);
        Assert.True(second > first);
    }

    [Fact]
    public void ListingsOfOneDemandShareALineWithCountAndSubtotal()
    {
        var plan = ShoppingPlan();

        var text = Lines(PlanChecklistExporter.ToText(plan, Names(), ExportedAt));

        var stop = Array.IndexOf(text, "Stop 1 of 2: Alpha (Light)");
        Assert.Equal("  [ ] 15 x Iron Ore, 2 listings, about 15,750 gil", text[stop + 1]);
        Assert.Equal("  [ ] 2 x Iron Ore (HQ), 1 listing, about 4,200 gil", text[stop + 2]);
        Assert.Equal("  Stop estimate: 19,950 gil", text[stop + 3]);
        Assert.Contains("  [ ] 3 x Linen Cloth (HQ), 1 listing, about 388,886 gil", text);
    }

    [Fact]
    public void LineOrderInsideAStopDoesNotChangeTheChecklist()
    {
        var plan = ShoppingPlan();
        var stops = plan.Stops.AsSpan().ToArray();
        var reversed = stops[0].Lines.AsSpan().ToArray();
        Array.Reverse(reversed);
        stops[0] = stops[0] with { Lines = [.. reversed] };
        var shuffled = plan with { Stops = [.. stops] };

        Assert.Equal(PlanChecklistExporter.ToText(plan, Names(), ExportedAt), PlanChecklistExporter.ToText(shuffled, Names(), ExportedAt));
        Assert.Equal(PlanChecklistExporter.ToMarkdown(plan, Names(), ExportedAt), PlanChecklistExporter.ToMarkdown(shuffled, Names(), ExportedAt));
    }

    [Fact]
    public void UncoveredItemsListMissingQuantitiesAndReasons()
    {
        var plan = ShoppingPlan();

        var text = Lines(PlanChecklistExporter.ToText(plan, Names(), ExportedAt));

        var section = Array.IndexOf(text, "Not covered (1)");
        Assert.True(section > 0);
        Assert.Equal("  Wind Shard: 50 of 50 missing, no listings on the chosen worlds", text[section + 1]);
    }

    [Fact]
    public void TotalsAreLabelledEstimatesWithInvariantThousandsAndPriceAge()
    {
        var plan = ShoppingPlan();
        string text;
        using (new InvariantCultureCheck())
        {
            text = PlanChecklistExporter.ToText(plan, Names(), ExportedAt);
        }

        var lines = Lines(text);
        var section = Array.IndexOf(lines, "Estimated totals (prices 2 h to 5 h old, the board may differ)");
        Assert.True(section > 0);
        Assert.Equal("  Purchases with fees: 408,836 gil", lines[section + 1]);
        Assert.Equal("  Buyer fees included: 19,468 gil", lines[section + 2]);
        Assert.Equal("  Items planned: 20 in 2 stops", lines[section + 3]);
        Assert.Equal("  Missing units: 50", lines[section + 4]);
        Assert.Equal(408_836, plan.Totals.PurchaseEstimate);
        Assert.StartsWith("Shopping checklist, planned 2026-10-13 12:00 UTC", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MarkdownHasOneTablePerStopAndEscapesNames()
    {
        var plan = ShoppingPlan();
        var names = Names().Item(Ore, "Iron | Ore *rare*");

        var markdown = PlanChecklistExporter.ToMarkdown(plan, names, ExportedAt);
        var lines = Lines(markdown);

        Assert.Equal(2, Count(lines, "| Quantity | Item | Listings | Estimate with fee (gil) |"));
        var first = Array.IndexOf(lines, "## Stop 1 of 2: Alpha (Light)");
        var second = Array.IndexOf(lines, "## Stop 2 of 2: Delta (Chaos)");
        Assert.True(first > 0 && second > first);
        Assert.Contains("| 15 | Iron \\| Ore \\*rare\\* | 2 | 15,750 |", lines);
        Assert.Contains("| | **Stop estimate** | | **19,950** |", lines);
        Assert.Contains("| Wind Shard | 50 | 50 | no listings on the chosen worlds |", lines);
        Assert.Contains("- Purchases with fees: **408,836 gil**", lines);
        Assert.Contains("_prices 2 h to 5 h old, the board may differ_", lines);
    }

    [Fact]
    public void ChecklistNeverPrintsListingKeys()
    {
        var plan = ShoppingPlan();

        var text = PlanChecklistExporter.ToText(plan, Names(), ExportedAt);
        var markdown = PlanChecklistExporter.ToMarkdown(plan, Names(), ExportedAt);

        AssertNoIdentifiers(text);
        AssertNoIdentifiers(markdown);
        Assert.DoesNotContain("game:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("game:", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingNamesFallBackToIdsAndPlannedWorldNames()
    {
        var plan = ShoppingPlan();

        var text = Lines(PlanChecklistExporter.ToText(plan, new FakeExportNames(), ExportedAt));

        Assert.Contains("Stop 1 of 2: Alpha (Data center 10)", text);
        Assert.Contains("  [ ] 15 x Item 5000, 2 listings, about 15,750 gil", text);
    }

    [Fact]
    public void ExportsAreFreeOfEmDashes()
    {
        var plan = ShoppingPlan();

        Assert.DoesNotContain(EmDash, PlanChecklistExporter.ToText(plan, Names(), ExportedAt));
        Assert.DoesNotContain(EmDash, PlanChecklistExporter.ToMarkdown(plan, Names(), ExportedAt));
    }

    // Ore splits into an any-quality line over two stacks and an HQ line; cloth sits on another data center; shards have no listings.
    private static PurchasePlan ShoppingPlan()
    {
        ImmutableArray<PlanDemand> demands =
        [
            PlanDemand.ForPurchase(Ore, DemandQuality.Any, 15),
            PlanDemand.ForPurchase(Ore, DemandQuality.HighQuality, 2),
            PlanDemand.ForPurchase(Cloth, DemandQuality.HighQuality, 3),
            PlanDemand.ForPurchase(Shard, DemandQuality.Any, 50),
        ];
        var oldest = Fixtures.Offer(Fixtures.Delta, Cloth, 3, 123_456, true, Hexadecimal(SecretListing));
        var request = Fixtures.Request(
            Fixtures.Settings(),
            demands,
            Fixtures.Offer(Fixtures.Alpha, Ore, 5, 1_000, key: ListingKeys.ForGameListing(SecretListing)),
            Fixtures.Offer(Fixtures.Alpha, Ore, 10, 1_000, key: ListingKeys.ForGameListing(OtherSecretListing)),
            Fixtures.Offer(Fixtures.Alpha, Ore, 2, 2_000, true, ListingKeys.ForGameListing(ThirdSecretListing)),
            oldest with { ObservedAtUtc = Fixtures.ObservedAt.AddHours(-3) });
        var plan = Fixtures.NewPlanner().Plan(request);
        Assert.Single(plan.Unresolved);
        return plan;
    }

    private static int Count(string[] lines, string line)
    {
        var count = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index] == line)
            {
                count++;
            }
        }

        return count;
    }
}
