using System.Collections.Immutable;
using System.Text;
using AutoShopper.Core.Planning;
using AutoShopper.Core.Shared;

namespace AutoShopper.Core.Exports;

// A checklist names worlds, items, quantities and estimates only; listing keys stay inside the plan.
internal static class PlanChecklistExporter
{
    private const int CharactersPerItem = 64;
    private const int CharactersPerSection = 256;
    private const string MarkdownBullet = "- ";

    public static string ToText(PurchasePlan plan, IExportNames names, DateTime nowUtc)
    {
        var builder = new StringBuilder(EstimateCapacity(plan));
        builder.Append("Shopping checklist, planned ");
        ExportText.AppendTimestamp(builder, plan.Provenance.PlannedAtUtc).Append(ExportText.NewLine);
        AppendTextStops(builder, plan, names);
        AppendTextMissing(builder, plan, names);
        builder.Append(ExportText.NewLine).Append("Estimated totals (");
        ExportText.AppendPriceAge(builder, plan.Provenance.OldestObservationUtc, plan.Provenance.NewestObservationUtc, nowUtc)
            .Append(", the board may differ)").Append(ExportText.NewLine);
        AppendTotals(builder, plan, ExportText.Indent, false);
        return builder.ToString();
    }

    public static string ToMarkdown(PurchasePlan plan, IExportNames names, DateTime nowUtc)
    {
        var builder = new StringBuilder(EstimateCapacity(plan) * 2);
        builder.Append("# Shopping checklist").Append(ExportText.NewLine).Append(ExportText.NewLine).Append("Planned ");
        ExportText.AppendTimestamp(builder, plan.Provenance.PlannedAtUtc)
            .Append(". Every amount is an estimate from market data.").Append(ExportText.NewLine);
        AppendMarkdownStops(builder, plan, names);
        AppendMarkdownMissing(builder, plan, names);
        builder.Append(ExportText.NewLine).Append("## Estimated totals").Append(ExportText.NewLine).Append(ExportText.NewLine).Append('_');
        ExportText.AppendPriceAge(builder, plan.Provenance.OldestObservationUtc, plan.Provenance.NewestObservationUtc, nowUtc)
            .Append(", the board may differ_").Append(ExportText.NewLine).Append(ExportText.NewLine);
        AppendTotals(builder, plan, MarkdownBullet, true);
        return builder.ToString();
    }

    private static List<ChecklistItem> GroupByDemand(ImmutableArray<PlanLine> lines)
    {
        var items = new List<ChecklistItem>(lines.Length);
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var itemIndex = IndexOf(items, line.Demand);
            if (itemIndex < 0)
            {
                items.Add(new ChecklistItem(line.Demand, line.Offer.Quantity, 1, line.Payable));
                continue;
            }

            var item = items[itemIndex];
            items[itemIndex] = item with
            {
                Quantity = item.Quantity + line.Offer.Quantity,
                ListingCount = item.ListingCount + 1,
                Estimate = Gil.SaturatingAdd(item.Estimate, line.Payable),
            };
        }

