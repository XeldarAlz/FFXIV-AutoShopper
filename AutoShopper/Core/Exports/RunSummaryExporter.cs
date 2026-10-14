using System.Collections.Immutable;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AutoShopper.Core.Execution.Model;

namespace AutoShopper.Core.Exports;

internal static class RunSummaryExporter
{
    private const int CharactersPerLine = 64;
    private const int HeaderCharacters = 512;

    // Pasted, not served as HTML, so names keep their own letters; one newline everywhere keeps the output identical
    // across platforms.
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(RunSummary summary)
    {
        return JsonSerializer.Serialize(summary, JsonOptions);
    }

    public static string ToText(RunSummary summary)
    {
        var builder = new StringBuilder(EstimateCapacity(summary));
        AppendHeader(builder, summary);
        AppendSpend(builder, summary.Spend);
        AppendAttempts(builder, "Bought", summary.Purchases, "nothing");
        AppendExcess(builder, summary.Excess);
        AppendShortfalls(builder, summary.Unresolved);
        AppendAttempts(builder, "Rejected", summary.Rejected);
        AppendAttempts(builder, "Unknown result, reconcile before buying more", summary.Unknown);
        AppendAttempts(builder, "Not sent", summary.NotSent);
        if (summary.Diagnostics is { } diagnostics)
        {
            AppendDiagnostics(builder, diagnostics);
        }

        return builder.ToString();
    }

    private static void AppendHeader(StringBuilder builder, RunSummary summary)
    {
        builder.Append("Auto Shopper run summary").Append(ExportText.NewLine);
        builder.Append("Run ").Append(summary.RunId).Append(ExportText.NewLine);
        builder.Append("Started: ");
        AppendMoment(builder, summary.StartedAtUtc, "unknown").Append(ExportText.NewLine);
        builder.Append("Ended: ");
        AppendMoment(builder, summary.EndedAtUtc, "not yet").Append(ExportText.NewLine);
        builder.Append("Outcome: ").Append(OutcomeText(summary.Outcome)).Append(ExportText.NewLine);
    }

    private static void AppendSpend(StringBuilder builder, SummarySpend spend)
    {
        builder.Append(ExportText.NewLine).Append("Spend").Append(ExportText.NewLine).Append(ExportText.Indent).Append("Confirmed: ");
        ExportText.AppendGil(builder, spend.Confirmed);
        if (spend.Cap > 0)
        {
            builder.Append(" of a ");
            ExportText.AppendGil(builder, spend.Cap).Append(" cap");
        }

        builder.Append(ExportText.NewLine);
        if (spend.Reserved > 0)
        {
            builder.Append(ExportText.Indent).Append("Awaiting reconciliation: ");
            ExportText.AppendGil(builder, spend.Reserved).Append(ExportText.NewLine);
        }

        if (spend.Cap > 0)
        {
            builder.Append(ExportText.Indent).Append("Left under the cap: ");
            ExportText.AppendGil(builder, spend.Remaining).Append(ExportText.NewLine);
        }
    }

    private static void AppendAttempts(StringBuilder builder, string title, ImmutableArray<SummaryAttempt> attempts, string? emptyText = null)
    {
        if (attempts.IsDefaultOrEmpty)
        {
            if (emptyText is not null)
            {
                builder.Append(ExportText.NewLine).Append(title).Append(": ").Append(emptyText).Append(ExportText.NewLine);
            }

            return;
        }

        AppendSectionTitle(builder, title, attempts.Length);
        for (var index = 0; index < attempts.Length; index++)
        {
            var attempt = attempts[index];
            builder.Append(ExportText.Indent);
            ExportText.AppendNumber(builder, attempt.Quantity).Append(" x ").Append(attempt.ItemName);
            ExportText.AppendQuality(builder, attempt.IsHq).Append(" for ");
            ExportText.AppendGil(builder, attempt.Payable).Append(" on ").Append(attempt.WorldName);
            if (attempt.ReasonCode != 0)
            {
                builder.Append(", reason code ");
                ExportText.AppendCode(builder, attempt.ReasonCode);
            }

            if (attempt.Reconciled)
            {
                builder.Append(", settled by reconciliation");
            }

            builder.Append(ExportText.NewLine);
        }
    }

    private static void AppendExcess(StringBuilder builder, ImmutableArray<SummaryExcess> excess)
    {
        if (excess.IsDefaultOrEmpty)
        {
            return;
        }

        var total = 0;
        for (var index = 0; index < excess.Length; index++)
        {
            total += excess[index].Quantity;
        }

        AppendSectionTitle(builder, "Extra units bought", total);
        for (var index = 0; index < excess.Length; index++)
        {
            var entry = excess[index];
            builder.Append(ExportText.Indent).Append(entry.ItemName);
            ExportText.AppendQuality(builder, entry.Quality).Append(": ");
            ExportText.AppendNumber(builder, entry.Quantity).Append(ExportText.NewLine);
        }
    }