        items.Sort(static (left, right) => left.Demand.CompareTo(right.Demand));
        return items;
    }

    private static void AppendTextStops(StringBuilder builder, PurchasePlan plan, IExportNames names)
    {
        var stops = plan.Stops;
        if (stops.IsDefaultOrEmpty)
        {
            builder.Append(ExportText.NewLine).Append("No stops: nothing in this plan can be bought.").Append(ExportText.NewLine);
            return;
        }

        for (var stopIndex = 0; stopIndex < stops.Length; stopIndex++)
        {
            var stop = stops[stopIndex];
            builder.Append(ExportText.NewLine);
            AppendStopTitle(builder, stopIndex, stops.Length, stop.World, names, false).Append(ExportText.NewLine);
            var items = GroupByDemand(stop.Lines);
            for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                var item = items[itemIndex];
                builder.Append(ExportText.Indent).Append("[ ] ");
                ExportText.AppendNumber(builder, item.Quantity).Append(" x ").Append(ExportText.ItemName(names, item.Demand.ItemId));
                ExportText.AppendQuality(builder, item.Demand.Quality).Append(", ");
                ExportText.AppendCounted(builder, item.ListingCount, "listing", "listings").Append(", about ");
                ExportText.AppendGil(builder, item.Estimate).Append(ExportText.NewLine);
            }

            builder.Append(ExportText.Indent).Append("Stop estimate: ");
            ExportText.AppendGil(builder, stop.PurchaseEstimate).Append(ExportText.NewLine);
        }
    }

    private static void AppendTextMissing(StringBuilder builder, PurchasePlan plan, IExportNames names)
    {
        var unresolved = plan.Unresolved;
        if (unresolved.IsDefaultOrEmpty)
        {
            return;
        }

        builder.Append(ExportText.NewLine).Append("Not covered (");
        ExportText.AppendNumber(builder, unresolved.Length).Append(')').Append(ExportText.NewLine);
        for (var index = 0; index < unresolved.Length; index++)
        {
            var entry = unresolved[index];
            builder.Append(ExportText.Indent).Append(ExportText.ItemName(names, entry.Demand.ItemId));
            ExportText.AppendQuality(builder, entry.Demand.Quality).Append(": ");
            ExportText.AppendNumber(builder, entry.MissingQuantity).Append(" of ");
            ExportText.AppendNumber(builder, entry.RequiredQuantity).Append(" missing, ").Append(ReasonText(entry.Reason)).Append(ExportText.NewLine);
        }
    }

    private static void AppendMarkdownStops(StringBuilder builder, PurchasePlan plan, IExportNames names)
    {
        var stops = plan.Stops;
        if (stops.IsDefaultOrEmpty)
        {
            builder.Append(ExportText.NewLine).Append("No stops: nothing in this plan can be bought.").Append(ExportText.NewLine);
            return;
        }

        for (var stopIndex = 0; stopIndex < stops.Length; stopIndex++)
        {
            var stop = stops[stopIndex];
            builder.Append(ExportText.NewLine).Append("## ");
            AppendStopTitle(builder, stopIndex, stops.Length, stop.World, names, true).Append(ExportText.NewLine).Append(ExportText.NewLine);
            builder.Append("| Quantity | Item | Listings | Estimate with fee (gil) |").Append(ExportText.NewLine);
            builder.Append("| ---: | --- | ---: | ---: |").Append(ExportText.NewLine);
            var items = GroupByDemand(stop.Lines);
            for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                var item = items[itemIndex];
                builder.Append("| ");
                ExportText.AppendNumber(builder, item.Quantity).Append(" | ");
                ExportText.AppendMarkdown(builder, ExportText.ItemName(names, item.Demand.ItemId));
                ExportText.AppendQuality(builder, item.Demand.Quality).Append(" | ");
                ExportText.AppendNumber(builder, item.ListingCount).Append(" | ");
                ExportText.AppendNumber(builder, item.Estimate).Append(" |").Append(ExportText.NewLine);
            }

            builder.Append("| | **Stop estimate** | | **");
            ExportText.AppendNumber(builder, stop.PurchaseEstimate).Append("** |").Append(ExportText.NewLine);
        }
    }

    private static void AppendMarkdownMissing(StringBuilder builder, PurchasePlan plan, IExportNames names)
    {
        var unresolved = plan.Unresolved;
        if (unresolved.IsDefaultOrEmpty)
        {
            return;
        }

        builder.Append(ExportText.NewLine).Append("## Not covered").Append(ExportText.NewLine).Append(ExportText.NewLine);
        builder.Append("| Item | Missing | Needed | Reason |").Append(ExportText.NewLine);
        builder.Append("| --- | ---: | ---: | --- |").Append(ExportText.NewLine);
        for (var index = 0; index < unresolved.Length; index++)
        {
            var entry = unresolved[index];
            builder.Append("| ");
            ExportText.AppendMarkdown(builder, ExportText.ItemName(names, entry.Demand.ItemId));
            ExportText.AppendQuality(builder, entry.Demand.Quality).Append(" | ");
            ExportText.AppendNumber(builder, entry.MissingQuantity).Append(" | ");
            ExportText.AppendNumber(builder, entry.RequiredQuantity).Append(" | ").Append(ReasonText(entry.Reason)).Append(" |").Append(ExportText.NewLine);
        }
    }

    private static StringBuilder AppendStopTitle(StringBuilder builder, int stopIndex, int stopCount, PlanWorld world, IExportNames names, bool markdown)
    {
        builder.Append("Stop ");
        ExportText.AppendNumber(builder, stopIndex + 1).Append(" of ");
        ExportText.AppendNumber(builder, stopCount).Append(": ");
        AppendName(builder, ExportText.WorldName(names, world.WorldId, world.Name), markdown).Append(" (");
        return AppendName(builder, ExportText.DataCenterName(names, world.DataCenterId), markdown).Append(')');
    }

    private static void AppendTotals(StringBuilder builder, PurchasePlan plan, string bullet, bool emphasize)
    {
        var totals = plan.Totals;
        var emphasis = emphasize ? "**" : string.Empty;
        builder.Append(bullet).Append("Purchases with fees: ").Append(emphasis);
        ExportText.AppendGil(builder, totals.PurchaseEstimate).Append(emphasis).Append(ExportText.NewLine);
        builder.Append(bullet).Append("Buyer fees included: ");
        ExportText.AppendGil(builder, totals.FeeEstimate).Append(ExportText.NewLine);
        builder.Append(bullet).Append("Items planned: ");
        ExportText.AppendNumber(builder, totals.PlannedQuantity).Append(" in ");
        ExportText.AppendCounted(builder, totals.StopCount, "stop", "stops").Append(ExportText.NewLine);
        if (totals.ExcessQuantity > 0)
        {
            builder.Append(bullet).Append("Extra units from whole stacks: ");
            ExportText.AppendNumber(builder, totals.ExcessQuantity).Append(ExportText.NewLine);
        }

        if (totals.MissingQuantity > 0)
        {
            builder.Append(bullet).Append("Missing units: ");
            ExportText.AppendNumber(builder, totals.MissingQuantity).Append(ExportText.NewLine);
        }
    }

    private static StringBuilder AppendName(StringBuilder builder, string name, bool markdown)
    {
        return markdown ? ExportText.AppendMarkdown(builder, name) : builder.Append(name);
    }

    private static int IndexOf(List<ChecklistItem> items, DemandKey demand)
    {
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].Demand == demand)
            {
                return index;
            }
        }

        return -1;
    }

    private static int EstimateCapacity(PurchasePlan plan)
    {
        var lineCount = plan.Unresolved.IsDefault ? 0 : plan.Unresolved.Length;
        var stops = plan.Stops.IsDefault ? ImmutableArray<PlanStop>.Empty : plan.Stops;
        for (var index = 0; index < stops.Length; index++)
        {
            lineCount += stops[index].Lines.Length;
        }

        return CharactersPerSection * (stops.Length + 2) + CharactersPerItem * lineCount;
    }

    private static string ReasonText(UnresolvedReason reason)
    {
        return reason switch
        {
            UnresolvedReason.InvalidDemand => "the line's values are out of range",
            UnresolvedReason.NoListings => "no listings on the chosen worlds",
            UnresolvedReason.AboveUnitCap => "listings cost more than the line's max per unit",
            UnresolvedReason.InsufficientStock => "not enough units are listed",
            UnresolvedReason.NoFittingStacks => "no mix of whole stacks fits the quantity",
            UnresolvedReason.TakenByStricterDemand => "the listings went to a stricter line for the same item",
            UnresolvedReason.StopLimit => "the most stops setting cut it",
            UnresolvedReason.SearchLimit => "the search stopped before covering it",
            _ => "not covered",
        };
    }

    private readonly record struct ChecklistItem(DemandKey Demand, int Quantity, int ListingCount, long Estimate);
}