    private static void AppendShortfalls(StringBuilder builder, ImmutableArray<SummaryShortfall> shortfalls)
    {
        if (shortfalls.IsDefaultOrEmpty)
        {
            builder.Append(ExportText.NewLine).Append("Still needed: nothing, every line was bought").Append(ExportText.NewLine);
            return;
        }

        AppendSectionTitle(builder, "Still needed", shortfalls.Length);
        for (var index = 0; index < shortfalls.Length; index++)
        {
            var entry = shortfalls[index];
            builder.Append(ExportText.Indent).Append(entry.ItemName);
            ExportText.AppendQuality(builder, entry.Quality).Append(": ");
            ExportText.AppendNumber(builder, entry.MissingQuantity).Append(" of ");
            ExportText.AppendNumber(builder, entry.RequiredQuantity).Append(" missing, ").Append(ShortfallText(entry.Reason)).Append(ExportText.NewLine);
        }
    }

    private static void AppendDiagnostics(StringBuilder builder, RunDiagnostics diagnostics)
    {
        builder.Append(ExportText.NewLine).Append("Diagnostics").Append(ExportText.NewLine).Append(ExportText.Indent).Append("Journal: last sequence ");
        ExportText.AppendCode(builder, diagnostics.LastSequence).Append(diagnostics.TornTail ? ", torn last line" : ", complete").Append(ExportText.NewLine);
        builder.Append(ExportText.Indent).Append("Attempts: ");
        ExportText.AppendNumber(builder, diagnostics.AttemptCount).Append(", failed ");
        ExportText.AppendNumber(builder, diagnostics.FailedAttemptCount).Append(", excluded listings ");
        ExportText.AppendNumber(builder, diagnostics.ExcludedListingCount).Append(ExportText.NewLine);
        var anomalies = diagnostics.Anomalies;
        for (var index = 0; index < anomalies.Length; index++)
        {
            var anomaly = anomalies[index];
            builder.Append(ExportText.Indent).Append("Anomaly ").Append(anomaly.Kind.ToString()).Append(": position ");
            ExportText.AppendCode(builder, anomaly.Position).Append(", sequence ");
            ExportText.AppendCode(builder, anomaly.Sequence);
            if (anomaly.Attempt.Length > 0)
            {
                builder.Append(", attempt ").Append(anomaly.Attempt);
            }

            builder.Append(ExportText.NewLine);
        }

        var attempts = diagnostics.Attempts;
        for (var index = 0; index < attempts.Length; index++)
        {
            var attempt = attempts[index];
            builder.Append(ExportText.Indent).Append("Attempt ").Append(attempt.Attempt).Append(": ").Append(attempt.Status.ToString());
            if (attempt.Listing.Length > 0)
            {
                builder.Append(", listing ").Append(attempt.Listing);
            }

            builder.Append(", world ");
            ExportText.AppendCode(builder, attempt.WorldId).Append(", sequence ");
            ExportText.AppendCode(builder, attempt.FirstSequence).Append(ExportText.NewLine);
        }
    }

    private static void AppendSectionTitle(StringBuilder builder, string title, int count)
    {
        builder.Append(ExportText.NewLine).Append(title).Append(" (");
        ExportText.AppendNumber(builder, count).Append(')').Append(ExportText.NewLine);
    }

    private static StringBuilder AppendMoment(StringBuilder builder, DateTime? atUtc, string missingText)
    {
        return atUtc is { } moment ? ExportText.AppendTimestamp(builder, moment) : builder.Append(missingText);
    }

    private static int EstimateCapacity(RunSummary summary)
    {
        var lineCount = summary.Purchases.Length + summary.Excess.Length + summary.Unresolved.Length + summary.Rejected.Length
            + summary.Unknown.Length + summary.NotSent.Length;
        if (summary.Diagnostics is { } diagnostics)
        {
            lineCount += diagnostics.Anomalies.Length + diagnostics.Attempts.Length;
        }

        return HeaderCharacters + CharactersPerLine * lineCount;
    }

    private static string OutcomeText(RunState state)
    {
        return state switch
        {
            RunState.Completed => "Completed",
            RunState.CompletedPartial => "Completed with items missing",
            RunState.Stopped => "Stopped",
            RunState.Failed => "Failed",
            RunState.NeedsReconciliation => "Needs reconciliation",
            RunState.Paused => "Paused, not finished",
            RunState.Idle => "Not started",
            _ => "In progress",
        };
    }

    private static string ShortfallText(ShortfallReason reason)
    {
        return reason switch
        {
            ShortfallReason.AwaitingReconciliation => "a purchase result is unknown",
            ShortfallReason.PurchasesFailed => "purchases were rejected or not sent",
            _ => "no purchase was made",
        };
    }
}
